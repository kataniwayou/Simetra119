# Architecture: Priority Vector Data Layer

**Project:** SnmpCollector - Priority Vector Integration
**Researched:** 2026-03-10
**Confidence:** HIGH (based on direct codebase analysis + design spec)

## Current Architecture Summary

The existing pipeline processes SNMP samples (polls and traps) through a MediatR behavior chain:

```
[MetricPollJob / ChannelConsumerService]
    |  creates SnmpOidReceived { Oid, AgentIp, DeviceName, Value, Source, TypeCode }
    v
LoggingBehavior          <- outermost, logs entry, increments published counter
    v
ExceptionBehavior        <- catches unhandled exceptions
    v
ValidationBehavior       <- rejects malformed OIDs, missing DeviceName
    v
OidResolutionBehavior    <- sets msg.MetricName from OidMapService
    v
OtelMetricHandler        <- terminal handler, records to OTel instruments
```

Key existing singletons:
- **DeviceRegistry** (`IDeviceRegistry`): FrozenDictionary keyed by `configAddress:port`, volatile swap on reload
- **OidMapService** (`IOidMapService`): FrozenDictionary mapping `oid -> metric_name`, volatile swap on reload
- **OidMapWatcherService**: watches `simetra-oidmaps` ConfigMap, calls `IOidMapService.UpdateMap()`
- **DeviceWatcherService**: watches `simetra-devices` ConfigMap, calls `IDeviceRegistry.ReloadAsync()` + `DynamicPollScheduler.ReconcileAsync()`

## Integration Architecture

### New Pipeline Position

TenantVectorFanOutBehavior inserts **after OidResolutionBehavior** as the 5th behavior (innermost before the handler). This is correct because it needs `MetricName` to be resolved (set by OidResolution) before it can look up routing slots.

```
LoggingBehavior              <- 1st (outermost)
ExceptionBehavior            <- 2nd
ValidationBehavior           <- 3rd
OidResolutionBehavior        <- 4th (sets MetricName)
TenantVectorFanOutBehavior   <- 5th (NEW - routes to tenant slots)
OtelMetricHandler            <- terminal handler (unchanged)
```

The fan-out behavior **always calls next()** -- it is a side-effect behavior that writes to tenant slots, it does not replace or interfere with OTel metric export. The existing OtelMetricHandler continues to function exactly as before.

### Data Flow

```
Sample arrives (poll response or trap)
    |
    v
SnmpOidReceived enters MediatR pipeline
    |  (AgentIp, Oid, MetricName set by OidResolution)
    v
TenantVectorFanOutBehavior:
    1. Skip if msg.IsHeartbeat (no tenant routing for heartbeats)
    2. Look up DeviceInfo from IDeviceRegistry using DeviceName to get Port
    3. Build routing key: (ResolvedIp, Port, MetricName)
    4. Query routing index for matching tenant slots
    5. For each matching slot: write (value, timestamp) in-place
    6. Call next() -- sample continues to OtelMetricHandler unchanged
    |
    v
OtelMetricHandler (unchanged -- exports to OTel as before)
```

### Port Resolution: How the Fan-Out Gets Port

**The problem:** `SnmpOidReceived` carries `AgentIp` and `DeviceName` but NOT `Port`. The fan-out behavior needs Port to construct the routing key `(ip, port, metric_name)`.

**The solution:** Use the existing `IDeviceRegistry.TryGetDeviceByName(msg.DeviceName)` to get the `DeviceInfo` record, which contains `.Port` and `.ResolvedIp`. This is:

- **O(1)** -- DeviceRegistry maintains a FrozenDictionary by-name index
- **Already tested** -- the TryGetDeviceByName path is an existing interface method
- **Always available** -- ValidationBehavior (step 3) already rejects messages with null DeviceName, so by step 5 DeviceName is guaranteed non-null
- **Zero changes to existing code** -- no modifications to SnmpOidReceived, MetricPollJob, or ChannelConsumerService

The alternative of adding Port to SnmpOidReceived is a reasonable future cleanup but would require modifying both MetricPollJob.DispatchResponseAsync and ChannelConsumerService.ExecuteAsync. Not worth the churn for this phase.

### Routing Key Design

Per the design spec (`Docs/tenantvector.txt`), the routing index maps:

```
index[ (ip, port, oid) ] -> list of (tenant_id, metric_slot)
```

However, the behavior runs AFTER OidResolution, so it has `MetricName` rather than raw OID. The config file should use MetricName.

**Recommendation: Use MetricName as the routing key dimension, not raw OID.** Reasons:
1. MetricName is the human-readable identifier already used throughout the system
2. Multiple OIDs can map to the same MetricName (the OidMapService is many-to-one)
3. Config authors should not need to know raw OIDs -- they already use metric names in Grafana dashboards
4. The routing key becomes `(ip, port, metric_name)` which matches the milestone context

The routing index structure:

```csharp
FrozenDictionary<RoutingKey, FrozenList<TenantSlotRef>> _routingIndex;

// where:
public readonly record struct RoutingKey(string Ip, int Port, string MetricName);
public readonly record struct TenantSlotRef(int TenantIndex, int SlotIndex);
```

`TenantSlotRef` is an index pair enabling direct write into the tenant vector without re-lookup. The FrozenList avoids per-lookup allocation.

### IP Normalization in Routing Keys

**Critical detail:** The config file will contain raw IPs (e.g., `"10.0.1.50"`). The `SnmpOidReceived.AgentIp` is an `IPAddress` set by MetricPollJob from `DeviceInfo.ResolvedIp` (which is `MapToIPv4()` normalized). The routing key must use the same normalization.

When building the routing index from config, normalize all IPs through `IPAddress.Parse(x).MapToIPv4().ToString()`. When looking up at fan-out time, use `msg.AgentIp.MapToIPv4().ToString()` (or get it from the DeviceInfo lookup which already has the normalized ResolvedIp).

Since the fan-out already calls `TryGetDeviceByName(msg.DeviceName)` to get Port, it can use `device.ResolvedIp` directly for the routing key -- this is already normalized and avoids redundant parsing.

---

## New Components

### 1. Configuration Models (`Configuration/` namespace)

| Class | File | Purpose |
|-------|------|---------|
| `TenantVectorOptions` | `Configuration/TenantVectorOptions.cs` | Root options: `List<TenantOptions> Tenants` |
| `TenantOptions` | `Configuration/TenantOptions.cs` | Per-tenant: `Id`, `Priority`, `GraceMultiplier`, `List<TenantMetricOptions> Metrics` |
| `TenantMetricOptions` | `Configuration/TenantMetricOptions.cs` | Per-slot config: `Ip`, `Port`, `MetricName`, `Source`, `IntervalSeconds` |
| `TenantVectorOptionsValidator` | `Configuration/Validators/TenantVectorOptionsValidator.cs` | IValidateOptions: unique IDs, valid priorities, non-empty metrics |

**Config file structure (`tenantvector.json`):**

```json
{
  "Tenants": [
    {
      "Id": "link-health-L1",
      "Priority": 1,
      "GraceMultiplier": 3.0,
      "Metrics": [
        {
          "Ip": "10.0.1.50",
          "Port": 161,
          "MetricName": "obp_link_state_L1",
          "Source": "poll",
          "IntervalSeconds": 10
        }
      ]
    },
    {
      "Id": "npb-port-stats",
      "Priority": 2,
      "GraceMultiplier": 2.0,
      "Metrics": [
        {
          "Ip": "10.0.1.60",
          "Port": 161,
          "MetricName": "npb_port_rx_bytes",
          "Source": "poll",
          "IntervalSeconds": 10
        }
      ]
    }
  ]
}
```

### 2. Core Data Types (`Pipeline/` namespace)

| Class | File | Purpose |
|-------|------|---------|
| `RoutingKey` | `Pipeline/RoutingKey.cs` | `readonly record struct(string Ip, int Port, string MetricName)` |
| `TenantSlot` | `Pipeline/TenantSlot.cs` | Mutable cell: `MetricName`, `Value` (double), `StringValue` (string?), `UpdatedAt` (DateTimeOffset) |
| `Tenant` | `Pipeline/Tenant.cs` | Runtime model: `Id`, `Priority`, `GraceMultiplier`, `IReadOnlyList<TenantSlot> Slots` |
| `TenantVector` | `Pipeline/TenantVector.cs` | Ordered list of tenants, grouped by priority. Provides priority-group iteration. |

### 3. Registry (`Pipeline/` namespace)

| Class | File | Purpose |
|-------|------|---------|
| `ITenantVectorRegistry` | `Pipeline/ITenantVectorRegistry.cs` | Interface: `TryRoute(RoutingKey)`, `Reload(...)`, `TenantVector` property |
| `TenantVectorRegistry` | `Pipeline/TenantVectorRegistry.cs` | Singleton: FrozenDictionary routing index, volatile swap, structured diff logging |

### 4. Pipeline Behavior (`Pipeline/Behaviors/` namespace)

| Class | File | Purpose |
|-------|------|---------|
| `TenantVectorFanOutBehavior<T,R>` | `Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` | IPipelineBehavior: routes samples to tenant slots via routing index |

### 5. ConfigMap Watcher (`Services/` namespace)

| Class | File | Purpose |
|-------|------|---------|
| `TenantVectorWatcherService` | `Services/TenantVectorWatcherService.cs` | BackgroundService: watches `simetra-tenantvector` ConfigMap |

### 6. Telemetry (`Telemetry/` namespace)

| Class | File | Purpose |
|-------|------|---------|
| (Optional) `TenantVectorMetrics` | `Telemetry/TenantVectorMetrics.cs` | Pipeline meter: fan-out hit/miss counts, reload count |

---

## Modified Components

### Files That Change

| File | Change | Scope |
|------|--------|-------|
| `Extensions/ServiceCollectionExtensions.cs` (`AddSnmpConfiguration`) | Bind `TenantVectorOptions`, register `ITenantVectorRegistry` singleton, register `TenantVectorWatcherService` in K8s block | ~20 lines added |
| `Extensions/ServiceCollectionExtensions.cs` (`AddSnmpPipeline`) | Add `cfg.AddOpenBehavior(typeof(TenantVectorFanOutBehavior<,>))` after OidResolution | 1 line added |
| `Program.cs` | Add local-dev fallback for `tenantvector.json` loading (same pattern as oidmaps.json block) | ~15 lines added |

### Files That Do NOT Change

| File | Why Unchanged |
|------|---------------|
| `Pipeline/SnmpOidReceived.cs` | No new properties needed -- DeviceName lookup provides Port |
| `Jobs/MetricPollJob.cs` | Already sets DeviceName and AgentIp correctly |
| `Services/ChannelConsumerService.cs` | Already sets DeviceName and AgentIp correctly |
| `Pipeline/Handlers/OtelMetricHandler.cs` | Fan-out is a side-effect; OTel export unaffected |
| `Pipeline/Behaviors/ValidationBehavior.cs` | No new validation at pipeline level |
| `Pipeline/Behaviors/OidResolutionBehavior.cs` | MetricName resolution unchanged |
| `Pipeline/Behaviors/LoggingBehavior.cs` | No changes needed |
| `Pipeline/Behaviors/ExceptionBehavior.cs` | No changes needed |
| `Pipeline/DeviceRegistry.cs` | Existing TryGetDeviceByName is sufficient |
| `Pipeline/OidMapService.cs` | No changes needed |
| `Services/OidMapWatcherService.cs` | No changes needed |
| `Services/DeviceWatcherService.cs` | No changes needed |

---

## Component Boundaries and Patterns

### Registry Pattern (matching existing convention)

The `TenantVectorRegistry` follows the exact same pattern as `DeviceRegistry` and `OidMapService`:

1. **Singleton** registered in DI container
2. **FrozenDictionary** for the routing index (immutable, lock-free reads on the hot path)
3. **Volatile swap** on reload (atomic replacement, no reader locks)
4. **Structured diff logging** on reload (added/removed tenants, changed slots)
5. **SemaphoreSlim** in the watcher to serialize concurrent reload events

```
TenantVectorWatcherService
    |  watches simetra-tenantvector ConfigMap
    |  parses tenantvector.json key
    v
ITenantVectorRegistry.Reload(List<TenantOptions>)
    |  builds Tenant objects from config
    |  builds routing index: RoutingKey -> List<TenantSlotRef>
    |  volatile swap of both TenantVector and routing index
    |  logs diff (added/removed tenants, slot count changes)
```

### Watcher Pattern (structural clone of OidMapWatcherService)

`TenantVectorWatcherService` follows the exact structure of `OidMapWatcherService` (lines 24-228 of that file):

- ConfigMap name: `simetra-tenantvector`
- Config key: `tenantvector.json`
- Initial load via `ReadNamespacedConfigMapAsync` before watch starts
- K8s API watch with `ListNamespacedConfigMapWithHttpMessagesAsync` + `WatchAsync`
- Handles `Added`/`Modified` events; warns on `Deleted` (retains current state)
- `SemaphoreSlim` for reload serialization
- JSON deserialization with try/catch (skip on parse failure, log error)
- Auto-reconnect on watch timeout (~30 min) or unexpected disconnect (5s backoff)
- Namespace read from service account mount (`/var/run/secrets/kubernetes.io/serviceaccount/namespace`)

### DI Registration Order

Additions go into existing methods in `ServiceCollectionExtensions.cs`:

```csharp
// In AddSnmpConfiguration(), after OidMapService registration (line ~301):
services.AddSingleton<ITenantVectorRegistry, TenantVectorRegistry>();

// In AddSnmpConfiguration(), inside the IsInCluster() block (line ~243):
services.AddSingleton<TenantVectorWatcherService>();
services.AddHostedService(sp => sp.GetRequiredService<TenantVectorWatcherService>());

// In AddSnmpPipeline(), after OidResolutionBehavior (line ~341):
cfg.AddOpenBehavior(typeof(TenantVectorFanOutBehavior<,>)); // 5th = after OidResolution
```

### Local Dev Fallback in Program.cs

Following the existing pattern (Program.cs lines 59-97):

```csharp
// After the oidmaps.json and devices.json local-dev loading blocks:
var tenantvectorPath = Path.Combine(configDir, "tenantvector.json");
if (File.Exists(tenantvectorPath))
{
    var tvJson = File.ReadAllText(tenantvectorPath);
    var tvOptions = JsonSerializer.Deserialize<TenantVectorOptions>(tvJson, jsonOptions);
    if (tvOptions?.Tenants != null)
    {
        var tvRegistry = app.Services.GetRequiredService<ITenantVectorRegistry>();
        tvRegistry.Reload(tvOptions.Tenants);
    }
}
```

---

## TenantSlot Write Semantics

Per the design spec: "When a new sample arrives, the value and updated_at are overwritten in place. The previous value is discarded."

```csharp
public sealed class TenantSlot
{
    public string MetricName { get; }
    public string Ip { get; }
    public int Port { get; }

    // Mutable state -- written by fan-out behavior, read by future evaluator
    private double _value;
    private string? _stringValue;
    private long _updatedAtTicks;  // DateTimeOffset.UtcTicks for atomic read/write

    public void Write(double value, DateTimeOffset timestamp)
    {
        Volatile.Write(ref _value, value);
        Volatile.Write(ref _updatedAtTicks, timestamp.UtcTicks);
    }

    public void WriteString(string value, DateTimeOffset timestamp)
    {
        Volatile.Write(ref _stringValue, value);
        Volatile.Write(ref _updatedAtTicks, timestamp.UtcTicks);
    }

    public (double Value, DateTimeOffset UpdatedAt) Read()
    {
        var ticks = Volatile.Read(ref _updatedAtTicks);
        var val = Volatile.Read(ref _value);
        return (val, new DateTimeOffset(ticks, TimeSpan.Zero));
    }
}
```

**No locks.** Last-writer-wins is the specified behavior. `Volatile.Write`/`Volatile.Read` ensures visibility across threads without locking overhead.

---

## Suggested Build Order

Dependencies flow downward -- each step depends on the one above it.

### Step 1: Config Models + Validation
**Files:** `TenantVectorOptions.cs`, `TenantOptions.cs`, `TenantMetricOptions.cs`, `TenantVectorOptionsValidator.cs`
**Tests:** Unit tests for validation (required fields, unique tenant IDs, valid priorities, non-empty metrics)
**Dependencies:** None -- pure POCOs and validators
**Integration:** None yet

### Step 2: Core Data Types
**Files:** `RoutingKey.cs`, `TenantSlot.cs`, `Tenant.cs`, `TenantVector.cs`
**Tests:** Unit tests for RoutingKey equality/hashing, TenantSlot write/read with Volatile semantics, TenantVector priority grouping
**Dependencies:** Step 1 (config models used to construct tenants from options)
**Integration:** None yet

### Step 3: TenantVectorRegistry
**Files:** `ITenantVectorRegistry.cs`, `TenantVectorRegistry.cs`
**Tests:** Unit tests for routing index construction from config, fan-out lookup (hit/miss/multi-tenant), reload atomicity (old reads during rebuild), IP normalization, structured diff logging
**Dependencies:** Steps 1 + 2
**Integration:** None yet (pure unit tests with mocked logger)

### Step 4: TenantVectorFanOutBehavior + DI Registration
**Files:** `TenantVectorFanOutBehavior.cs`, changes to `ServiceCollectionExtensions.cs`
**Tests:** Unit tests with mocked registry + device registry. Integration test verifying behavior fires in correct pipeline position (after OidResolution, before handler).
**Dependencies:** Step 3 (registry), existing `IDeviceRegistry`
**Integration:** First touch to existing files -- adds 1 line to `AddSnmpPipeline`, ~5 lines to `AddSnmpConfiguration`

### Step 5: TenantVectorWatcherService + Local Dev
**Files:** `TenantVectorWatcherService.cs`, changes to `ServiceCollectionExtensions.cs` (K8s block), changes to `Program.cs`
**Tests:** Unit test with mocked IKubernetes client. E2E validation: deploy ConfigMap, verify registry populated.
**Dependencies:** Step 3 (registry reload method)
**Integration:** Adds watcher registration to K8s block, adds local-dev loading to Program.cs

### Step 6: K8s ConfigMap Manifest
**Files:** `deploy/k8s/production/simetra-tenantvector-configmap.yaml`, `src/SnmpCollector/config/tenantvector.json` (local dev)
**Tests:** E2E: apply ConfigMap, verify watcher picks it up, verify fan-out routes samples
**Dependencies:** Step 5

---

## Anti-Patterns to Avoid

### 1. Do NOT add a second poll scheduler for tenant metrics
The design spec shows tenant metrics carry `(ip, port, oid, intervalSeconds)`. This might suggest tenant-specific poll jobs. **Do not.** The existing `MetricPollJob` + `DynamicPollScheduler` already polls devices. Tenant slots are populated by fanning out from the existing pipeline -- they are **consumers** of samples, not producers. If a tenant references a MetricName that no device polls, that is a configuration validation error, not something the system should try to fix.

### 2. Do NOT modify SnmpOidReceived in this phase
Adding Port or TenantId fields to the message creates coupling between the core pipeline and the tenant feature. The behavior can get Port from DeviceRegistry via DeviceName. Keep the message contract stable.

### 3. Do NOT use locks on TenantSlot writes
Slot writes are last-writer-wins by design. Use `Volatile.Write` for value and timestamp, not locks or ConcurrentDictionary. The routing index is rebuilt on reload (FrozenDictionary swap), so reads are lock-free.

### 4. Do NOT couple the fan-out behavior to metric export
The fan-out behavior writes to in-memory tenant slots. It does NOT export tenant data as OTel metrics. Export is a separate concern for a future phase. The data layer is purely in-memory state that a future evaluator will read.

### 5. Do NOT rebuild routing index on OID map reload
The routing index uses MetricName (not raw OID), so when the OID map changes (oidmaps.json update), the routing index does NOT need rebuilding. The OidResolutionBehavior already resolves to the new MetricName before the fan-out runs. Only `tenantvector.json` changes trigger a routing index rebuild.

### 6. Do NOT use string concatenation for routing keys
`RoutingKey` must be a `record struct` with proper `GetHashCode`/`Equals` for use as a FrozenDictionary key. Do not use `$"{ip}:{port}:{metricName}"` string keys -- that allocates on every lookup and is fragile to formatting differences.

---

## Scalability Notes

| Concern | Current Scale | With Tenant Vector |
|---------|---------------|-------------------|
| Pipeline throughput | ~100-500 samples/sec across 2 devices | Same rate, +1 FrozenDictionary lookup per sample |
| Memory | DeviceRegistry + OidMapService FrozenDicts | +TenantVector + routing index (small: dozens of tenants, hundreds of slots) |
| Reload frequency | ConfigMap changes (minutes/hours) | Same pattern, same frequency |
| Routing index size | N/A | O(unique metric addresses) entries, each pointing to O(tenants-per-metric) slots |

The fan-out adds one `FrozenDictionary.TryGetValue()` call plus one `IDeviceRegistry.TryGetDeviceByName()` call per pipeline invocation. For samples that match no tenant slots, this is two hash lookups returning quickly. For matching samples, slot writes are direct field assignments via `Volatile.Write`. Negligible overhead.

---

## Sources

- Direct codebase analysis of all referenced source files (HIGH confidence):
  - `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` -- DI registration patterns
  - `src/SnmpCollector/Pipeline/Behaviors/*.cs` -- all 4 existing behaviors
  - `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` -- terminal handler
  - `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` -- message contract
  - `src/SnmpCollector/Pipeline/DeviceRegistry.cs` + `IDeviceRegistry.cs` -- registry pattern
  - `src/SnmpCollector/Pipeline/OidMapService.cs` + `IOidMapService.cs` -- FrozenDictionary swap pattern
  - `src/SnmpCollector/Pipeline/DeviceInfo.cs` -- device record (Name, ConfigAddress, ResolvedIp, Port)
  - `src/SnmpCollector/Services/OidMapWatcherService.cs` -- ConfigMap watcher template
  - `src/SnmpCollector/Services/DeviceWatcherService.cs` -- ConfigMap watcher with downstream calls
  - `src/SnmpCollector/Services/ChannelConsumerService.cs` -- trap path message construction
  - `src/SnmpCollector/Jobs/MetricPollJob.cs` -- poll path message construction
  - `src/SnmpCollector/Program.cs` -- local-dev config loading pattern
  - `src/SnmpCollector/Telemetry/TelemetryConstants.cs` -- meter names
- `Docs/tenantvector.txt` -- authoritative design specification (HIGH confidence)
