# Architecture Patterns: v1.6 Organization & Command Map Foundation

**Domain:** SNMP monitoring agent -- OID map validation, human-name device config, command map infrastructure
**Researched:** 2026-03-13
**Confidence:** HIGH (all findings based on direct codebase analysis; no training-data speculation)

---

## Existing Architecture Snapshot

Understanding the current components is required before placement of new features.

### Configuration Load Path (Startup)

```
simetra-oidmaps ConfigMap
    |  watched by OidMapWatcherService (BackgroundService)
    |  ParseJSON -> Dictionary<string, string>
    v
OidMapService.UpdateMap(entries)
    |  MergeWithHeartbeatSeed() -- adds HeartbeatOid
    |  BuildFrozenMap() -- FrozenDictionary<string, string>
    |  volatile swap of _map
    |  volatile swap of _metricNames (FrozenSet for ContainsMetricName)

simetra-devices ConfigMap
    |  watched by DeviceWatcherService (BackgroundService)
    |  ParseJSON -> List<DeviceOptions>
    v
DeviceRegistry.ReloadAsync(List<DeviceOptions>)
    |  DNS resolution per device
    |  builds _byIpPort and _byName FrozenDictionaries
    |  volatile swap of both
    v
DynamicPollScheduler.ReconcileAsync(IReadOnlyList<DeviceInfo>)
    |  diffs current Quartz jobs vs desired
    |  adds / removes / reschedules metric-poll-* jobs
```

### DeviceOptions (existing config model)

```json
{
  "Name": "OBP-01",
  "IpAddress": "127.0.0.1",
  "Port": 10161,
  "CommunityString": null,
  "MetricPolls": [
    { "IntervalSeconds": 10, "Oids": ["1.3.6.1.4.1..."] }
  ]
}
```

`Oids` in `MetricPolls` are raw OID strings today. The human-name feature introduces the ability to express them as metric names instead.

### DynamicPollScheduler Job Key Pattern

```
metric-poll-{device.ConfigAddress}_{device.Port}-{pollGroupIndex}
```

Job data map contains `configAddress`, `port`, `pollIndex`, `intervalSeconds`. These are used at execution time to look up the device from `IDeviceRegistry`.

### OidMapService Internal State

```csharp
private volatile FrozenDictionary<string, string> _map;   // OID -> MetricName
private volatile FrozenSet<string>  _metricNames;          // set of MetricName values
```

The `_metricNames` set already exists on `IOidMapService` via `ContainsMetricName(string)`. There is no reverse lookup (MetricName -> OID) today.

### OidMapWatcherService Parse Location

`HandleConfigMapChangedAsync()` does:
1. JSON deserialization into `Dictionary<string, string>`
2. Null guard
3. `SemaphoreSlim` gate
4. `_oidMapService.UpdateMap(oidMap)`

Validation (duplicate detection) belongs between steps 1 and 3 -- after successful parse, before calling `UpdateMap`.

---

## Integration Architecture: Three New Features

### Feature 1: OID Map Duplicate Validation

**What:** Detect when two OIDs map to the same MetricName in `oidmaps.json`. The JSON `Dictionary<string, string>` deserialization silently accepts duplicates at the value level (many OIDs can share a name) -- this is the case we want to detect and warn about at load time.

**Where to integrate:** `OidMapWatcherService.HandleConfigMapChangedAsync()`, immediately after successful JSON deserialization, before `UpdateMap`.

**Why there, not in OidMapService.UpdateMap:** The watcher is the parse boundary. It is the appropriate place for data quality checks that operate on the raw incoming map. `UpdateMap` is a pure swap method -- keeping validation separate preserves single-responsibility and makes the watcher independently testable. The local-dev path in `Program.cs` also calls `UpdateMap` directly (after loading `oidmaps.json` from disk), so any validation placed only in `UpdateMap` would also run for local dev; whether that is desirable is a decision point.

**Recommended: validate in watcher only.** Local dev does not need the warning -- duplicate detection is primarily a production config quality signal.

**Data flow addition:**

```
OidMapWatcherService.HandleConfigMapChangedAsync()
    |  JSON deserialize -> Dictionary<string, string>
    |  [NEW] ValidateDuplicateNames(oidMap) -> logs warnings per duplicate MetricName
    |         groups by Value, filters groups.Count > 1
    |         LogWarning per group: "MetricName '{name}' mapped from {N} OIDs: ..."
    |  (continues to UpdateMap regardless -- validation is advisory, not blocking)
    v
OidMapService.UpdateMap(oidMap)
```

The check does NOT block the reload. An operator error in OID naming should not prevent metrics from flowing. The warning is sufficient.

**IOidMapService interface:** No changes needed. Validation is watcher-internal.

**New private method in OidMapWatcherService:**

```csharp
private void ValidateDuplicateMetricNames(Dictionary<string, string> oidMap)
{
    var duplicates = oidMap
        .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1);

    foreach (var group in duplicates)
    {
        _logger.LogWarning(
            "OID map validation: MetricName '{MetricName}' is assigned to {Count} OIDs: {Oids}. " +
            "Only one OID per MetricName is recommended for unambiguous resolution.",
            group.Key,
            group.Count(),
            string.Join(", ", group.Select(kv => kv.Key)));
    }
}
```

**Confidence:** HIGH -- based on direct read of `OidMapWatcherService.HandleConfigMapChangedAsync` and `OidMapService.UpdateMap`.

---

### Feature 2: Reverse-Lookup Index (MetricName -> OID)

**What:** Complement to the forward map. Enables the device config to reference OIDs by human name (`"obp_channel_L1"`) rather than raw OID string (`"1.3.6.1.4.1.47477.10.21.1.3.4.0"`). Also enables command dispatch: given a MetricName, what OID do we SET?

**Where to integrate:** `OidMapService`. Add a second `volatile FrozenDictionary<string, string>` for the reverse map, built and swapped alongside `_map` in `UpdateMap` and the constructor.

**Note on uniqueness:** The reverse index assumes MetricName -> single OID. If duplicates exist (caught by Feature 1), the reverse index retains one entry arbitrarily (last-writer-wins from dictionary iteration order). Feature 1 warns about this situation; Feature 2 documents the consequence.

**Changes to `IOidMapService`:**

```csharp
/// <summary>
/// Resolves a metric name back to its OID string.
/// Returns null if the metric name is not in the map.
/// Used by device config human-name resolution and command dispatch.
/// </summary>
string? ResolveOid(string metricName);
```

**Changes to `OidMapService`:**

```csharp
private volatile FrozenDictionary<string, string> _reverseMap;  // MetricName -> OID

// In constructor and UpdateMap:
_reverseMap = BuildReverseMap(seeded);

// BuildReverseMap:
private static FrozenDictionary<string, string> BuildReverseMap(Dictionary<string, string> entries)
{
    // Last-writer-wins for duplicate MetricNames
    var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (oid, name) in entries)
        reverse[name] = oid;
    return reverse.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}

// ResolveOid implementation:
public string? ResolveOid(string metricName)
    => _reverseMap.TryGetValue(metricName, out var oid) ? oid : null;
```

**Both maps rebuild together on every `UpdateMap` call** -- they are always in sync. The `volatile` field swap ensures atomic visibility to concurrent readers.

**Confidence:** HIGH.

---

### Feature 3: Human-Name Device Config

**What:** Allow `MetricPolls[].Oids` entries in `devices.json` to be either raw OID strings or human metric names. Before Quartz jobs are created, names are resolved back to OIDs.

**Where to integrate:** `DeviceWatcherService.HandleConfigMapChangedAsync()`, between JSON deserialization and `DeviceRegistry.ReloadAsync`. This is the mirror of where OID map validation integrates in Feature 1.

**Dependency order issue:** `DeviceWatcherService` currently depends only on `IDeviceRegistry` and `DynamicPollScheduler`. Human-name resolution requires `IOidMapService`. This is a new dependency injection point.

**Change to `DeviceWatcherService` constructor:**

```csharp
public DeviceWatcherService(
    IKubernetes kubeClient,
    IDeviceRegistry deviceRegistry,
    DynamicPollScheduler pollScheduler,
    IOidMapService oidMapService,           // NEW
    ILogger<DeviceWatcherService> logger)
```

**Resolution logic** (new private method):

```csharp
private List<DeviceOptions> ResolveHumanNames(List<DeviceOptions> devices)
{
    // For each device, for each poll group, for each OID entry:
    // if it looks like a metric name (no dots, matches OidMapService.ResolveOid),
    // replace with the resolved OID.
    // Log a warning if a name cannot be resolved (leave as-is, will become "Unknown" in pipeline).
    foreach (var device in devices)
    {
        foreach (var poll in device.MetricPolls)
        {
            for (var i = 0; i < poll.Oids.Count; i++)
            {
                var entry = poll.Oids[i];
                if (!IsRawOid(entry))
                {
                    var resolved = _oidMapService.ResolveOid(entry);
                    if (resolved is null)
                        _logger.LogWarning(
                            "Device '{Device}': OID entry '{Entry}' is not a raw OID and not found in OID map -- leaving as-is",
                            device.Name, entry);
                    else
                        poll.Oids[i] = resolved;
                }
            }
        }
    }
    return devices;
}

private static bool IsRawOid(string entry)
    => entry.Length > 0 && (char.IsDigit(entry[0]) || entry[0] == '.');
```

**Timing dependency -- startup ordering risk:** `DeviceWatcherService` and `OidMapWatcherService` are both `BackgroundService` instances that start concurrently. At startup, the OID map may not be loaded yet when the device watcher first runs its initial load. This is a real race.

**Resolution strategy:** The device watcher should attempt name resolution, log unresolved names as warnings, but proceed with raw OID strings for unresolved entries. This is the same "degrade gracefully" policy the existing pipeline uses (unresolved OIDs become `"Unknown"`). At the next ConfigMap reload cycle, if the OID map has loaded by then, device name resolution will succeed.

An alternative -- making `DeviceWatcherService` wait for `OidMapWatcherService` to complete its initial load -- introduces a startup ordering dependency that is fragile and contradicts the existing design of independent watchers. Do not add that dependency.

**Confidence:** HIGH on the integration point; MEDIUM on startup race handling (the graceful-degrade approach is correct but should be explicitly tested).

---

### Feature 4: CommandMapService

**What:** A new singleton service for writable OIDs (SNMP SET targets). Structurally parallel to `OidMapService`, but for a different map: `commandName -> OID`. Enables the system to look up the OID for a named command (e.g., `"obp_bypass_activate"` -> `"1.3.6.1.4.1.47477.10.21.1.2.1.0"`).

**Why a separate service, not merged into OidMapService:** The two maps have different semantics. The OID map is poll-oriented (OID -> MetricName for read). The command map is write-oriented (CommandName -> OID for SET). Keeping them separate avoids confusion and lets each evolve independently. The pattern is already established in the codebase -- `OidMapService` and `DeviceRegistry` are separate singletons for the same reason.

**New components:**

```
src/SnmpCollector/Pipeline/ICommandMapService.cs
src/SnmpCollector/Pipeline/CommandMapService.cs
src/SnmpCollector/Configuration/CommandMapOptions.cs
```

**`ICommandMapService`:**

```csharp
public interface ICommandMapService
{
    /// <summary>Resolves a command name to its OID. Returns null if not found.</summary>
    string? ResolveCommandOid(string commandName);

    /// <summary>Number of entries in the command map.</summary>
    int EntryCount { get; }

    /// <summary>Replaces the command map atomically. Called by watcher on ConfigMap change.</summary>
    void UpdateMap(Dictionary<string, string> entries);
}
```

**`CommandMapService`:** Structurally identical to `OidMapService` minus the `_metricNames` set and the heartbeat seed merge. Uses `volatile FrozenDictionary<string, string>` with the same volatile-swap pattern.

**`CommandMapOptions`:**

```csharp
public sealed class CommandMapOptions
{
    public const string SectionName = "CommandMap";
    public Dictionary<string, string> Entries { get; set; } = [];
}
```

**ConfigMap format (`simetra-commandmaps`):**

```json
{
  "obp_bypass_activate":   "1.3.6.1.4.1.47477.10.21.1.2.1.0",
  "obp_bypass_deactivate": "1.3.6.1.4.1.47477.10.21.1.2.2.0"
}
```

The ConfigMap key should be `commandmaps.json`, matching the `oidmaps.json` naming convention.

---

### Feature 5: CommandMapWatcherService

**What:** `BackgroundService` that watches `simetra-commandmaps` ConfigMap and calls `ICommandMapService.UpdateMap()`. Structurally identical to `OidMapWatcherService`.

**New file:**

```
src/SnmpCollector/Services/CommandMapWatcherService.cs
```

**Template:** Clone `OidMapWatcherService` with:
- `ConfigMapName = "simetra-commandmaps"`
- `ConfigKey = "commandmaps.json"`
- Dependency: `ICommandMapService` instead of `IOidMapService`
- Remove the K8s config-only guard (OidMapWatcherService is already guard-gated in `ServiceCollectionExtensions`; follow the same pattern)

**No validation of command map duplicates is needed at this phase.** Command names are unique by definition (a `Dictionary<string, string>` key). OID values could be duplicated (two commands hitting the same OID) -- that is valid.

**DI registration (in `ServiceCollectionExtensions.AddSnmpConfiguration`, inside `IsInCluster()` block):**

```csharp
services.AddSingleton<ICommandMapService, CommandMapService>();

services.AddSingleton<CommandMapWatcherService>();
services.AddHostedService(sp => sp.GetRequiredService<CommandMapWatcherService>());
```

**Local dev fallback (Program.cs):** Add after the oidmaps.json block, following the same pattern. A `config/commandmaps.json` file with a small set of test commands.

**K8s manifest:**

```
deploy/k8s/snmp-collector/simetra-commandmaps.yaml
```

---

## Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| `OidMapWatcherService` | Watch `simetra-oidmaps`, validate duplicates, call `UpdateMap` | `IOidMapService` |
| `OidMapService` | Forward OID->Name resolution, reverse Name->OID resolution | `IDeviceRegistry` (indirectly via DeviceWatcher) |
| `DeviceWatcherService` | Watch `simetra-devices`, resolve human names, reload registry | `IDeviceRegistry`, `DynamicPollScheduler`, `IOidMapService` (NEW) |
| `CommandMapService` | CommandName->OID resolution, hot-reload via atomic swap | None (read by future SNMP SET handler) |
| `CommandMapWatcherService` | Watch `simetra-commandmaps`, call `UpdateMap` | `ICommandMapService` |

---

## Data Flow Changes

### OID Map Load (modified)

```
simetra-oidmaps ConfigMap
    v
OidMapWatcherService.HandleConfigMapChangedAsync
    |  JSON deserialize
    |  [NEW] ValidateDuplicateMetricNames() -- log warnings, continue
    v
OidMapService.UpdateMap
    |  MergeWithHeartbeatSeed
    |  BuildFrozenMap() -> volatile swap _map
    |  [NEW] BuildReverseMap() -> volatile swap _reverseMap
```

### Device Load (modified)

```
simetra-devices ConfigMap
    v
DeviceWatcherService.HandleConfigMapChangedAsync
    |  JSON deserialize -> List<DeviceOptions>
    |  [NEW] ResolveHumanNames(devices, _oidMapService)
    |         for each OID entry: if IsMetricName -> ResolveOid -> replace
    v
DeviceRegistry.ReloadAsync(List<DeviceOptions>)
    v
DynamicPollScheduler.ReconcileAsync(IReadOnlyList<DeviceInfo>)
```

### Command Map Load (new path)

```
simetra-commandmaps ConfigMap
    v
CommandMapWatcherService.HandleConfigMapChangedAsync
    |  JSON deserialize -> Dictionary<string, string>
    v
CommandMapService.UpdateMap
    |  BuildFrozenMap() -> volatile swap
```

---

## Modified Components

| File | What Changes | Scope |
|------|-------------|-------|
| `Services/OidMapWatcherService.cs` | Add `ValidateDuplicateMetricNames()` call after JSON parse | ~20 lines |
| `Pipeline/IOidMapService.cs` | Add `ResolveOid(string metricName)` method | 1 method |
| `Pipeline/OidMapService.cs` | Add `_reverseMap` field, `BuildReverseMap()`, `ResolveOid()` impl, update both constructor and `UpdateMap` | ~20 lines |
| `Services/DeviceWatcherService.cs` | Add `IOidMapService` constructor parameter, add `ResolveHumanNames()` method | ~40 lines |
| `Extensions/ServiceCollectionExtensions.cs` | Register `ICommandMapService`, `CommandMapWatcherService` in K8s block; register `ICommandMapService` singleton | ~10 lines |
| `Program.cs` | Add local-dev loading for `commandmaps.json` | ~15 lines |

## New Components

| File | Type | Description |
|------|------|-------------|
| `Pipeline/ICommandMapService.cs` | Interface | `ResolveCommandOid`, `EntryCount`, `UpdateMap` |
| `Pipeline/CommandMapService.cs` | Singleton service | Volatile FrozenDictionary, same pattern as OidMapService |
| `Configuration/CommandMapOptions.cs` | Options POCO | `Dictionary<string, string> Entries` |
| `Services/CommandMapWatcherService.cs` | BackgroundService | Clone of OidMapWatcherService for `simetra-commandmaps` |
| `config/commandmaps.json` | Config file | Local dev fallback command map |
| `deploy/k8s/snmp-collector/simetra-commandmaps.yaml` | K8s manifest | ConfigMap for command map data |

## Files That Do NOT Change

| File | Why Unchanged |
|------|---------------|
| `Pipeline/SnmpOidReceived.cs` | No new message properties needed |
| `Jobs/MetricPollJob.cs` | OID entries already raw strings by the time Quartz runs |
| `Pipeline/DeviceRegistry.cs` | No changes -- receives already-resolved `DeviceOptions` |
| `Services/DynamicPollScheduler.cs` | No changes -- receives `DeviceInfo` from registry |
| `Pipeline/Behaviors/OidResolutionBehavior.cs` | No changes -- forward resolution path unchanged |
| `Services/TenantVectorWatcherService.cs` | Independent -- no coupling to command map |
| `Pipeline/TenantVectorRegistry.cs` | Independent |
| All MediatR behaviors | Unaffected by map infrastructure |

---

## Patterns to Follow

### Pattern: Volatile FrozenDictionary Swap (existing, replicate for CommandMapService)

All hot-reloadable maps in this codebase use the same pattern:
- `private volatile FrozenDictionary<string, string> _map`
- Constructor builds initial `FrozenDictionary` (from empty dict or seed)
- `UpdateMap()` builds new dict, assigns to `_map` with volatile write
- Readers call `_map.TryGetValue()` -- no locking needed (immutable dict, volatile read)

This pattern is well-established in `OidMapService` and `DeviceRegistry`. `CommandMapService` must follow it exactly.

### Pattern: Watcher with Initial Load + Reconnect Loop (existing, replicate for CommandMapWatcherService)

Both existing watchers (`OidMapWatcherService`, `DeviceWatcherService`) share identical structure:
1. Initial load via `ReadNamespacedConfigMapAsync` (non-watch, gets current state immediately)
2. Watch loop with `ListNamespacedConfigMapWithHttpMessagesAsync` + `WatchAsync`
3. Handles `Added`/`Modified`: call `Handle...Async()`
4. Handles `Deleted`: log warning, retain current state
5. On normal watch closure (30-min timeout): reconnect silently
6. On unexpected disconnect: log warning, delay 5s, reconnect
7. `SemaphoreSlim(1,1)` for reload serialization

`CommandMapWatcherService` must follow this structure exactly. Copy from `OidMapWatcherService` and change `ConfigMapName`, `ConfigKey`, and the service dependency type.

### Pattern: Graceful Degrade on Parse Failure

Both existing watchers return early (skip reload) on JSON parse failure, retaining the previous map. `CommandMapWatcherService` must follow the same pattern. This keeps the last-known-good state active.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Making Device Load Wait for OID Map Load

**What:** Blocking `DeviceWatcherService.HandleConfigMapChangedAsync` until `OidMapService` has a non-empty map.

**Why bad:** Creates startup ordering coupling between two independent `BackgroundService` instances. If the OID map fails to load (network issue), device polling never starts. The existing architecture deliberately avoids such dependencies.

**Instead:** Attempt name resolution, log unresolved entries as warnings, proceed with raw OID strings (or leave the entry as-is). The device will poll successfully; the "Unknown" metric name issue is caught separately by Feature 1 warnings.

### Anti-Pattern 2: Putting Validation in OidMapService.UpdateMap

**What:** Adding duplicate detection or any validation logic to `OidMapService.UpdateMap`.

**Why bad:** `UpdateMap` is called from multiple entry points (watcher in K8s mode, `Program.cs` in local-dev mode). Mixing validation into the atomic swap method conflates data quality with state management. Validation should live at the parse/ingest boundary, not in the service's core swap method.

**Instead:** Validate in `OidMapWatcherService.HandleConfigMapChangedAsync`, before calling `UpdateMap`.

### Anti-Pattern 3: Reverse Map as Dictionary<string, List<string>>

**What:** Building the reverse map as `MetricName -> List<OID>` to handle duplicates.

**Why bad:** Command dispatch needs exactly one OID per name. Exposing ambiguity at the API level pushes the resolution decision to every caller. Feature 1 (warnings) is the right mechanism for surfacing the ambiguity; Feature 2 (reverse map) can safely use last-writer-wins.

**Instead:** `FrozenDictionary<string, string>` (one-to-one), built with last-writer-wins. The warning from Feature 1 makes the ambiguity visible to operators.

### Anti-Pattern 4: Merging OID Map and Command Map

**What:** Adding `commandmaps.json` content as a second section of `simetra-oidmaps`, or adding a `Commands` property to `OidMapService`.

**Why bad:** Read-OIDs (poll targets) and write-OIDs (SET targets) have different lifecycles, different audiences (metrics team vs operations team), and different validation rules. Coupling them into one service creates a class with two unrelated responsibilities.

**Instead:** Separate `CommandMapService` / `CommandMapWatcherService` / `simetra-commandmaps` ConfigMap.

### Anti-Pattern 5: Rebuilding the Quartz Job Key After Name Resolution

**What:** Using resolved MetricNames (rather than OID strings or the device ConfigAddress) in Quartz job key construction inside `DynamicPollScheduler`.

**Why bad:** The Quartz job key is currently `metric-poll-{device.ConfigAddress}_{device.Port}-{pollGroupIndex}`. It is based on the stable device identity. If OID-to-name resolution is inserted before `DeviceRegistry.ReloadAsync`, the device options have OID strings again by the time `DynamicPollScheduler` runs. Job key construction is unaffected.

**Instead:** Human-name resolution replaces metric names with OID strings inside `DeviceOptions.MetricPolls[].Oids` before passing to `DeviceRegistry`. The scheduler sees only raw OID strings -- no change to job keys or job data maps.

---

## Build Order

Dependencies flow downward. Each step is independently testable before the next.

### Step 1: Reverse Lookup Index (OidMapService change)

**Files:** `Pipeline/IOidMapService.cs` (+`ResolveOid` method), `Pipeline/OidMapService.cs` (`_reverseMap` field, `BuildReverseMap`, `ResolveOid` impl)

**Tests:** Unit tests for `ResolveOid` (found, not-found, duplicate-name last-writer-wins, case-insensitive, heartbeat seed OID is in reverse map)

**Dependencies:** None -- self-contained change to existing service

**Why first:** Feature 3 (human-name device config) depends on `IOidMapService.ResolveOid`. Feature 2 must exist before Feature 3 can be built.

---

### Step 2: OID Map Duplicate Validation (OidMapWatcherService change)

**Files:** `Services/OidMapWatcherService.cs` (`ValidateDuplicateMetricNames` private method, call site in `HandleConfigMapChangedAsync`)

**Tests:** Unit test with mocked `IOidMapService`: feed a map with duplicates, verify `LogWarning` fires with correct MetricName and OID list. Feed a map without duplicates, verify no warning. Reload does not block on duplicate detection.

**Dependencies:** Step 1 completed (IOidMapService interface stable -- but this step does NOT use ResolveOid; can be done in parallel with Step 1)

**Why here:** Independent of Steps 1 and 3. Can be developed in parallel but keeps the interface stable before adding watcher dependencies.

---

### Step 3: Human-Name Device Config (DeviceWatcherService change)

**Files:** `Services/DeviceWatcherService.cs` (add `IOidMapService` parameter, add `ResolveHumanNames` private method, call it in `HandleConfigMapChangedAsync`)

**Tests:** Unit test: feed `DeviceOptions` with metric names, mock `IOidMapService.ResolveOid` returning OIDs, verify entries replaced. Feed unresolvable name, verify `LogWarning`, entry left as-is. Feed raw OID string, verify `IsRawOid` returns true and entry unchanged.

**Dependencies:** Step 1 (`ResolveOid` on `IOidMapService`)

**Why here:** Cannot build until `IOidMapService.ResolveOid` exists. Must come before Step 4 so the device config can reference command OIDs by name once command map is loaded.

---

### Step 4: CommandMapService (new service)

**Files:** `Configuration/CommandMapOptions.cs`, `Pipeline/ICommandMapService.cs`, `Pipeline/CommandMapService.cs`

**Tests:** Unit tests identical in structure to OidMapService tests: initial state (empty map), `UpdateMap` (volatile swap), `ResolveCommandOid` (found / not-found), `EntryCount`.

**Dependencies:** None -- pure new code, no changes to existing services

**Why here:** CommandMapService can be built in parallel with Steps 1-3. No cross-dependency.

---

### Step 5: CommandMapWatcherService + DI + Config Files

**Files:** `Services/CommandMapWatcherService.cs`, additions to `Extensions/ServiceCollectionExtensions.cs`, additions to `Program.cs`, `config/commandmaps.json`, `deploy/k8s/snmp-collector/simetra-commandmaps.yaml`

**Tests:** Unit test with mocked `IKubernetes` and `ICommandMapService`. Verify initial load path, Added/Modified event path, Deleted event path (retain + warn), JSON parse failure path (skip + log). Integration: deploy ConfigMap in K8s, verify `CommandMapService.EntryCount > 0`.

**Dependencies:** Step 4 (`ICommandMapService` must exist)

**Why last:** Wiring and infrastructure. Can be done after Step 4 independently; no dependency on Steps 1-3.

---

## Startup Ordering Summary

After all five features are built, the startup sequence becomes:

```
BackgroundService startup (concurrent, .NET Generic Host):
  OidMapWatcherService       -- loads simetra-oidmaps, populates OidMapService
  DeviceWatcherService       -- loads simetra-devices, resolves human names via OidMapService
                             -- NOTE: OidMapService may still be empty at this point
                             -- unresolvable names logged as warnings; raw OID left as-is
  CommandMapWatcherService   -- loads simetra-commandmaps, populates CommandMapService
  TenantVectorWatcherService -- loads simetra-tenantvector, populates TenantVectorRegistry
  K8sLeaseElection           -- acquires or waits for leadership

Quartz scheduler startup (after BackgroundServices start):
  DynamicPollScheduler initialized with devices from DeviceRegistry
  Metric poll jobs scheduled
```

The race between OidMapWatcher and DeviceWatcher is a known condition. It is handled by the graceful-degrade strategy in Feature 3. Devices using only raw OID strings in their `MetricPolls` are unaffected.

---

## Scalability Notes

| Concern | Impact |
|---------|--------|
| Reverse map memory | Adds one `FrozenDictionary` of the same size as `_map` (~100 entries for current OID set) -- negligible |
| `ResolveOid` lookup cost | O(1) FrozenDictionary lookup -- same cost as forward `Resolve()` |
| Human-name resolution at reload | O(total OIDs across all devices * map lookup) -- runs once per ConfigMap change, not per poll cycle -- negligible |
| CommandMapService size | Expected small (tens of commands) -- trivial memory overhead |
| CommandMapWatcher startup | Adds one K8s watch connection -- same pattern as existing watchers |

---

## Sources

All findings based on direct codebase analysis (HIGH confidence):

- `src/SnmpCollector/Pipeline/OidMapService.cs` -- internal state structure, `UpdateMap`, `MergeWithHeartbeatSeed`, `BuildFrozenMap`, `ContainsMetricName`
- `src/SnmpCollector/Pipeline/IOidMapService.cs` -- current interface contract
- `src/SnmpCollector/Services/OidMapWatcherService.cs` -- `HandleConfigMapChangedAsync` parse/validate/update sequence; watcher pattern template
- `src/SnmpCollector/Services/DeviceWatcherService.cs` -- existing constructor dependencies; `HandleConfigMapChangedAsync` flow
- `src/SnmpCollector/Services/DynamicPollScheduler.cs` -- job key construction, `ReconcileAsync` inputs
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` -- `ReloadAsync` signature, FrozenDictionary swap pattern
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` -- record shape (Name, ConfigAddress, ResolvedIp, Port, PollGroups)
- `src/SnmpCollector/Configuration/DeviceOptions.cs` -- `MetricPolls` property type (`List<MetricPollOptions>`)
- `src/SnmpCollector/Configuration/MetricPollOptions.cs` -- `Oids: List<string>`
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` -- DI registration patterns, K8s block, local-dev patterns
- `src/SnmpCollector/config/oidmaps.json` -- current OID map format (flat JSON object)
- `src/SnmpCollector/config/devices.json` -- current devices format (JSON array, raw OID strings in MetricPolls)
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` -- ConfigMap structure (`oidmaps.json` key)
- `deploy/k8s/snmp-collector/simetra-devices.yaml` -- ConfigMap structure (`devices.json` key)
