# Phase 10: Metrics - Research

**Researched:** 2026-03-06
**Domain:** SNMP community string convention, metric label taxonomy, channel architecture, readiness checks
**Confidence:** HIGH

## Summary

This phase redesigns the SNMP trap and poll paths to use a `Simetra.{DeviceName}` community string convention for both authentication and device identity extraction, replaces `site_name` with `host_name` (machine hostname), simplifies the channel architecture from per-device to a single shared channel for traps, updates readiness checks to not require devices, and ensures consistent metric labeling across trap and poll paths.

The changes are entirely internal refactoring -- no new libraries are needed. The work touches configuration classes, the trap listener, the channel manager, the metric factory, pipeline behaviors, health checks, log enrichment, and the console formatter. All decisions are locked by CONTEXT.md; the only discretionary items are: (1) whether to keep `IDeviceChannelManager` interface or simplify, (2) how to signal trap listener "bound" status, (3) CardinalityAuditService updates, and (4) test strategy.

**Primary recommendation:** Execute as a series of atomic refactoring steps: config changes first, then trap path rewrite, then poll path alignment, then metric label updates, then readiness/health check updates, then test updates.

## Standard Stack

No new libraries needed. This phase is pure refactoring of existing code.

### Core (Unchanged)
| Library | Purpose | Relevance to Phase 10 |
|---------|---------|----------------------|
| SharpSnmpLib | SNMP PDU parsing | Community string extraction from `TrapV2Message.Community()` |
| System.Threading.Channels | Backpressure buffering | Refactor from per-device to single shared channel |
| System.Diagnostics.Metrics | OTel instrument API | TagList label changes (`site_name` -> `host_name`, `agent` -> `device_name` + `ip`) |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | OTLP export | No changes needed |
| MediatR 12.x | Pipeline behaviors | `SnmpOidReceived` property changes propagate through pipeline |

## Architecture Patterns

### Current vs Target Architecture

**Trap path (current):**
```
UDP datagram -> TrapListenerService
  -> DeviceRegistry.TryGetDevice(senderIp) [BLOCKS unknown IPs]
  -> Verify community string matches device config
  -> DeviceChannelManager.GetWriter(deviceName) [PER-DEVICE channel]
  -> ChannelConsumerService (one task per device)
  -> ISender.Send(SnmpOidReceived)
```

**Trap path (target):**
```
UDP datagram -> TrapListenerService
  -> Parse community string from PDU
  -> Validate "Simetra." prefix [DROP + Debug log if invalid]
  -> Extract device_name from community string (after "Simetra.")
  -> Write VarbindEnvelope to SINGLE shared channel
  -> ChannelConsumerService (single consumer task)
  -> ISender.Send(SnmpOidReceived)
```

**Poll path (current):**
```
MetricPollJob -> DeviceRegistry.TryGetDeviceByName(name)
  -> OctetString(device.CommunityString) [from config]
  -> SNMP GET
  -> ISender.Send(SnmpOidReceived { DeviceName = device.Name, AgentIp = device.IpAddress })
```

**Poll path (target):**
```
MetricPollJob -> DeviceRegistry.TryGetDeviceByName(name)
  -> community = "Simetra." + device.Name [derived, not from config]
  -> SNMP GET
  -> ISender.Send(SnmpOidReceived { DeviceName = device.Name, AgentIp = device.IpAddress })
```

### Pattern 1: Community String Convention

**What:** `Simetra.{DeviceName}` is both the auth token and the device identity carrier.
**Validation:** Must start with `Simetra.` (case-sensitive, `StringComparison.Ordinal`). Reject otherwise.
**Extraction:** `community.Substring("Simetra.".Length)` or `community[8..]` (since "Simetra." is 8 chars).

```csharp
// Community string parsing helper
private const string CommunityPrefix = "Simetra.";

internal static bool TryExtractDeviceName(string community, out string deviceName)
{
    if (community.StartsWith(CommunityPrefix, StringComparison.Ordinal)
        && community.Length > CommunityPrefix.Length)
    {
        deviceName = community[CommunityPrefix.Length..];
        return true;
    }
    deviceName = string.Empty;
    return false;
}
```

### Pattern 2: Single Shared Channel (replacing per-device channels)

**What:** One `BoundedChannel<VarbindEnvelope>` shared across all trap sources.
**Why:** Traps can come from any device (no pre-registration required). Per-device channels require knowing devices at startup.
**Design:**

```csharp
// Simplified channel manager or direct channel injection
var options = new BoundedChannelOptions(capacity)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleWriter = false,  // multiple trap sources
    SingleReader = true,   // single consumer
    AllowSynchronousContinuations = false,
};
```

**Discretion recommendation:** Replace `IDeviceChannelManager` with a simpler `ITrapChannel` interface:
```csharp
public interface ITrapChannel
{
    ChannelWriter<VarbindEnvelope> Writer { get; }
    ChannelReader<VarbindEnvelope> Reader { get; }
    void Complete();
    Task WaitForDrainAsync(CancellationToken cancellationToken);
}
```
This is cleaner than keeping `IDeviceChannelManager` with a single entry. The `DeviceNames` property, `GetWriter(name)`, and `GetReader(name)` methods become meaningless with a shared channel.

### Pattern 3: Hostname Resolution

**What:** Replace `SiteOptions.Name` with machine hostname for metric/log labels.
**Resolution order:** `HOSTNAME` env var (set by K8s) -> `Environment.MachineName` (local dev).
**Where used:** Already partially done in `ServiceCollectionExtensions` for `serviceInstanceId`. Extend to metric labels.

```csharp
// Hostname helper (reusable across metric factory, log enrichment, console formatter)
internal static string ResolveHostName()
    => Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
```

### Pattern 4: Metric Label Taxonomy Change

**Current labels (snmp_gauge, snmp_info):**
| Label | Source |
|-------|--------|
| `site_name` | `SiteOptions.Name` |
| `metric_name` | OID map resolution |
| `oid` | raw OID |
| `agent` | `DeviceName ?? AgentIp.ToString()` |
| `source` | "poll" or "trap" |
| `snmp_type` | SNMP type code string |

**Target labels:**
| Label | Source |
|-------|--------|
| `host_name` | `HOSTNAME` env var or `Environment.MachineName` |
| `device_name` | Extracted from community string (after `Simetra.`) |
| `ip` | Sender IP (trap) or polled device IP (poll) |
| `metric_name` | OID map resolution (unchanged) |
| `oid` | raw OID (unchanged) |
| `source` | "poll" or "trap" (unchanged) |
| `snmp_type` | SNMP type code string (unchanged) |

**Breaking change:** `agent` label splits into `device_name` + `ip`. `site_name` renamed to `host_name`. Any existing Grafana dashboards or Prometheus alerting rules referencing `site_name` or `agent` will break.

### Anti-Patterns to Avoid
- **Mixing old and new label names in the same deployment:** All label changes must be atomic. Do not have some metrics with `site_name` and others with `host_name`.
- **Keeping IDeviceRegistry as a trap-path dependency:** Traps no longer need device registry lookup. The trap listener should not import or depend on `IDeviceRegistry`.
- **Leaving `CommunityString` as a configurable field while also using the convention:** Remove the config fields completely to prevent confusion.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Community string validation | Custom regex | Simple `string.StartsWith` + length check | Prefix match is sufficient; regex is overkill |
| Hostname resolution | Complex detection logic | `Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName` | Already used in `ServiceCollectionExtensions` lines 77-78, 228-230 |
| Channel backpressure | Custom queue | `BoundedChannel<T>` with `DropOldest` | Already proven in current `DeviceChannelManager` |

## Common Pitfalls

### Pitfall 1: ISnmpMetricFactory Interface Change Cascade
**What goes wrong:** Changing `RecordGauge`/`RecordInfo` signatures propagates to `OtelMetricHandler`, `TestSnmpMetricFactory`, `PipelineIntegrationTests`, and all test stubs.
**Why it happens:** The `agent` parameter must split into `deviceName` + `ip`, and `ISnmpMetricFactory` is the contract boundary.
**How to avoid:** Change the interface first. Then update all callers and test doubles in one pass. The compiler will catch all misses.
**Warning signs:** Tests compile but produce wrong label values because parameter order shifted.

### Pitfall 2: DeviceRegistry Still Required for Poll Path
**What goes wrong:** Removing `IDeviceRegistry` entirely breaks the poll path, which still needs device lookup by name for IP resolution and poll group definitions.
**Why it happens:** The trap path no longer needs the registry, but the poll path does.
**How to avoid:** Only remove `IDeviceRegistry` dependency from `SnmpTrapListenerService` and `ValidationBehavior` (for trap source). Keep it in `MetricPollJob`, `CardinalityAuditService`, and `DeviceChannelManager` (if retained for poll path).

### Pitfall 3: SiteOptions.Name Still Used Elsewhere
**What goes wrong:** Removing `SiteOptions.Name` as required breaks the options validation at startup, but other code still references it.
**Why it happens:** `SiteOptions.Name` is used in: `SnmpMetricFactory`, `PipelineMetricService`, `SnmpLogEnrichmentProcessor`, `SnmpConsoleFormatter`, and `SiteOptionsValidator`.
**How to avoid:**
1. Make `SiteOptions.Name` optional (remove `[Required]` and `required` keyword)
2. Remove `SiteOptionsValidator` check for Name
3. Replace all usages of `_siteName` from `SiteOptions.Name` with hostname resolution
4. Consider keeping `SiteOptions` class but removing `Name` or making it optional with no usage

### Pitfall 4: PipelineMetricService Also Uses site_name
**What goes wrong:** Forgetting to update `PipelineMetricService` which tags all 11 pipeline counters with `site_name`.
**Why it happens:** The CONTEXT.md says "replace `site_name` with `host_name` everywhere" -- PipelineMetricService is easy to miss since it's separate from the business metric factory.
**How to avoid:** Search all occurrences of `site_name` and `_siteName` across the entire codebase. Both `SnmpMetricFactory` AND `PipelineMetricService` need updating.

### Pitfall 5: ChannelConsumerService Architecture Change
**What goes wrong:** Current `ChannelConsumerService` spawns one task per device via `_channelManager.DeviceNames`. With a single shared channel, it should be a single consumer loop.
**Why it happens:** The consumer service is tightly coupled to `IDeviceChannelManager.DeviceNames`.
**How to avoid:** Rewrite `ChannelConsumerService` to consume from the single shared channel. Remove the `DeviceNames` iteration pattern.

### Pitfall 6: GracefulShutdownService Channel Drain
**What goes wrong:** `GracefulShutdownService` calls `_channelManager.CompleteAll()` and `_channelManager.WaitForDrainAsync()`. These must work with the new single-channel abstraction.
**Why it happens:** The shutdown service depends on `IDeviceChannelManager`.
**How to avoid:** Update `GracefulShutdownService` to use the new `ITrapChannel` (or equivalent) interface. Both `CompleteAll()` -> `Complete()` and `WaitForDrainAsync()` must still work.

### Pitfall 7: ValidationBehavior Device Lookup for Traps
**What goes wrong:** Current `ValidationBehavior` does `_deviceRegistry.TryGetDevice(msg.AgentIp)` when `DeviceName` is null (trap path). With the new design, traps always have `DeviceName` set (extracted from community string by the listener).
**Why it happens:** The trap listener now extracts device name before writing to channel, so `DeviceName` is always set.
**How to avoid:** Remove the device registry IP lookup from `ValidationBehavior`. The `DeviceName is null` case should no longer occur for traps (listener pre-populates it). For polls, `DeviceName` is always set by `MetricPollJob`. The entire "unknown device" rejection path in `ValidationBehavior` may become dead code.

### Pitfall 8: SnmpTrapListenerService.StopAsync Calls channelManager.CompleteAll()
**What goes wrong:** The current `StopAsync` override calls `_channelManager.CompleteAll()`. The `GracefulShutdownService` also calls it. With a single channel, double-complete is fine (`TryComplete` is idempotent), but the dependency needs updating.
**How to avoid:** Update `SnmpTrapListenerService.StopAsync` to call the new channel's `Complete()`. Verify idempotency.

## Code Examples

### Community String Parsing in Trap Listener

```csharp
// In SnmpTrapListenerService.ProcessDatagram (refactored)
internal void ProcessDatagram(UdpReceiveResult result)
{
    IList<ISnmpMessage> messages;
    try
    {
        messages = MessageFactory.ParseMessages(result.Buffer, 0, result.Buffer.Length, _userRegistry);
    }
    catch (Exception ex)
    {
        _logger.LogWarning("Malformed SNMP packet from {SourceIp}: {Error}",
            result.RemoteEndPoint.Address, ex.Message);
        return;
    }

    foreach (var message in messages)
    {
        if (message is not TrapV2Message trapV2)
            continue;

        var senderIp = result.RemoteEndPoint.Address.MapToIPv4();
        var receivedCommunity = trapV2.Community().ToString();

        // Validate Simetra.* convention and extract device name
        if (!TryExtractDeviceName(receivedCommunity, out var deviceName))
        {
            _logger.LogDebug(
                "Trap dropped: invalid community string from {SourceIp}",
                senderIp);
            return; // or continue, depending on multi-message handling
        }

        // Route each varbind to the shared channel
        foreach (var variable in trapV2.Variables())
        {
            var envelope = new VarbindEnvelope(
                Oid: variable.Id.ToString(),
                Value: variable.Data,
                TypeCode: variable.Data.TypeCode,
                AgentIp: senderIp,
                DeviceName: deviceName);

            _channelWriter.TryWrite(envelope);
        }
    }
}
```

### Updated ISnmpMetricFactory Interface

```csharp
public interface ISnmpMetricFactory
{
    void RecordGauge(string metricName, string oid, string deviceName, string ip,
        string source, string snmpType, double value);

    void RecordInfo(string metricName, string oid, string deviceName, string ip,
        string source, string snmpType, string value);
}
```

### Updated SnmpMetricFactory TagList

```csharp
public void RecordGauge(string metricName, string oid, string deviceName, string ip,
    string source, string snmpType, double value)
{
    var gauge = GetOrCreateGauge("snmp_gauge");
    gauge.Record(value, new TagList
    {
        { "host_name", _hostName },      // was "site_name"
        { "device_name", deviceName },    // was "agent" (device name only)
        { "ip", ip },                     // NEW: sender/target IP
        { "metric_name", metricName },
        { "oid", oid },
        { "source", source },
        { "snmp_type", snmpType }
    });
}
```

### Updated OtelMetricHandler Dispatch

```csharp
// agent was: notification.DeviceName ?? notification.AgentIp.ToString()
// Now split into two distinct values:
var deviceName = notification.DeviceName!;  // always set by listener/poll job
var ip = notification.AgentIp.ToString();

_metricFactory.RecordGauge(
    metricName,
    notification.Oid,
    deviceName,
    ip,
    source,
    "integer32",
    ((Integer32)notification.Value).ToInt32());
```

### Updated SnmpOidReceived (no structural change needed)

The existing `SnmpOidReceived` already has both `DeviceName` and `AgentIp`. The handler just needs to pass both separately instead of coalescing them into a single `agent` string.

### Readiness Health Check (updated)

```csharp
public sealed class ReadinessHealthCheck : IHealthCheck
{
    private readonly ITrapListenerStatus _trapListenerStatus;  // new interface
    private readonly ISchedulerFactory _schedulerFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Check trap listener UDP socket is bound
        if (!_trapListenerStatus.IsBound)
            return HealthCheckResult.Unhealthy("Trap listener not bound");

        // Check Quartz scheduler is running
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        if (!scheduler.IsStarted || scheduler.IsShutdown)
            return HealthCheckResult.Unhealthy("Quartz scheduler is not running");

        return HealthCheckResult.Healthy();
    }
}
```

### Trap Listener Bound Status (discretion recommendation)

**Recommendation:** Use a simple `volatile bool` flag exposed via a minimal interface. No need for events or service checks.

```csharp
public interface ITrapListenerStatus
{
    bool IsBound { get; }
}

// In SnmpTrapListenerService:
private volatile bool _isBound;
public bool IsBound => _isBound;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var udpClient = new UdpClient(endpoint);
    _isBound = true;  // signal readiness
    // ... receive loop ...
}
```

Register `SnmpTrapListenerService` as both `IHostedService` and `ITrapListenerStatus` using the same-instance pattern (already demonstrated with `K8sLeaseElection` in `ServiceCollectionExtensions` lines 215-217).

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| `site_name` from config | `host_name` from `HOSTNAME` env var / `MachineName` | No config needed for site identity |
| Per-device community string config | `Simetra.{DeviceName}` convention | Config simplified, identity derived from protocol |
| Per-device BoundedChannels | Single shared BoundedChannel | Accept traps from unknown devices |
| Device registry gate on trap path | Community string prefix validation | No pre-registration needed for traps |
| `agent` label (device name or IP) | `device_name` + `ip` labels (always both) | Consistent, unambiguous labeling |

## Impact Analysis

### Files to Modify

| File | Change | Complexity |
|------|--------|-----------|
| `SiteOptions.cs` | Remove `[Required]` from `Name`, make optional | Low |
| `SiteOptionsValidator.cs` | Remove Name validation | Low |
| `SnmpListenerOptions.cs` | Remove `CommunityString` field | Low |
| `SnmpListenerOptionsValidator.cs` | Remove CommunityString validation | Low |
| `DeviceOptions.cs` | Remove `CommunityString` field | Low |
| `DeviceInfo.cs` | Remove `CommunityString` parameter | Low |
| `DeviceRegistry.cs` | Derive community from name, remove global fallback | Medium |
| `SnmpTrapListenerService.cs` | Rewrite: remove IDeviceRegistry, add community parsing, use shared channel | High |
| `DeviceChannelManager.cs` | Replace with single-channel implementation (or new `TrapChannel` class) | Medium |
| `IDeviceChannelManager.cs` | Replace with `ITrapChannel` interface | Medium |
| `ChannelConsumerService.cs` | Single consumer loop instead of per-device tasks | Medium |
| `SnmpMetricFactory.cs` | Change labels, replace `_siteName` with `_hostName`, update method signatures | Medium |
| `ISnmpMetricFactory.cs` | Update `RecordGauge`/`RecordInfo` signatures | Low |
| `PipelineMetricService.cs` | Replace `_siteName`/`site_name` with `_hostName`/`host_name` | Low |
| `OtelMetricHandler.cs` | Split `agent` into `deviceName` + `ip` in method calls | Medium |
| `SnmpOidReceived.cs` | No structural change needed (already has `DeviceName` + `AgentIp`) | None |
| `ValidationBehavior.cs` | Remove device registry IP lookup for trap path | Medium |
| `ReadinessHealthCheck.cs` | Replace device channel count check with trap listener bound status | Medium |
| `SnmpConsoleFormatter.cs` | Replace `site` with hostname | Low |
| `SnmpLogEnrichmentProcessor.cs` | Replace `site_name` with `host_name`, hostname source | Low |
| `CardinalityAuditService.cs` | Update label taxonomy documentation in log messages | Low |
| `MetricPollJob.cs` | Derive community string: `"Simetra." + device.Name` | Low |
| `ServiceCollectionExtensions.cs` | Update DI wiring for new channel, trap listener status, remove SiteOptions Name requirement | Medium |
| `GracefulShutdownService.cs` | Update channel drain to use new interface | Low |
| `appsettings.json` | Remove `Site.Name`, remove `SnmpListener.CommunityString` | Low |
| `appsettings.Development.json` | Same config cleanup | Low |
| `appsettings.Production.json` | Same config cleanup | Low |

### Test Files to Update

| File | Change | Complexity |
|------|--------|-----------|
| `TestSnmpMetricFactory.cs` | Update method signatures to match new interface | Low |
| `OtelMetricHandlerTests.cs` | Update expected label values | Medium |
| `PipelineIntegrationTests.cs` | Remove `SiteOptions.Name`, remove `SnmpListenerOptions.CommunityString`, update assertions | Medium |
| `SnmpTrapListenerServiceTests.cs` | Rewrite stubs: remove `StubDeviceRegistry`, test community string validation | High |
| `DeviceChannelManagerTests.cs` | Rewrite or replace for single-channel model | High |
| `DeviceRegistryTests.cs` | Update for removed CommunityString config field | Medium |

## Open Questions

1. **Should `SiteOptions` class be removed entirely or just emptied?**
   - What we know: `SiteOptions.PodIdentity` is still used for K8s lease holder identification
   - What's unclear: Whether to keep the class with just `PodIdentity` or move `PodIdentity` elsewhere
   - Recommendation: Keep `SiteOptions` with just `PodIdentity`. Removing the class would cascade into lease election code changes that are out of scope.

2. **CardinalityAuditService: how to handle unbounded device_name from traps?**
   - What we know: Currently, device count is bounded by `IDeviceRegistry.AllDevices.Count`. With traps from any source, `device_name` is unbounded.
   - What's unclear: Whether the audit should account for trap-only devices or only poll-configured devices
   - Recommendation: Audit based on configured devices only (poll path). Add a log note that trap-originated device names are unbounded and not included in the cardinality estimate. This matches the "warn-but-allow" design.

3. **VarbindEnvelope: needs `ip` field for trap path?**
   - What we know: VarbindEnvelope already has `AgentIp` field. `SnmpOidReceived` already has `AgentIp`.
   - Recommendation: No change needed. The IP flows correctly through the existing data types.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: All files listed in Impact Analysis section were read and analyzed directly
- `ServiceCollectionExtensions.cs` lines 77-78, 228-230: Existing hostname resolution pattern
- `DeviceChannelManager.cs`: Current per-device channel implementation
- `SnmpTrapListenerService.cs`: Current trap listener with device registry dependency
- `PipelineMetricService.cs`: All 11 pipeline counters use `site_name` tag

### Secondary (MEDIUM confidence)
- `BoundedChannel<T>` with `DropOldest` is proven stable in the current implementation for per-device channels; single shared channel is the same API with different topology

## Metadata

**Confidence breakdown:**
- Configuration changes: HIGH - Direct code analysis, clear field removal
- Trap path rewrite: HIGH - Architecture well-understood, community string convention is simple
- Label taxonomy: HIGH - Complete mapping documented from current to target
- Channel simplification: HIGH - Same API (`BoundedChannel`), simpler topology
- Readiness check: MEDIUM - Trap listener bound status signaling is new, but pattern exists in codebase (K8sLeaseElection)
- Test impact: HIGH - Every affected test file identified and change described

**Research date:** 2026-03-06
**Valid until:** 2026-04-06 (stable -- internal refactoring, no external dependency changes)
