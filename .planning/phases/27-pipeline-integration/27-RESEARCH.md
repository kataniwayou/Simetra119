# Phase 27: Pipeline Integration - Research

**Researched:** 2026-03-10
**Domain:** MediatR pipeline behaviors, C# in-process fan-out, .NET 9 threading primitives
**Confidence:** HIGH

## Summary

Phase 27 wires the tenant vector registry (built in Phase 26) into the existing MediatR pipeline by adding two new behaviors and removing the now-redundant `IsHeartbeat` special-casing. All implementation is in-process; no new libraries are needed. The codebase already has the exact infrastructure this phase requires: `ITenantVectorRegistry.TryRoute`, `MetricSlotHolder.WriteValue`, `IDeviceRegistry.TryGetDeviceByName`, and `PipelineMetricService` counter registration patterns are all present and verified.

The primary complexity is correctness of message mutation ordering and exception isolation. `ValueExtractionBehavior` must run after `OidResolutionBehavior` (needs MetricName set) and before `TenantVectorFanOutBehavior` (needs ExtractedValue set). The fan-out behavior must catch its own exceptions and unconditionally call `next()` so `OtelMetricHandler` always fires. `OtelMetricHandler` must be refactored to read pre-extracted values instead of doing its own `TypeCode` switch.

Heartbeat normalization is a surgical removal across four files: `SnmpOidReceived.cs` (remove `IsHeartbeat` property and XML doc), `ChannelConsumerService.cs` (remove assignment), `OidResolutionBehavior.cs` (remove `if (msg.IsHeartbeat)` guard), and `OtelMetricHandler.cs` (remove early-return block). The heartbeat OID gets seeded into `OidMapService` at construction time before configurable entries. Three existing tests (`OidResolutionBehaviorTests.SkipsResolution_WhenIsHeartbeat`, `PipelineIntegrationTests` heartbeat test if any) will need to be updated or removed.

**Primary recommendation:** Implement in strict order — (1) heartbeat normalization + OidMapService seed, (2) `ValueExtractionBehavior` + `SnmpOidReceived` property additions, (3) `OtelMetricHandler` refactor to pre-extracted values, (4) `TenantVectorFanOutBehavior` + pipeline counter, (5) DI wiring. This order means each step compiles and tests pass before moving to the next.

## Standard Stack

No new NuGet packages. All work uses libraries already in the project.

### Core (already installed)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| MediatR | 12.x | Pipeline behaviors | Already used; `IPipelineBehavior<TRequest, TResponse>` is the extension point |
| Lextm.SharpSnmpLib | in-use | `SnmpType` enum, `ISnmpData` casting | Already used; `TypeCode` switch already present in `OtelMetricHandler` |
| System.Diagnostics.Metrics | .NET 9 built-in | `Counter<long>` via `PipelineMetricService` | Already used for all PMET counters |
| System.Threading | .NET 9 built-in | `Volatile.Read`/`Volatile.Write` | Already used in `MetricSlotHolder` |

### No Alternatives to Consider
All technology choices are locked. The phase uses existing infrastructure only.

**Installation:** None required.

## Architecture Patterns

### Recommended Project Structure

New files this phase creates:
```
src/SnmpCollector/Pipeline/Behaviors/
├── ValueExtractionBehavior.cs          # NEW: TypeCode switch, sets ExtractedValue + ExtractedStringValue
├── TenantVectorFanOutBehavior.cs       # NEW: looks up route, writes slots, increments counter
src/SnmpCollector/Pipeline/
├── SnmpOidReceived.cs                  # MODIFY: add ExtractedValue + ExtractedStringValue, remove IsHeartbeat
├── MetricSlot.cs                       # MODIFY: add SnmpType TypeCode property
├── MetricSlotHolder.cs                 # MODIFY: WriteValue signature adds SnmpType typeCode param
├── OidMapService.cs                    # MODIFY: seed heartbeat OID at construction time
src/SnmpCollector/Pipeline/Handlers/
├── OtelMetricHandler.cs                # MODIFY: read pre-extracted values, remove IsHeartbeat branch
src/SnmpCollector/Pipeline/Behaviors/
├── OidResolutionBehavior.cs            # MODIFY: remove IsHeartbeat guard
src/SnmpCollector/Services/
├── ChannelConsumerService.cs           # MODIFY: remove IsHeartbeat assignment
src/SnmpCollector/Telemetry/
├── PipelineMetricService.cs            # MODIFY: add snmp.tenantvector.routed counter
src/SnmpCollector/Extensions/
├── ServiceCollectionExtensions.cs      # MODIFY: register two new behaviors in correct order
```

### Pattern 1: Progressive Pipeline Enrichment (existing codebase pattern)

**What:** Each `IPipelineBehavior` enriches `SnmpOidReceived` in-place by setting mutable properties. Downstream behaviors and handlers read what they need without re-doing work.
**When to use:** Always, for this codebase. The message is a sealed class (not a record) specifically so behaviors can mutate it.
**Example (existing pattern — OidResolutionBehavior):**
```csharp
// Source: src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
if (notification is SnmpOidReceived msg)
{
    msg.MetricName = _oidMapService.Resolve(msg.Oid);
}
return await next();
```
`ValueExtractionBehavior` follows the same shape: check `notification is SnmpOidReceived msg`, set `msg.ExtractedValue` and `msg.ExtractedStringValue`, always call `next()`.

### Pattern 2: Open Generic Behavior Registration

**What:** Behaviors are registered with `cfg.AddOpenBehavior(typeof(BehaviorName<,>))`. Registration order = execution order (first registered = outermost = runs first).
**Critical constraint:** The new behaviors must be added in this exact position:
```csharp
cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));           // 1st = outermost
cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));          // 2nd
cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));         // 3rd
cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>));      // 4th
cfg.AddOpenBehavior(typeof(ValueExtractionBehavior<,>));    // 5th — NEW
cfg.AddOpenBehavior(typeof(TenantVectorFanOutBehavior<,>)); // 6th — NEW (innermost before handler)
```
Source: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` lines 350-353 (existing registrations).

### Pattern 3: Fan-Out Behavior Exception Isolation

**What:** The fan-out behavior wraps its own logic in try/catch and always calls `next()` regardless of success or failure. This guarantees `OtelMetricHandler` always fires.
**Critical:** `ExceptionBehavior` (2nd in chain) catches unhandled exceptions from everything inside it. But TenantVectorFanOutBehavior must do its own catch to prevent its exceptions from being counted as pipeline errors AND to ensure next() fires — the behavior is responsible for its own faults.
```csharp
// Pattern from ExceptionBehavior.cs — adapt for fan-out
public async Task<TResponse> Handle(
    TNotification notification,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken cancellationToken)
{
    if (notification is SnmpOidReceived msg)
    {
        try
        {
            // fan-out logic
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TenantVectorFanOut exception for {DeviceName}", msg.DeviceName);
        }
    }
    return await next();  // always called, outside try/catch
}
```

### Pattern 4: PipelineMetricService Counter Registration

**What:** New counters are added to `PipelineMetricService` constructor and exposed via new increment methods. Follow existing naming: dotted meter names, `device_name` tag only.
**Example (existing pattern):**
```csharp
// Source: src/SnmpCollector/Telemetry/PipelineMetricService.cs
private readonly Counter<long> _pollUnreachable;
_pollUnreachable = _meter.CreateCounter<long>("snmp.poll.unreachable");
public void IncrementPollUnreachable(string deviceName)
    => _pollUnreachable.Add(1, new TagList { { "device_name", deviceName } });
```
New counter: `"snmp.tenantvector.routed"` — increments once per successful holder write (fan-out to 3 tenants = 3 increments).

### Pattern 5: MetricSlotHolder.WriteValue Signature Change

**What:** The existing `WriteValue(double value, string? stringValue)` signature must be extended to `WriteValue(double value, string? stringValue, SnmpType typeCode)` so `MetricSlot` can record which type code was active at write time.
**Impact:** Every call to `WriteValue` must be updated:
- `TenantVectorRegistry.Reload()` at line 113 — carries over old slot values; the carried-over `TypeCode` must be read from `existingSlot.TypeCode`
- New `TenantVectorFanOutBehavior` — writes from `msg.TypeCode`
**MetricSlot record** must gain a `SnmpType TypeCode` parameter:
```csharp
// Current: src/SnmpCollector/Pipeline/MetricSlot.cs
public sealed record MetricSlot(double Value, string? StringValue, DateTimeOffset UpdatedAt);
// After: add TypeCode
public sealed record MetricSlot(double Value, string? StringValue, SnmpType TypeCode, DateTimeOffset UpdatedAt);
```
Parameter order: put `TypeCode` before `UpdatedAt` so the auto-generated constructor is logical.

### Anti-Patterns to Avoid

- **Calling TryRoute before DeviceRegistry lookup:** The routing key uses IP + port + metric_name. The IP comes from `msg.AgentIp`. The port comes from `DeviceRegistry.TryGetDeviceByName(msg.DeviceName)` → `device.Port`. Never assume port 161; always resolve via registry.
- **Short-circuiting next() on fan-out miss:** A routing miss means the sample doesn't belong to any tenant — this is normal. Must always call `next()`, even when `TryRoute` returns false.
- **Logging on routing miss:** Decision is explicit: silent skip. No log, no counter on misses.
- **Re-doing the TypeCode switch in OtelMetricHandler:** After this phase, OtelMetricHandler reads `msg.ExtractedValue` and `msg.ExtractedStringValue` directly — no switch. The switch lives only in `ValueExtractionBehavior`.
- **Checking `msg.IsHeartbeat` anywhere after this phase:** The property is removed from `SnmpOidReceived`. The heartbeat now flows as a normal sample with `MetricName = "heartbeat"`.
- **Seeding the heartbeat OID via config JSON:** The heartbeat OID seed (`"1.3.6.1.4.1.9999.1.1.1.0" → "heartbeat"`) belongs in `OidMapService`'s constructor, injected before the configurable entries. It must not appear in `devices.json` or any configurable OID map file.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe slot write | Custom lock | `Volatile.Write` on plain field (existing pattern) | Already proven in Phase 26; CS0420 trap avoided |
| Routing lookup | Manual loop over groups | `ITenantVectorRegistry.TryRoute` | Already implemented with FrozenDictionary O(1) lookup |
| Port resolution | Hardcode 161 or parse from AgentIp | `IDeviceRegistry.TryGetDeviceByName(msg.DeviceName).Port` | Port is per-device config; some devices use non-161 ports |
| Counter instrument | Direct `Meter.CreateCounter` in behavior | Add method to `PipelineMetricService` | Consistent with PMET-01 through PMET-10 pattern |
| Value cast/switch | Inline in fan-out behavior | Read `msg.ExtractedValue` set by `ValueExtractionBehavior` | The extraction happens once; consumers read the result |

**Key insight:** All the hard infrastructure (registry, atomic slots, routing index, counters) was built in prior phases. Phase 27 is wiring, not building.

## Common Pitfalls

### Pitfall 1: Behavior Order Wrong in DI Registration

**What goes wrong:** `ValueExtractionBehavior` runs before `OidResolutionBehavior`, so `msg.MetricName` is null when extraction happens. Or `TenantVectorFanOutBehavior` runs before `ValueExtractionBehavior`, reading uninitialised `ExtractedValue = 0` for all types.
**Why it happens:** `AddOpenBehavior` registration order determines execution order. It's easy to add them at the top or wrong position.
**How to avoid:** Add both new behaviors AFTER `OidResolutionBehavior` in `ServiceCollectionExtensions.AddSnmpPipeline`. The comment in that method already lists the existing order — extend it.
**Warning signs:** Tests show `ExtractedValue = 0` for numeric types, or fan-out writes 0 when real values are non-zero.

### Pitfall 2: MetricSlot Record Constructor Parameter Order

**What goes wrong:** `MetricSlot` is a positional record. Adding `SnmpType TypeCode` changes the constructor signature. Any code using positional construction (not named parameters) breaks.
**Why it happens:** `TenantVectorRegistry.Reload` calls `newHolder.WriteValue(slot.Value, slot.StringValue)` — this must be updated to pass `slot.TypeCode`. If the old `MetricSlot` didn't have TypeCode, `slot.TypeCode` doesn't exist yet.
**How to avoid:** Update `MetricSlot` record first, then update `MetricSlotHolder.WriteValue`, then update all call sites (`TenantVectorRegistry.Reload` carry-over at line 113, and the new fan-out behavior write).
**Warning signs:** Build error CS7036 (required positional argument not provided) or CS1503 (wrong argument type).

### Pitfall 3: IsHeartbeat Removal Breaks Existing Tests

**What goes wrong:** `OidResolutionBehaviorTests.SkipsResolution_WhenIsHeartbeat` constructs `SnmpOidReceived` with `IsHeartbeat = true`. After removing the property, this test fails to compile.
**Why it happens:** The property no longer exists on the class.
**How to avoid:** Delete or rewrite the `SkipsResolution_WhenIsHeartbeat` test. After normalization, the heartbeat simply resolves to `"heartbeat"` via `OidMapService` — a new test can verify that behavior instead.
**Warning signs:** Build error CS0117 ("SnmpOidReceived does not contain a definition for 'IsHeartbeat'").

### Pitfall 4: Heartbeat OID Seed Overwritten by UpdateMap

**What goes wrong:** `OidMapService.UpdateMap` replaces the entire `_map` with whatever the configurable JSON provides. If the heartbeat OID is only seeded in the constructor but the JSON omits it, the first hot-reload wipes it out.
**Why it happens:** `UpdateMap` calls `BuildFrozenMap(entries)` on the new entries only — it does not merge with the existing map.
**How to avoid:** In `OidMapService.UpdateMap`, always inject the hardcoded heartbeat seed entry into the new entries dictionary before calling `BuildFrozenMap`. This can be a private constant or a method that ensures the heartbeat entry survives any reload.
**Warning signs:** After a ConfigMap reload, heartbeat OID resolves to `"Unknown"` instead of `"heartbeat"`.

### Pitfall 5: Fan-Out Exception Swallows OTel Export

**What goes wrong:** Fan-out behavior throws, exception propagates to `ExceptionBehavior`, pipeline terminates, `OtelMetricHandler` never fires, `snmp.event.handled` does not increment — looks like a dead pipeline.
**Why it happens:** `next()` is inside the try block instead of outside it.
**How to avoid:** The `next()` call must be OUTSIDE the try/catch in `TenantVectorFanOutBehavior`. Fan-out logic is inside the try; `next()` is always called after the try/catch block regardless.
**Warning signs:** `snmp.event.handled` counter stops incrementing when fan-out starts failing; `snmp.event.errors` spikes.

### Pitfall 6: DeviceName vs AgentIp for Routing Key

**What goes wrong:** Using `msg.AgentIp.ToString()` directly as the IP in `TryRoute` without resolving the port from `DeviceRegistry`. The routing index stores the IP from `TenantVectorOptions` (config-specified), which may differ from the raw agent IP format (IPv4-mapped IPv6 etc.).
**Why it happens:** `SnmpOidReceived.AgentIp` is an `IPAddress` object. The routing index IP key is the string from `TenantVectorOptions`. Case-insensitive matching (`RoutingKeyComparer`) handles most format differences, but the port must still come from `DeviceRegistry`.
**How to avoid:** Always call `IDeviceRegistry.TryGetDeviceByName(msg.DeviceName)` to get `device.Port`. Use `msg.AgentIp.ToString()` for the IP in `TryRoute`. If `TryGetDeviceByName` returns false, silent skip (no route).
**Warning signs:** `snmp.tenantvector.routed_total` stays zero even when registry has matching routes.

## Code Examples

### ValueExtractionBehavior — TypeCode Switch Pattern

```csharp
// Source: adapted from OtelMetricHandler.cs switch pattern (codebase)
// Runs after OidResolutionBehavior; sets ExtractedValue + ExtractedStringValue once for all consumers
public async Task<TResponse> Handle(
    TNotification notification,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken cancellationToken)
{
    if (notification is SnmpOidReceived msg)
    {
        switch (msg.TypeCode)
        {
            case SnmpType.Integer32:
                msg.ExtractedValue = ((Integer32)msg.Value).ToInt32();
                break;
            case SnmpType.Gauge32:
                msg.ExtractedValue = ((Gauge32)msg.Value).ToUInt32();
                break;
            case SnmpType.TimeTicks:
                msg.ExtractedValue = ((TimeTicks)msg.Value).ToUInt32();
                break;
            case SnmpType.Counter32:
                msg.ExtractedValue = ((Counter32)msg.Value).ToUInt32();
                break;
            case SnmpType.Counter64:
                msg.ExtractedValue = (double)((Counter64)msg.Value).ToUInt64();
                break;
            case SnmpType.OctetString:
            case SnmpType.IPAddress:
            case SnmpType.ObjectIdentifier:
                msg.ExtractedValue = 0;
                msg.ExtractedStringValue = msg.Value.ToString();
                break;
            // default: leave ExtractedValue = 0, ExtractedStringValue = null
        }
    }
    return await next();
}
```

### TenantVectorFanOutBehavior — Core Fan-Out Loop

```csharp
// Source: adapted from TenantVectorRegistry.TryRoute contract (Phase 26 codebase)
// and IDeviceRegistry.TryGetDeviceByName pattern (DeviceRegistry.cs)
if (notification is SnmpOidReceived msg)
{
    // Filter: skip Unknown metric names (unresolved OIDs)
    var metricName = msg.MetricName;
    if (metricName is null || metricName == OidMapService.Unknown)
        return await next();

    try
    {
        // Resolve port from DeviceRegistry (no changes to SnmpOidReceived contract)
        if (!_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device))
            return await next(); // silent skip — device not in registry

        var ip = msg.AgentIp.ToString();

        if (_registry.TryRoute(ip, device.Port, metricName, out var holders))
        {
            foreach (var holder in holders)
            {
                holder.WriteValue(msg.ExtractedValue, msg.ExtractedStringValue, msg.TypeCode);
                _pipelineMetrics.IncrementTenantVectorRouted(msg.DeviceName!);
            }
        }
        // silent skip if no route — no log, no counter
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "TenantVectorFanOut exception for {DeviceName}", msg.DeviceName);
    }
}
return await next(); // always called
```

### OtelMetricHandler — After Refactor (reads pre-extracted values)

```csharp
// Source: src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs (existing, after refactor)
// Replace the TypeCode switch with reads from pre-extracted properties
switch (notification.TypeCode)
{
    case SnmpType.Integer32:
    case SnmpType.Gauge32:
    case SnmpType.TimeTicks:
    case SnmpType.Counter32:
    case SnmpType.Counter64:
        _metricFactory.RecordGauge(
            metricName, notification.Oid, deviceName, ip, source,
            notification.TypeCode.ToString().ToLowerInvariant(),
            notification.ExtractedValue);        // <-- pre-extracted
        _pipelineMetrics.IncrementHandled(deviceName);
        break;

    case SnmpType.OctetString:
    case SnmpType.IPAddress:
    case SnmpType.ObjectIdentifier:
        _metricFactory.RecordInfo(
            metricName, notification.Oid, deviceName, ip, source,
            notification.TypeCode.ToString().ToLowerInvariant(),
            (notification.ExtractedStringValue ?? string.Empty)[..Math.Min(128, notification.ExtractedStringValue?.Length ?? 0)]);
        _pipelineMetrics.IncrementHandled(deviceName);
        break;

    default:
        _logger.LogWarning("Unrecognized SnmpType dropped: ...");
        break;
}
```
Note: string truncation to 128 chars stays in OtelMetricHandler only, not in ValueExtractionBehavior.

### OidMapService — Heartbeat OID Seed

```csharp
// Source: src/SnmpCollector/Pipeline/OidMapService.cs (existing, modified)
// Constructor: merge heartbeat seed before BuildFrozenMap
public OidMapService(Dictionary<string, string> initialEntries, ILogger<OidMapService> logger)
{
    _logger = logger;
    var seeded = MergeWithHeartbeatSeed(initialEntries);
    _map = BuildFrozenMap(seeded);
    _metricNames = _map.Values.ToFrozenSet();
    _logger.LogInformation("OidMapService initialized with {EntryCount} entries", _map.Count);
}

// Also in UpdateMap: merge seed before rebuild
public void UpdateMap(Dictionary<string, string> entries)
{
    var seeded = MergeWithHeartbeatSeed(entries);
    // ... rest of existing diff logic using seeded instead of entries
    var newMap = BuildFrozenMap(seeded);
    // ...
}

private static Dictionary<string, string> MergeWithHeartbeatSeed(Dictionary<string, string> entries)
{
    var merged = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
    merged[HeartbeatJobOptions.HeartbeatOid] = "heartbeat";
    return merged;
}
```

### PipelineMetricService — New Counter

```csharp
// Source: src/SnmpCollector/Telemetry/PipelineMetricService.cs (existing, add to end)
// Follows exact same pattern as _pollUnreachable
private readonly Counter<long> _tenantVectorRouted;

// In constructor:
_tenantVectorRouted = _meter.CreateCounter<long>("snmp.tenantvector.routed");

// New public method:
public void IncrementTenantVectorRouted(string deviceName)
    => _tenantVectorRouted.Add(1, new TagList { { "device_name", deviceName } });
```

### SnmpOidReceived — New Properties

```csharp
// Source: src/SnmpCollector/Pipeline/SnmpOidReceived.cs (existing, add two properties, remove IsHeartbeat)
/// <summary>
/// Pre-extracted numeric value. Set by ValueExtractionBehavior for numeric TypeCodes
/// (Integer32, Gauge32, TimeTicks, Counter32, Counter64). Zero for string types.
/// </summary>
public double ExtractedValue { get; set; }

/// <summary>
/// Pre-extracted string value. Set by ValueExtractionBehavior for string TypeCodes
/// (OctetString, IPAddress, ObjectIdentifier). Null for numeric types.
/// Consumers apply their own truncation as needed (OtelMetricHandler truncates to 128 chars).
/// </summary>
public string? ExtractedStringValue { get; set; }
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `IsHeartbeat` flag on message, checked in each behavior | Heartbeat normalised through pipeline; OidMapService seeds `"heartbeat"` metric name | Phase 27 | Removes special-case branches; heartbeat behaves as any metric |
| TypeCode switch duplicated in OtelMetricHandler | Single switch in `ValueExtractionBehavior`; consumers read pre-extracted values | Phase 27 | No duplication; fan-out and handler both benefit |
| `MetricSlot(double, string?, DateTimeOffset)` 3-field record | `MetricSlot(double, string?, SnmpType, DateTimeOffset)` 4-field record | Phase 27 | TypeCode preserved on slot for consumer context |
| Pipeline chain: Logging→Exception→Validation→OidResolution→Handler | Chain: Logging→Exception→Validation→OidResolution→ValueExtraction→FanOut→Handler | Phase 27 | Fan-out integrated without disrupting OTel export |

**Deprecated/removed by this phase:**
- `SnmpOidReceived.IsHeartbeat`: property removed; any code referencing it breaks at compile time
- IsHeartbeat check in `OidResolutionBehavior`: removed (the guard that skipped OID resolution for heartbeat)
- IsHeartbeat early-return in `OtelMetricHandler`: removed (heartbeat is now a real metric, counted normally)
- IsHeartbeat assignment in `ChannelConsumerService`: removed (no property to assign)

## Open Questions

1. **OtelMetricHandler TypeCode label strings after refactor**
   - What we know: Currently the switch uses inline `"integer32"`, `"gauge32"`, etc. string literals as the `snmpType` parameter to `RecordGauge`/`RecordInfo`
   - What's unclear: After collapsing numeric cases into one block, should the snmpType label use `notification.TypeCode.ToString().ToLowerInvariant()` or keep per-case literals? The current strings match the old per-case literals exactly (e.g., `"counter64"` not `"Counter64"`). `ToLowerInvariant()` produces the same result for these enum values.
   - Recommendation: Use `notification.TypeCode.ToString().ToLowerInvariant()` — it's equivalent to the literals and avoids a new switch. Verify with one test that the label value is lowercase.

2. **Test updates required for IsHeartbeat removal**
   - What we know: `OidResolutionBehaviorTests.SkipsResolution_WhenIsHeartbeat` constructs `SnmpOidReceived { IsHeartbeat = true }` — this will not compile after the property is removed.
   - What's unclear: Whether any other tests set `IsHeartbeat`.
   - Recommendation: Delete `SkipsResolution_WhenIsHeartbeat` and replace with a new test `ResolvesHeartbeatOid_ViaOidMapService` that verifies `msg.MetricName == "heartbeat"` after the behavior runs. Run a grep for `IsHeartbeat` before finalising removal.

3. **MetricSlot carry-over TypeCode in TenantVectorRegistry.Reload**
   - What we know: Line 113 of `TenantVectorRegistry.cs` currently: `newHolder.WriteValue(existingSlot.Value, existingSlot.StringValue)`. After `WriteValue` gains a `typeCode` parameter, this call must pass `existingSlot.TypeCode`.
   - What's unclear: Nothing — this is a mechanical change. Just flagged to ensure the planner includes it in the carry-over task.
   - Recommendation: Update line 113 to `newHolder.WriteValue(existingSlot.Value, existingSlot.StringValue, existingSlot.TypeCode)`.

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection — all findings verified against actual source files in `src/SnmpCollector/`
  - `Pipeline/SnmpOidReceived.cs` — current properties, IsHeartbeat presence
  - `Pipeline/Behaviors/OidResolutionBehavior.cs` — IsHeartbeat guard to remove
  - `Pipeline/Behaviors/ExceptionBehavior.cs` — exception isolation pattern to replicate
  - `Pipeline/Handlers/OtelMetricHandler.cs` — TypeCode switch to refactor
  - `Pipeline/TenantVectorRegistry.cs` — TryRoute API, WriteValue carry-over at line 113
  - `Pipeline/MetricSlotHolder.cs` — WriteValue current signature
  - `Pipeline/MetricSlot.cs` — current record fields
  - `Pipeline/DeviceRegistry.cs` — TryGetDeviceByName API
  - `Pipeline/OidMapService.cs` — UpdateMap replaces entire map (pitfall 4 root cause)
  - `Telemetry/PipelineMetricService.cs` — counter registration pattern
  - `Extensions/ServiceCollectionExtensions.cs` lines 350-353 — behavior registration order
  - `Services/ChannelConsumerService.cs` — IsHeartbeat assignment to remove
  - `Configuration/HeartbeatJobOptions.cs` — HeartbeatOid and HeartbeatDeviceName constants
  - `.planning/phases/26-core-data-types-and-registry/26-VERIFICATION.md` — Phase 26 completion confirmed
- Phase 26 verification report — confirms all Phase 26 artifacts are wired and tested

### Secondary (MEDIUM confidence)
- None required — all questions answered by direct codebase inspection

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries; all packages already in use
- Architecture: HIGH — all patterns derived directly from existing codebase source files
- Pitfalls: HIGH — identified from actual code paths (UpdateMap overwrites map, behavior ordering in DI, IsHeartbeat in tests)

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (stable domain; no external dependencies)
