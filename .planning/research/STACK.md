# Technology Stack: OID Map Validation, Human-Name Device Config, and Command Map Foundation

**Project:** SnmpCollector -- v1.6 Organization and Command Map Foundation
**Researched:** 2026-03-13
**Overall confidence:** HIGH (all recommendations use BCL types and patterns already proven in this codebase)

---

## Executive Decision

**Zero new NuGet packages.** The four features in scope -- OID map duplicate detection, reverse-lookup index (human name to OID), command map service, and CommandMapWatcherService -- are pure pattern-replication work. Every data structure, service shape, and validation technique needed is already present in the codebase and ships in the .NET 9 BCL or existing dependencies.

---

## Feature-by-Feature Stack Decisions

### 1. OID Map Duplicate Detection

**What it needs:** At `UpdateMap` time inside `OidMapService`, detect and report duplicate OID keys and duplicate metric-name values before building the `FrozenDictionary`.

| Decision | Type / API | Why |
|----------|-----------|-----|
| Duplicate OID key detection | `HashSet<string>` populated while iterating the incoming `Dictionary<string, string>` | Keys in a raw `Dictionary<string,string>` deserialized from JSON with `PropertyNameCaseInsensitive = true` can silently clobber earlier entries. A `HashSet` pass before `ToFrozenDictionary()` catches this. O(n), zero allocation beyond the set itself. |
| Duplicate metric-name detection | `HashSet<string>` over `values` of the incoming dictionary | Two OIDs mapping to the same metric name produce silent data loss (one value overwrites the other at the metric layer). Detecting this at `UpdateMap` time surfaces the error as an `ILogger.LogWarning` call. Not a fatal error -- the map loads anyway, but the operator sees the duplicate pair in logs. |
| Validation severity | Warning (not startup failure) | OID map is hot-reloaded from a ConfigMap; a fatal exception on every reload would drop the entire map. Log the duplicates and load anyway so monitoring continues. |
| Implementation location | Inside `OidMapService.UpdateMap()` before `BuildFrozenMap()` | Keeps the detection co-located with the single place maps are built. No new class needed. |

**No new types. No new packages.** Uses `HashSet<string>` from `System.Collections.Generic` already in BCL.

---

### 2. Reverse-Lookup Index (Human Name to OID)

**What it needs:** Given a human name (metric name string from the OID map), resolve back to the OID string. Required so `devices.json` and the command map can reference metrics by name rather than raw OID.

| Decision | Type | Why |
|----------|------|-----|
| Index structure | `volatile FrozenDictionary<string, string>` (name -> OID) | Exact mirror of the existing forward `_map` field on `OidMapService`. Built from the same data in `UpdateMap`, swapped atomically. Zero-cost reads: volatile read + `FrozenDictionary.TryGetValue()`. |
| Key comparer | `StringComparer.OrdinalIgnoreCase` | Metric names are case-insensitive in practice. Matches the existing forward map comparer convention. |
| Duplicate name handling | First-wins with a warning log | If two OIDs map to the same name, only the first is entered in the reverse index. The duplicate detection pass (feature 1 above) already logs the conflict. First-wins is deterministic and simple. |
| New interface member | `bool TryResolveOid(string metricName, out string oid)` on `IOidMapService` | Symmetric with the existing `Resolve(string oid)` method. Same interface, same service, no new class. |
| Swap timing | Same `volatile` write as `_map`, inside the same `UpdateMap()` call | One operation under the `SemaphoreSlim` gate: build both forward and reverse FrozenDictionaries, then write both volatile fields. Readers always see a consistent pair. |

**Implementation sketch:**
```csharp
// New field on OidMapService
private volatile FrozenDictionary<string, string> _reverseMap;

// Built alongside _map in UpdateMap() and constructor
private static FrozenDictionary<string, string> BuildReverseMap(
    Dictionary<string, string> entries)
{
    var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (oid, name) in entries)
    {
        // First-wins: only add if name not already present
        reverse.TryAdd(name, oid);
    }
    return reverse.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
```

**No new packages.** `FrozenDictionary` is `System.Collections.Frozen` in BCL since .NET 8.

---

### 3. Human-Name OIDs in `devices.json`

**What it needs:** `MetricPollOptions.Oids` should accept human names (e.g., `"npb_cpu_util"`) in addition to raw OID strings (e.g., `"1.3.6.1.4.1.47477.100.1.1.0"`). At poll time, human names are resolved to OIDs via the reverse-lookup index.

| Decision | Type / Location | Why |
|----------|----------------|-----|
| Config model change | None -- `Oids` stays `List<string>` | The field already holds strings. The interpretation (name vs OID) is resolved at dispatch time, not in the model. Adding a new type would require a migration and complicates deserialization. |
| Resolution point | Inside `MetricPollJob` or the service that issues the SNMP GET, immediately before building the OID list for `SharpSnmpLib` | Resolving at dispatch time means the resolution uses the current hot-loaded reverse map, handling the case where a name's OID changes after a ConfigMap update. |
| Resolution logic | `IOidMapService.TryResolveOid(entry, out var oid)` returning `oid` if found, else treating `entry` as a literal OID string | Allows gradual migration: existing raw-OID entries in `devices.json` continue to work. No flag or mode switch needed. |
| Validation addition to `DevicesOptionsValidator` | Log a warning (not fail startup) if a name-style entry appears to not be in the OID map at startup | At startup, the OID map may not yet be loaded (watcher loads it asynchronously). A hard validation failure here would cause a startup race condition. The existing pattern is to accept config values and let them fail silently at runtime if not resolvable. |

**No new packages. No new types.** Resolution via `IOidMapService` (existing interface with new member added in feature 2).

---

### 4. Command Map Service

**What it needs:** A new `ICommandMapService` / `CommandMapService` pair that holds a `FrozenDictionary<string, string>` mapping human name to SNMP command OID (e.g., `"obp_bypass_enable"` -> `"1.3.6.1.4.1.47477.10.21.60.5.0"`). Loaded from `commandmap.json` in the `simetra-commandmap` ConfigMap.

| Decision | Type | Why |
|----------|------|-----|
| Internal structure | `volatile FrozenDictionary<string, string>` | Identical to `OidMapService._map`. Same volatile-swap pattern. O(1) reads. |
| Interface shape | `IOidMapService` is the template: `string Resolve(string name)` + `void UpdateMap(Dictionary<string, string>)` + `int EntryCount` | Symmetry is intentional. Both services map strings to strings. Same consumers, same watcher pattern. A reader familiar with `IOidMapService` understands `ICommandMapService` immediately. |
| Singleton registration | `services.AddSingleton<ICommandMapService, CommandMapService>()` | Same as `IOidMapService`. Single instance, hot-reload via watcher. |
| Fallback value | `CommandMapService.Unknown = "Unknown"` constant, same as `OidMapService.Unknown` | Consistent sentinel. Callers check for `Unknown` before issuing SNMP SET. |
| Config JSON format | Same flat `{ "name": "oid" }` object format as `oidmaps.json` | Reuses the same `JsonSerializer.Deserialize<Dictionary<string,string>>()` call. Operators learn one format. |

**Implementation shape (mirrors `OidMapService` exactly):**
```csharp
public sealed class CommandMapService : ICommandMapService
{
    private volatile FrozenDictionary<string, string> _map;

    public string Resolve(string commandName) =>
        _map.TryGetValue(commandName, out var oid) ? oid : Unknown;

    public void UpdateMap(Dictionary<string, string> entries)
    {
        _map = entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        // diff logging same as OidMapService
    }
}
```

**No new packages.**

---

### 5. CommandMapWatcherService

**What it needs:** A `BackgroundService` that watches `simetra-commandmap` ConfigMap key `commandmap.json` and calls `ICommandMapService.UpdateMap()` on change.

| Decision | Type | Why |
|----------|------|-----|
| Base class | `BackgroundService` | Same as `OidMapWatcherService`, `DeviceWatcherService`, `TenantVectorWatcherService`. |
| Watch loop | K8s `WatchAsync` over `simetra-commandmap`, `fieldSelector: metadata.name=simetra-commandmap` | Line-for-line copy of `OidMapWatcherService`. Same watch API, same reconnect logic, same 5s backoff on disconnect. |
| Reload serialization | `SemaphoreSlim(1, 1)` `_reloadLock` | Same as all other watchers. Prevents race if two ConfigMap events arrive rapidly. |
| JSON options | `JsonSerializerOptions` with `ReadCommentHandling.Skip` and `AllowTrailingCommas = true` | Copy from `OidMapWatcherService`. Operators can add comments to JSON. |
| Namespace detection | `ReadNamespace()` static method (reads `/var/run/secrets/kubernetes.io/serviceaccount/namespace`, falls back to `"simetra"`) | Copy from existing watchers. Local dev works without K8s. |
| ConfigMap name constant | `internal const string ConfigMapName = "simetra-commandmap"` | Follows the `OidMapWatcherService.ConfigMapName = "simetra-oidmaps"` convention. |

**No new packages.**

---

## What NOT to Add (and Why)

| Rejected Option | Why Not |
|-----------------|---------|
| **FluentValidation** | `IValidateOptions<T>` + manual `List<string>` failures is the established project pattern (`DevicesOptionsValidator`, `LeaseOptionsValidator`, etc.). Adding FluentValidation would create two validation styles in the same codebase. |
| **Any regex library for OID string validation** | OID format validation is a single `Regex` or character-scan in BCL. No library needed. And OID validation is not in scope for this milestone -- the existing codebase accepts OID strings as opaque keys. |
| **A new OID-name format (struct/record)** | Keeping `Oids` as `List<string>` and resolving at dispatch time is simpler and backward-compatible. A new discriminated union type would require JSON converter changes, breaking existing `devices.json` files. |
| **Abstract base class for watcher services** | The three existing watcher services share 70-80% structure but differ in their target service type (different interfaces) and config shape (different deserialization types). An abstract base with generics would add complexity for marginal gain. The copy-then-customize pattern is established and readable. |
| **MessagePipe or similar pub/sub for command dispatch** | Command lookup is a synchronous dictionary read. No event bus needed until there is a consumer that dispatches commands in response to events -- which is not in scope for this milestone. |
| **IMemoryCache or IDistributedCache** | The command map is a small, rarely-changed lookup table. Volatile FrozenDictionary is faster, simpler, and has no TTL expiry to manage. |
| **Any package upgrade** | All existing package versions satisfy the new features. Upgrades require regression testing with no functional benefit for this milestone. |

---

## Existing Dependencies (Unchanged)

| Package | Version | Role in This Milestone |
|---------|---------|----------------------|
| `KubernetesClient` | 18.0.13 | `CommandMapWatcherService` uses same K8s watch API as `OidMapWatcherService` |
| `Microsoft.Extensions.Hosting` | 9.0.0 | `BackgroundService` base for `CommandMapWatcherService` |
| `Microsoft.Extensions.Options.DataAnnotations` | 9.0.0 | `IValidateOptions<T>` for any new config options class |
| `Lextm.SharpSnmpLib` | 12.5.7 | Command map OIDs passed to `ISnmpClient` for SNMP SET (future) -- no change to this lib |

**No changes to `.csproj`.** No new `<PackageReference>` entries.

---

## Pattern Reuse Map

| Pattern | Existing Example | New Application |
|---------|-----------------|-----------------|
| Volatile FrozenDictionary atomic swap | `OidMapService._map` | `CommandMapService._map`, `OidMapService._reverseMap` (new field) |
| `IOidMapService` interface shape | `IOidMapService` | `ICommandMapService` (identical shape, different domain) |
| K8s ConfigMap watcher + SemaphoreSlim | `OidMapWatcherService` | `CommandMapWatcherService` (structural copy) |
| `List<string>` failure collector in validator | `DevicesOptionsValidator` | `OidMapOptionsValidator` (if OID map validator added to startup) |
| Duplicate key detection with `HashSet<T>` | `DeviceWatcherService` IP+Port uniqueness | `OidMapService.UpdateMap()` OID key uniqueness check |
| Warning-not-fatal for hot-reload issues | `OidMapWatcherService` `LogWarning` on delete event | Duplicate OID/name detection in `UpdateMap()` |

---

## Integration Points with Existing Stack

**`OidMapService` changes (additive, no breaking changes):**
- New `volatile FrozenDictionary<string, string> _reverseMap` field
- New `TryResolveOid(string name, out string oid)` method on `IOidMapService`
- Duplicate detection logic inserted at the top of `UpdateMap()` before `BuildFrozenMap()`
- All existing callers of `Resolve(string oid)` and `UpdateMap()` are unaffected

**`MetricPollJob` changes (additive):**
- Before building the OID list for the SNMP GET, resolve each entry in `poll.Oids` via `IOidMapService.TryResolveOid()`, falling back to treating the entry as a literal OID string if resolution fails
- Requires injecting `IOidMapService` into `MetricPollJob` (or wherever SNMP GETs are assembled)

**New files (following existing structure):**
- `src/SnmpCollector/Pipeline/ICommandMapService.cs`
- `src/SnmpCollector/Pipeline/CommandMapService.cs`
- `src/SnmpCollector/Services/CommandMapWatcherService.cs`

No new directories. Follows existing placement: pipeline abstractions in `Pipeline/`, watcher services in `Services/`.

---

## Confidence Assessment

| Area | Confidence | Reason |
|------|------------|--------|
| FrozenDictionary for reverse index | HIGH | Used in 3+ locations. BCL since .NET 8. Exact same swap pattern in `OidMapService`. |
| Duplicate detection with HashSet | HIGH | Standard BCL pattern. No library risk. |
| CommandMapService shape | HIGH | Structural mirror of `OidMapService`, which is already proven and tested. |
| CommandMapWatcherService | HIGH | Structural copy of `OidMapWatcherService`. Same K8s API calls, same error handling. |
| Name-resolution in poll job | MEDIUM | The resolution fallback logic (name-or-literal) is simple, but the exact injection point in `MetricPollJob` needs to be confirmed against the current job implementation to ensure `IOidMapService` is accessible. |
| No new packages needed | HIGH | All types in BCL or existing dependencies. Verified against `.csproj`. |

---

## Sources

- Codebase: `src/SnmpCollector/Pipeline/OidMapService.cs` -- volatile FrozenDictionary swap + `UpdateMap()` pattern
- Codebase: `src/SnmpCollector/Pipeline/IOidMapService.cs` -- interface shape to mirror for `ICommandMapService`
- Codebase: `src/SnmpCollector/Services/OidMapWatcherService.cs` -- watcher structural template
- Codebase: `src/SnmpCollector/Services/DeviceWatcherService.cs` -- watcher structural template (devices variant)
- Codebase: `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` -- IValidateOptions pattern with List<string> failure collector
- Codebase: `src/SnmpCollector/SnmpCollector.csproj` -- confirmed current package versions, no new packages needed
- .NET 9 BCL: `System.Collections.Frozen.FrozenDictionary` -- available since .NET 8, unchanged in .NET 9

---
*Stack research for: v1.6 Organization and Command Map Foundation*
*Researched: 2026-03-13*
