# Architecture Patterns: v1.7 Configuration Consistency & Tenant Commands

**Domain:** SNMP monitoring agent -- self-describing tenant metrics, commands data model, CommunityString validation, tenantvector rename
**Researched:** 2026-03-14
**Confidence:** HIGH (all findings based on direct codebase analysis; no training-data speculation)

---

## Existing Architecture Snapshot (v1.6 baseline)

### Component Inventory

| Component | Kind | File | Role |
|-----------|------|------|------|
| `TenantVectorRegistry` | Singleton class | `Pipeline/TenantVectorRegistry.cs` | Builds priority groups + routing index from `TenantVectorOptions`; volatile swap on `Reload()` |
| `TenantVectorWatcherService` | BackgroundService | `Services/TenantVectorWatcherService.cs` | Watches `simetra-tenantvector` ConfigMap; calls `registry.Reload(options)` |
| `DeviceRegistry` | Singleton class | `Pipeline/DeviceRegistry.cs` | Holds `FrozenDictionary` by `ip:port` and by `Name`; exposes `AllDevices`, `TryGetByIpPort`, `TryGetDeviceByName` |
| `DynamicPollScheduler` | Singleton class | `Services/DynamicPollScheduler.cs` | Diffs Quartz jobs vs desired device list; add/remove/reschedule |
| `TenantVectorFanOutBehavior` | MediatR behavior | `Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` | Routes received samples to `MetricSlotHolder`s; uses `IDeviceRegistry` to resolve port |
| `TenantVectorOptions` | Config model | `Configuration/TenantVectorOptions.cs` | Top-level wrapper: `{ "Tenants": [...] }` |
| `TenantOptions` | Config model | `Configuration/TenantOptions.cs` | `Priority` + `List<MetricSlotOptions>` |
| `MetricSlotOptions` | Config model | `Configuration/MetricSlotOptions.cs` | `Ip`, `Port`, `MetricName`, `TimeSeriesSize` |
| `TenantVectorOptionsValidator` | Validator | `Configuration/Validators/TenantVectorOptionsValidator.cs` | Currently a no-op pass-through |
| `DeviceOptions` | Config model | `Configuration/DeviceOptions.cs` | `Name`, `IpAddress`, `Port`, `CommunityString?`, `List<PollOptions>` |
| `DevicesOptionsValidator` | Validator | `Configuration/Validators/DevicesOptionsValidator.cs` | Validates name, IP, port, poll intervals; does NOT validate `CommunityString` |
| `CommunityStringHelper` | Static helper | `Pipeline/CommunityStringHelper.cs` | `Simetra.{DeviceName}` convention; `TryExtractDeviceName`, `DeriveFromDeviceName` |
| `MetricSlotHolder` | Runtime model | `Pipeline/MetricSlotHolder.cs` | Volatile cyclic time-series; `WriteValue`, `ReadSlot`, `CopyFrom` |
| `Tenant` | Runtime model | `Pipeline/Tenant.cs` | `Id`, `Priority`, `IReadOnlyList<MetricSlotHolder>` |

### Current `TenantVectorRegistry.Reload()` Data Flow

```
TenantVectorOptions.Tenants[]
    for each TenantOptions:
        for each MetricSlotOptions metric:
            resolvedIp = ResolveIp(metric.Ip)          // USES _deviceRegistry.AllDevices
            intervalSeconds = DeriveIntervalSeconds(...)  // USES _deviceRegistry.TryGetByIpPort
                                                          // then _oidMapService.Resolve(oid) == metricName
            new MetricSlotHolder(resolvedIp, port, metricName, intervalSeconds, timeSeriesSize)
            carry-over old value if (ip, port, metricName) existed
        new Tenant(id, priority, holders)
    FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>> routingIndex
    volatile swap _groups and _routingIndex
```

Key observation: `TenantVectorRegistry` currently has **two dependencies on `DeviceRegistry`**:

1. `ResolveIp()` -- translates a DNS config address (e.g. `npb-simulator.simetra.svc.cluster.local`) to a resolved IPv4 string
2. `DeriveIntervalSeconds()` -- walks device poll groups to find which interval a given metric runs at, by reverse-resolving OID → metricName via `IOidMapService`

### Current `TenantVectorFanOutBehavior` Device Registry Dependency

```csharp
else if (_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device))
{
    var ip = msg.AgentIp.ToString();
    if (_registry.TryRoute(ip, device.Port, metricName, out var holders))
    { ... }
}
```

The behavior uses `DeviceRegistry` only to look up the **port** for a given device name, so it can form the `(ip, port, metricName)` routing key.

### ConfigMap Names (current)

| ConfigMap | Watched by | Key | Content |
|-----------|-----------|-----|---------|
| `simetra-tenantvector` | `TenantVectorWatcherService` | `tenantvector.json` | `{ "Tenants": [...] }` |
| `simetra-devices` | `DeviceWatcherService` | `devices.json` | `[{ "Name": ..., "Polls": [...] }]` |

---

## v1.7 Feature Integration Analysis

### Feature 1: Tenant Metrics as Self-Describing Objects

**What changes:** Each metric slot carries its own `Ip`, `Port`, `MetricName`, `IntervalSeconds`, and optionally `TimeSeriesSize`. The tenant config does NOT rely on DeviceRegistry to supply the interval or resolve the IP. The config author writes the interval directly in the tenant metric entry.

**New `MetricSlotOptions` shape:**
```json
{
  "Ip": "npb-simulator.simetra.svc.cluster.local",
  "Port": 161,
  "MetricName": "npb_cpu_util",
  "IntervalSeconds": 10,
  "TimeSeriesSize": 10
}
```

`IntervalSeconds` becomes a first-class field on `MetricSlotOptions` (currently absent). The existing `TimeSeriesSize` field is already there.

**Impact on `TenantVectorRegistry.Reload()`:**

`DeriveIntervalSeconds()` is eliminated. It was the only reason `TenantVectorRegistry` needed `IOidMapService`. The new `intervalSeconds` comes directly from the config object.

`ResolveIp()` remains if the tenant config can contain DNS names (it currently does: `npb-simulator.simetra.svc.cluster.local`). However, the DNS-to-IP resolution that `ResolveIp()` performs today is a delegating lookup into `DeviceRegistry.AllDevices` — it does not do DNS itself. If the feature intent is "self-describing means no DeviceRegistry dependency at all", then the tenant config must either:
  - Accept only resolved IPs, OR
  - Do its own DNS resolution in `Reload()` (new DNS call, no DeviceRegistry)

The simpler and more consistent interpretation: **`MetricSlotOptions.Ip` must be a resolved IP address or a DNS name that `TenantVectorRegistry` resolves directly** (same as `DeviceRegistry` does with `Dns.GetHostAddresses`). This removes the `IDeviceRegistry` constructor dependency from `TenantVectorRegistry`.

**Constructor change:**

Current:
```csharp
public TenantVectorRegistry(IDeviceRegistry deviceRegistry, IOidMapService oidMapService, ILogger<TenantVectorRegistry> logger)
```

After removing both usages:
```csharp
public TenantVectorRegistry(ILogger<TenantVectorRegistry> logger)
```

Both `_deviceRegistry` and `_oidMapService` fields are removed. The DI registration in `ServiceCollectionExtensions` is simplified accordingly.

**Impact on `Reload()` signature:** No change to the method signature. `TenantVectorOptions` is still the parameter. The internal body changes: build `MetricSlotHolder` from `metric.IntervalSeconds` directly instead of calling `DeriveIntervalSeconds`.

**Impact on routing index:** No change to structure. The routing index is still `FrozenDictionary<RoutingKey(ip, port, metricName), IReadOnlyList<MetricSlotHolder>>`. The values in the routing index now use the interval supplied directly from config. The routing key tuple is identical.

**Carry-over logic:** No change. Carry-over is keyed on `(ip, port, metricName)` and is not affected by where `intervalSeconds` came from.

---

### Feature 2: Tenant Commands Array

**What changes:** `TenantOptions` gains a `Commands` list alongside `Metrics`. Each command entry has a name, value, and value type.

**New config shape:**
```json
{
  "Priority": 1,
  "Metrics": [...],
  "Commands": [
    { "Name": "obp_set_bypass_L1", "Value": "1", "ValueType": "Integer32" }
  ]
}
```

**New configuration model: `TenantCommandOptions`**
```csharp
public sealed class TenantCommandOptions
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
}
```

**Modified `TenantOptions`:**
```csharp
public List<TenantCommandOptions> Commands { get; set; } = [];
```
Adding `Commands` is additive and backward-compatible — existing configs without `Commands` will deserialize with an empty list.

**New runtime model: `TenantCommand`**

The `Tenant` class needs to carry commands at runtime. Parallel to `Holders` (which holds `MetricSlotHolder` instances), a `Commands` property would hold the command definitions:
```csharp
public sealed class Tenant
{
    public string Id { get; }
    public int Priority { get; }
    public IReadOnlyList<MetricSlotHolder> Holders { get; }
    public IReadOnlyList<TenantCommand> Commands { get; }   // NEW
}
```

`TenantCommand` is a simple value record (no live state):
```csharp
public sealed record TenantCommand(string Name, string Value, string ValueType);
```

**Impact on `TenantVectorRegistry.Reload()`:**
During the build loop, after building `holders`, build `commands` from `tenantOpts.Commands`. Construct `Tenant` with both. No routing index needed for commands — they are accessed by iterating `group.Tenants[i].Commands`.

**Impact on routing index:** None. Commands are not part of the routing index.

**Impact on `ITenantVectorRegistry`:** No interface change required unless consumers need to query commands by tenant ID. For now, commands are accessed via `Groups → Tenants → Commands`. No new interface method is needed for this milestone.

---

### Feature 3: CommunityString Validation

**What changes:** Every `CommunityString` field (in both `DeviceOptions` and `MetricSlotOptions`) must follow the `Simetra.*` pattern when present. Null/empty is allowed (falls back to convention).

**Where validation lives:**

The `Simetra.*` prefix check already exists in `CommunityStringHelper`. The validation logic should live in a dedicated validator, not scattered across watcher services or inline in registry code.

Recommended placement: **expand the existing validators**.

- `DevicesOptionsValidator` gains a `CommunityString` check in `ValidateDevice()`:
  ```
  if CommunityString is not null/empty AND does not start with "Simetra."
      failures.Add(...)
  ```

- `TenantVectorOptionsValidator` (currently a no-op) gains actual validation:
  ```
  for each tenant metric with CommunityString:
      if not null/empty AND not "Simetra.*"
          failures.Add(...)
  ```

**CommunityString on `MetricSlotOptions`:** This is a new field. `MetricSlotOptions` currently has `Ip`, `Port`, `MetricName`, `TimeSeriesSize`. A `CommunityString?` field is added.

**Why validators, not inline:**
- Watcher services already call the validator before calling `Reload()`. Invalid configs are rejected at the watcher boundary with the current config retained. This is the correct guard point.
- Inline checks in `Reload()` would require exception-based control flow or silent skipping — neither matches the existing pattern.
- `CommunityStringHelper` remains a utility; it does not validate (already exists as `TryExtractDeviceName`/`DeriveFromDeviceName`).

**No new validator class needed.** The two existing validator classes absorb the new rules.

---

### Feature 4: Name → CommunityString Rename in DeviceOptions

**What changes:** The `DeviceOptions` property `Name` is NOT being renamed — `Name` is the human-readable device name. The change is that `CommunityString` is the explicit SNMP community string field. Looking at the current `DeviceOptions`, it already has `CommunityString` as a property. This rename is already complete in the codebase.

The milestone description says "Name → CommunityString rename". In context, this likely refers to a rename within a different model that has a `Name` property meaning something like community string. Based on the code, `DeviceOptions.Name` is device identity (e.g., "OBP-01") and `DeviceOptions.CommunityString` is the SNMP community. Both already exist with the correct names.

If the rename refers to `MetricSlotOptions` gaining a `CommunityString` field (which it does not currently have), that is a new field addition, not a rename. The `TenantOptions` model similarly has no field named `Name` that maps to a community string.

**Assessment:** This feature is either already done (DeviceOptions already has CommunityString), or refers to a rename within a model that is yet to be identified. The roadmap creator should clarify scope. For now, this research treats it as: `MetricSlotOptions` gains an optional `CommunityString?` field (aligns with feature 3 where validation is needed).

---

### Feature 5: Skip Poll Job When All Metric Names Unresolvable

**Context from Phase 31 decisions:** "Poll group behavior: within a poll group, resolved names are collected into one poll job. If zero names resolve in a group, no job for that group." This was already decided for Phase 31.

**Where the skip logic lives:**

In `DeviceRegistry.BuildPollGroups()`, a `MetricPollInfo` is currently returned for every poll group, even if `resolvedOids` is empty. The skip behavior means: if `resolvedOids.Count == 0`, do NOT include this group in the returned `ReadOnlyCollection<MetricPollInfo>`.

```csharp
// Current (always includes group):
return new MetricPollInfo(PollIndex: index, Oids: resolvedOids.AsReadOnly(), IntervalSeconds: ...);

// After skip behavior:
if (resolvedOids.Count == 0)
{
    _logger.LogWarning("Device '{DeviceName}' poll group {index}: all {count} metric names unresolvable -- skipping job");
    return; // or continue in LINQ select
}
return new MetricPollInfo(...);
```

The `Select` + `ToList` in `BuildPollGroups` needs to become a `SelectMany` or filtered `Where`, since `Select` requires a 1:1 transformation. The simplest refactor is changing from LINQ `Select` to a foreach loop with conditional `Add`.

**Impact on `DynamicPollScheduler`:** No change. The scheduler sees only the `MetricPollInfo` list that `DeviceRegistry` exposes. If a group produces no `MetricPollInfo`, the scheduler simply has fewer jobs to create. This is already how removal works.

**Impact on initial Quartz registration in `ServiceCollectionExtensions.AddSnmpScheduling()`:**

The startup registration iterates `devicesOptions.Devices` and registers a Quartz job per `device.Polls[pi]`. It does not consult `IOidMapService` at this point (the OID map may be empty at startup). After the skip behavior, the startup registration could still create a job for a poll group whose MetricNames are all invalid — this job would fire and send an empty OID list to SNMP. This is a latent issue pre-existing from v1.6. The correct fix is the same: skip groups with zero names at `BuildPollGroups` time, and since `DeviceRegistry` is called during `DeviceWatcherService.HandleConfigMapChangedAsync` → `ReconcileAsync`, the reconcile will produce the correct desired set. The startup Quartz registration (in `AddSnmpScheduling`) is a separate path that should also apply the same skip logic — but since Phase 31 decisions indicate this is "no job for that group", the reconcile path is what matters for hot-reload.

---

### Feature 6: tenantvector → tenants Rename

**What changes:** The ConfigMap name `simetra-tenantvector` and key `tenantvector.json` are renamed. The new names are `simetra-tenants` and `tenants.json` (inferred from "tenantvector → tenants" rename).

**Touched files:**

| File | Change |
|------|--------|
| `Services/TenantVectorWatcherService.cs` | `ConfigMapName = "simetra-tenants"`, `ConfigKey = "tenants.json"` |
| `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` | Rename file to `simetra-tenants.yaml`; update `metadata.name` and data key |
| `Program.cs` | Local dev path reads `tenants.json` (currently reads `tenantvector.json`) |
| `TenantVectorOptions.SectionName` | Currently `"TenantVector"` -- if the section name also renames to `"Tenants"`, the JSON wrapper in local dev changes accordingly |

**The DI registration class name stays the same:** `TenantVectorWatcherService`, `TenantVectorRegistry`, `TenantVectorOptions` are internal class names. The rename is external (ConfigMap names, file names, JSON keys). No class renaming is required unless explicitly in scope.

**`TenantVectorOptions.SectionName`:** Currently `"TenantVector"`. The current `simetra-tenantvector.yaml` uses `{ "Tenants": [...] }` as the JSON content (no section wrapper — it's unwrapped). The local `tenantvector.json` uses `{ "TenantVector": { "Tenants": [...] } }` (section-wrapped for IConfiguration binding). If the ConfigMap key becomes `tenants.json` and the section wrapper also renames to `"Tenants"`, then `TenantVectorOptions.SectionName` changes to `"Tenants"` and the local dev JSON wrapper key changes. If the section wrapper stays as `"TenantVector"` but the file name changes to `tenants.json`, only the file names and ConfigMap key change.

**Recommendation:** Keep `TenantVectorOptions.SectionName = "TenantVector"` (internal binding key) and only rename the ConfigMap/file names. This isolates the rename to infrastructure artifacts, not the options binding chain.

---

### Feature 7: Remove Redundant Code from DeviceRegistry Tenant Dependency

**Redundant code that is eliminated when metrics become self-describing:**

1. `TenantVectorRegistry._deviceRegistry` field — removed entirely
2. `TenantVectorRegistry._oidMapService` field — removed entirely
3. `TenantVectorRegistry.ResolveIp()` method — removed (or replaced with direct DNS resolution)
4. `TenantVectorRegistry.DeriveIntervalSeconds()` method — removed
5. Constructor parameter `IDeviceRegistry deviceRegistry` — removed
6. Constructor parameter `IOidMapService oidMapService` — removed
7. DI registration in `ServiceCollectionExtensions` that passes these two parameters — simplified

**Redundant code that is eliminated from `TenantVectorFanOutBehavior` when tenants carry their own port:**

Currently `TenantVectorFanOutBehavior` calls `_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device)` to get the port. If tenant metrics are self-describing and the routing index is built with the correct port already embedded in the `RoutingKey`, then the behavior can route using `msg.AgentIp` + port from the holder (or from the routing key lookup itself).

However: the routing lookup is `TryRoute(ip, port, metricName)`. The behavior needs to know the port to call `TryRoute`. There are two options:

- **Option A:** Keep `IDeviceRegistry` in `TenantVectorFanOutBehavior` for port lookup (no change here). This means `TenantVectorFanOutBehavior` still depends on `DeviceRegistry` even after the tenant side is self-describing. Not fully redundant.
- **Option B:** Change `TryRoute` to accept `ip` and `metricName` only (drop port), and change the routing key to `(ip, metricName)`. This removes all DeviceRegistry dependency from the fan-out path. However it risks key collisions if two devices share an IP but use different ports with the same metric name — unlikely in practice but architecturally risky.
- **Option C:** Expose port directly from `MetricSlotHolder` (already available: `holder.Port`) and restructure the fan-out to look up by IP only, then filter by port. Overhead but correct.

**Recommended:** Option A — leave `TenantVectorFanOutBehavior._deviceRegistry` in place for this milestone. The port-lookup dependency is a small, well-contained coupling. Removing it requires a routing key model change that touches the hot path. Flag it as a future cleanup.

---

## Component Change Map

### New Files

| File | Kind | Purpose |
|------|------|---------|
| `Configuration/TenantCommandOptions.cs` | Config model | `Name`, `Value`, `ValueType` per tenant command |
| `Pipeline/TenantCommand.cs` | Runtime record | Immutable command descriptor carried by `Tenant` |

### Modified Files

| File | Change |
|------|--------|
| `Configuration/MetricSlotOptions.cs` | Add `IntervalSeconds` field; add optional `CommunityString?` field |
| `Configuration/TenantOptions.cs` | Add `List<TenantCommandOptions> Commands { get; set; } = []` |
| `Configuration/Validators/TenantVectorOptionsValidator.cs` | Replace no-op with real validation: non-empty CommunityString must match `Simetra.*`; `IntervalSeconds > 0` per metric slot |
| `Configuration/Validators/DevicesOptionsValidator.cs` | Add CommunityString pattern check in `ValidateDevice()` |
| `Pipeline/TenantVectorRegistry.cs` | Remove `IDeviceRegistry` and `IOidMapService` constructor deps; remove `ResolveIp()` and `DeriveIntervalSeconds()`; read interval from `metric.IntervalSeconds`; build `Tenant` with commands |
| `Pipeline/Tenant.cs` | Add `IReadOnlyList<TenantCommand> Commands` property; update constructor |
| `Pipeline/DeviceRegistry.cs` | `BuildPollGroups()`: skip poll group when `resolvedOids.Count == 0` (log warning, exclude from return value) |
| `Services/TenantVectorWatcherService.cs` | Update `ConfigMapName` and `ConfigKey` constants for rename |
| `Extensions/ServiceCollectionExtensions.cs` | Simplify `TenantVectorRegistry` DI registration (no longer passes `IDeviceRegistry` or `IOidMapService`) |
| `Program.cs` | Update local dev file path from `tenantvector.json` to `tenants.json` (if renamed) |
| `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` | Rename file; update `metadata.name` to `simetra-tenants`; update data key to `tenants.json`; add `IntervalSeconds` to metric slot examples; add `Commands` array example |

### Unchanged Files

| File | Reason unchanged |
|------|-----------------|
| `Pipeline/TenantVectorFanOutBehavior.cs` | Port lookup via DeviceRegistry stays (see Feature 7 analysis) |
| `Pipeline/MetricSlotHolder.cs` | No new fields; `IntervalSeconds` already exists on the holder |
| `Pipeline/ITenantVectorRegistry.cs` | Interface surface unchanged: `TryRoute`, `Groups`, `TenantCount`, `SlotCount` |
| `Pipeline/IDeviceRegistry.cs` | No change |
| `Pipeline/DeviceInfo.cs` | No change |
| `Pipeline/CommunityStringHelper.cs` | Used by validators; no change to its own logic |
| `Services/DeviceWatcherService.cs` | Device reload path unchanged |
| `Services/DynamicPollScheduler.cs` | Receives `IReadOnlyList<DeviceInfo>` from registry; only sees the filtered list |
| `Pipeline/OidMapService.cs` / `IOidMapService.cs` | No change |
| `Pipeline/CommandMapService.cs` / `ICommandMapService.cs` | No change |
| `Services/OidMapWatcherService.cs` | No change |
| `Services/CommandMapWatcherService.cs` | No change |
| All MediatR behaviors except `TenantVectorFanOutBehavior` | No change |
| `Pipeline/RoutingKey.cs` | No change (key shape stays `(ip, port, metricName)`) |
| `Pipeline/PriorityGroup.cs` | No change |

---

## Data Flow After v1.7

### Tenant Config Load Path

```
simetra-tenants ConfigMap (renamed from simetra-tenantvector)
    key: tenants.json (renamed from tenantvector.json)
    |
    TenantVectorWatcherService (updated ConfigMapName/ConfigKey constants)
    |  JsonDeserialize -> TenantVectorOptions
    |  TenantVectorOptionsValidator.Validate()
    |    - each metric.IntervalSeconds > 0
    |    - each metric.CommunityString if present matches "Simetra.*"
    |    - each command.Name non-empty
    v
TenantVectorRegistry.Reload(TenantVectorOptions)
    |  for each TenantOptions:
    |      for each MetricSlotOptions metric:
    |          resolvedIp = DirectResolve(metric.Ip)    // DNS or pass-through (NO DeviceRegistry)
    |          new MetricSlotHolder(resolvedIp, metric.Port, metric.MetricName,
    |                               metric.IntervalSeconds, metric.TimeSeriesSize)
    |          carry-over old value by (ip, port, metricName)
    |      for each TenantCommandOptions cmd:
    |          new TenantCommand(cmd.Name, cmd.Value, cmd.ValueType)
    |      new Tenant(id, priority, holders, commands)
    |  build FrozenDictionary routing index
    |  volatile swap _groups and _routingIndex
```

### Device Config Load Path (with skip behavior)

```
simetra-devices ConfigMap (unchanged name)
    |
    DeviceWatcherService
    |  JsonDeserialize -> List<DeviceOptions>
    v
DeviceRegistry.ReloadAsync(List<DeviceOptions>)
    |  for each DeviceOptions d:
    |      DNS resolution -> resolved IP
    |      BuildPollGroups(d.Polls, d.Name):
    |          for each PollOptions poll:
    |              for each MetricName name:
    |                  oid = _oidMapService.ResolveToOid(name)
    |                  if oid is null: warn, skip
    |              if resolvedOids.Count == 0: warn, SKIP THIS GROUP (no MetricPollInfo)
    |              else: emit MetricPollInfo(oids, interval)
    |      DeviceInfo(name, configAddress, resolvedIp, port, pollGroups, communityString)
    |      DevicesOptionsValidator: CommunityString pattern check fires here (watcher path)
    |  FrozenDictionary volatile swap
    v
DynamicPollScheduler.ReconcileAsync(AllDevices)
    |  desired jobs = only poll groups that survived skip filter
    |  add/remove/reschedule Quartz metric-poll-* jobs
```

---

## Build Order for v1.7

Dependencies flow from models → validators → registry → watcher → infrastructure.

### Phase ordering rationale

1. **Config model additions first** (`MetricSlotOptions`, `TenantOptions`, `TenantCommandOptions`, `TenantCommand`)
   - Everything else depends on these shapes. No dependencies upstream.
   - `MetricSlotOptions.IntervalSeconds` must exist before `TenantVectorRegistry.Reload()` can use it.
   - `TenantCommandOptions` must exist before `TenantOptions.Commands` compiles.

2. **Validator changes second** (`TenantVectorOptionsValidator`, `DevicesOptionsValidator`)
   - Depends on updated model shapes.
   - Fail-fast at watcher boundary; must be correct before watcher integration is tested.
   - `TenantVectorOptionsValidator` currently a no-op — activating it changes behavior; test it before wiring.

3. **`DeviceRegistry.BuildPollGroups` skip behavior**
   - Depends only on existing `IOidMapService` (unchanged).
   - Isolated change within one method; can be written and unit-tested independently.
   - No dependency on tenant changes.

4. **`TenantVectorRegistry` refactor** (remove DeviceRegistry/OidMapService deps, use `metric.IntervalSeconds`, add commands build)
   - Depends on model additions (step 1).
   - Remove two constructor parameters; DI registration update is part of this step.
   - Carry-over logic and routing index construction are unchanged structurally.

5. **`Tenant` runtime model update** (add `Commands` property)
   - Must happen in the same step as `TenantVectorRegistry` refactor — they compile together.

6. **ConfigMap rename** (`simetra-tenantvector` → `simetra-tenants`, `TenantVectorWatcherService` constants)
   - Purely mechanical; no logic changes.
   - Last to minimize noise on prior steps' diffs.
   - Requires updating: watcher constants, YAML manifests, local dev `Program.cs` file path.

7. **K8s ConfigMap YAML updates** (`simetra-tenantvector.yaml` → `simetra-tenants.yaml`)
   - Add `IntervalSeconds` to existing metric slot examples.
   - Add `Commands` array example entries.
   - CommunityString validation: existing entries have no CommunityString fields, so they pass.

### Suggested Phase Structure

| Phase | Content | Key files |
|-------|---------|-----------|
| Phase A | Config models: `MetricSlotOptions` + `IntervalSeconds` + `CommunityString?`; `TenantCommandOptions`; `TenantCommand` record | `MetricSlotOptions.cs`, `TenantOptions.cs`, `TenantCommandOptions.cs`, `TenantCommand.cs` |
| Phase B | Validators: activate `TenantVectorOptionsValidator`; add CommunityString check to `DevicesOptionsValidator` | `TenantVectorOptionsValidator.cs`, `DevicesOptionsValidator.cs` |
| Phase C | `DeviceRegistry.BuildPollGroups` skip behavior + unit tests | `DeviceRegistry.cs`, test file |
| Phase D | `TenantVectorRegistry` refactor: remove deps, use `metric.IntervalSeconds`, build commands; `Tenant` commands; DI registration | `TenantVectorRegistry.cs`, `Tenant.cs`, `ServiceCollectionExtensions.cs` |
| Phase E | ConfigMap rename + YAML updates | `TenantVectorWatcherService.cs`, `simetra-tenantvector.yaml` (rename), `Program.cs` |

Phases A and B can be combined. Phases C and D can be combined if tests are included. Phase E is always last.

---

## Confidence Assessment

| Area | Confidence | Basis |
|------|------------|-------|
| `TenantVectorRegistry` DeviceRegistry removal | HIGH | Direct code read; both usages (`ResolveIp`, `DeriveIntervalSeconds`) identified and traced |
| `MetricSlotOptions.IntervalSeconds` new field | HIGH | Absence confirmed by reading the model file |
| `TenantCommandOptions` new model | HIGH | `TenantOptions.Commands` does not exist; no `Commands` field anywhere in config models |
| `DeviceRegistry.BuildPollGroups` skip | HIGH | Current code returns all groups regardless of resolved count; skip behavior is a 5-line change |
| CommunityString validation placement | HIGH | Existing validator pattern is clear; `CommunityStringHelper` prefix constant already defined |
| ConfigMap rename scope | MEDIUM | Rename touches watcher constants, YAML, Program.cs -- exhaustive list requires grep verification |
| "Name → CommunityString rename" scope | LOW | Current `DeviceOptions` already has both `Name` and `CommunityString`; the rename target is unclear from milestone description; needs clarification |
| `TenantVectorFanOutBehavior` DeviceRegistry dep | HIGH | Code read confirms port lookup via DeviceRegistry; removing it requires routing key model change (deferred) |

---

## Open Questions for Roadmap

1. **DNS resolution in `TenantVectorRegistry`:** If `IDeviceRegistry` is removed, how should DNS names in `MetricSlotOptions.Ip` be handled? Options: require resolved IPs only, or add `Dns.GetHostAddressesAsync` in `Reload()`. The current `ResolveIp()` delegates to `DeviceRegistry` which already resolved the DNS. A direct DNS call in `Reload()` works but makes `Reload()` async (currently synchronous). Recommend either: (a) require IP-only in tenant config, or (b) make `Reload()` return `Task` and resolve DNS directly.

2. **"Name → CommunityString rename" clarification:** The milestone description mentions this but both properties already exist in `DeviceOptions` with the correct names. Either this refers to a model not yet identified, or it is referring to adding `CommunityString` as a new required field in a context where `Name` was previously repurposed. Needs clarification before Phase B is planned.

3. **`TenantVectorOptions.SectionName` with rename:** If ConfigMap key changes to `tenants.json` but local dev `tenantvector.json` also renames, what is the IConfiguration binding section key? Currently `"TenantVector"`. If this changes to `"Tenants"`, the IConfiguration binding chain must also update.
