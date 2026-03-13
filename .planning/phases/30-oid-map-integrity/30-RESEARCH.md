# Phase 30: OID Map Integrity - Research

**Researched:** 2026-03-13
**Domain:** C# .NET 9 — FrozenDictionary atomic swap, JsonDocument duplicate-key detection, reverse index
**Confidence:** HIGH

## Summary

Phase 30 is a focused modification to three existing files: `OidMapService`, `IOidMapService`, and `OidMapWatcherService`. No new services, no new dependencies. The primary technical challenges are: (1) detecting duplicate OID keys before JSON deserialization silently drops them, (2) detecting duplicate metric name values across keys, and (3) adding a reverse index (`FrozenDictionary<string, string>`) atomically alongside the existing forward map.

All decisions are locked in CONTEXT.md. The codebase already uses `System.Collections.Frozen.FrozenDictionary`, `System.Text.Json.JsonDocument`, `volatile` atomic swaps, and `ILogger` structured logging. No new NuGet packages are needed. The validation logic lives in `OidMapWatcherService.HandleConfigMapChangedAsync` (before the `_oidMapService.UpdateMap(oidMap)` call), and `OidMapService.UpdateMap` / constructor receive only pre-validated clean entries.

**Primary recommendation:** Use `JsonDocument` in `OidMapWatcherService` to enumerate properties manually, detecting duplicate OID keys before building the `Dictionary<string, string>`. Detect duplicate metric name values in a second pass with a counting dictionary. Strip both entries of any conflict. Then call `UpdateMap` with clean entries. In `OidMapService`, add a `volatile FrozenDictionary<string, string> _reverseMap` swapped atomically alongside `_map`, and add `ResolveToOid` to both `OidMapService` and `IOidMapService`.

## Standard Stack

### Core (already in use — no additions needed)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Collections.Frozen` | .NET 9 BCL | Immutable high-performance `FrozenDictionary` and `FrozenSet` for atomic volatile swap | Already used for `_map` and `_metricNames` |
| `System.Text.Json` | .NET 9 BCL | JSON parsing; `JsonDocument` allows manual property enumeration to detect duplicate keys | Already used in watcher and `Program.cs` |
| `Microsoft.Extensions.Logging.Abstractions` | 9.0.0 | `ILogger<T>` structured logging | Already used everywhere |
| xunit + NSubstitute | 2.9.3 / 5.3.0 | Unit test framework and mocking | Already used in test project |

### No New Packages

This phase adds zero NuGet packages. All required APIs are in the .NET 9 BCL or already-referenced packages.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `JsonDocument` for duplicate detection | `JsonSerializer.Deserialize<Dictionary<string,string>>` | `Dictionary` silently drops later duplicates — operator sees no warning. `JsonDocument` is required for explicit enumeration. |
| Reverse `FrozenDictionary` | LINQ `FirstOrDefault` scan over `_map.Values` | O(n) per lookup vs O(1). FrozenDictionary is the correct choice. |
| Custom validation service | In-place validation in `OidMapWatcherService` | Decision is locked: no new services. |

## Architecture Patterns

### File Modification Map

```
OidMapWatcherService.cs
  HandleConfigMapChangedAsync()
    [ADD] duplicate-OID detection via JsonDocument
    [ADD] duplicate-name detection via counting pass
    [ADD] ERROR log if all entries stripped
    [EXISTING] _oidMapService.UpdateMap(cleanEntries)

OidMapService.cs
  [ADD] volatile FrozenDictionary<string,string> _reverseMap
  constructor
    [CHANGE] pass entries through MergeWithHeartbeatSeed, then BuildFrozenMap AND BuildReverseMap
  UpdateMap()
    [CHANGE] also build and atomically swap _reverseMap
  [ADD] ResolveToOid(string metricName)

IOidMapService.cs
  [ADD] string? ResolveToOid(string metricName)
```

### Pattern 1: JsonDocument Duplicate-Key Detection

**What:** Enumerate all properties of the root JSON object manually via `JsonDocument`. Track seen keys; when a key is encountered a second time, add the key to a `HashSet<string> duplicateOids`. After enumeration, build the result dictionary excluding all keys in `duplicateOids`.

**When to use:** Any time `JsonSerializer.Deserialize<Dictionary<string,string>>` would silently win/lose on duplicate keys.

**Example:**

```csharp
// Source: System.Text.Json.JsonDocument — .NET 9 BCL (verified via codebase usage in Program.cs)
using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
{
    CommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
});

var root = doc.RootElement;
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var duplicateOids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var rawEntries = new List<(string oid, string name)>();

foreach (var property in root.EnumerateObject())
{
    var oid = property.Name;
    if (!seen.Add(oid))
        duplicateOids.Add(oid);
    else
        rawEntries.Add((oid, property.Value.GetString() ?? string.Empty));
}
```

### Pattern 2: Duplicate Metric Name Detection (Second Pass)

**What:** After building `rawEntries`, count occurrences of each metric name value. Names with count > 1 are duplicates.

**Example:**

```csharp
// Count metric name occurrences
var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var (_, name) in rawEntries)
    nameCounts[name] = nameCounts.TryGetValue(name, out var c) ? c + 1 : 1;

var duplicateNames = nameCounts
    .Where(kv => kv.Value > 1)
    .Select(kv => kv.Key)
    .ToHashSet(StringComparer.Ordinal);
```

### Pattern 3: Filtering + Warning Logs

**What:** Build the clean dictionary excluding both categories of conflicts, emitting one structured warning per conflicting entry.

**Example:**

```csharp
var cleanEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

foreach (var (oid, name) in rawEntries)
{
    if (duplicateOids.Contains(oid))
    {
        _logger.LogWarning(
            "OidMap duplicate OID key skipped: {Oid} -> {MetricName}",
            oid, name);
        continue;
    }
    if (duplicateNames.Contains(name))
    {
        _logger.LogWarning(
            "OidMap duplicate metric name skipped: {Oid} -> {MetricName}",
            oid, name);
        continue;
    }
    cleanEntries[oid] = name;
}

if (cleanEntries.Count == 0)
    _logger.LogError(
        "OidMap validation produced empty map for {ConfigMap}/{Key} -- all entries were duplicates",
        ConfigMapName, ConfigKey);

// Heartbeat seed and UpdateMap happen in OidMapService as today
_oidMapService.UpdateMap(cleanEntries);
```

### Pattern 4: Atomic Reverse Index Swap in OidMapService

**What:** Add a second `volatile FrozenDictionary<string, string>` for name → OID. Build it from the same validated entries as the forward map. Swap both in the same assignment sequence (not truly atomic, but sufficient given the existing `volatile` memory model — callers tolerate a brief inconsistency window, same as today's `_map` + `_metricNames`).

**Example:**

```csharp
// In OidMapService
private volatile FrozenDictionary<string, string> _reverseMap =
    FrozenDictionary<string, string>.Empty;

private static FrozenDictionary<string, string> BuildReverseMap(
    FrozenDictionary<string, string> forwardMap)
{
    // forwardMap already excludes duplicates; every value is unique
    return forwardMap
        .Select(kv => new KeyValuePair<string, string>(kv.Value, kv.Key))
        .ToFrozenDictionary(StringComparer.Ordinal);
}

public string? ResolveToOid(string metricName)
{
    return _reverseMap.TryGetValue(metricName, out var oid) ? oid : null;
}
```

In `UpdateMap` and constructor, after `_map = newMap;`:

```csharp
_reverseMap = BuildReverseMap(newMap);
```

### Pattern 5: Interface Extension

```csharp
// IOidMapService.cs — add:
/// <summary>
/// Reverse-resolves a metric name to its OID.
/// Returns null if the metric name is not in the current map.
/// </summary>
string? ResolveToOid(string metricName);
```

### Anti-Patterns to Avoid

- **Calling `JsonSerializer.Deserialize<Dictionary<string,string>>` first then checking for duplicates:** Impossible — the dictionary silently drops later duplicates during deserialization. Must use `JsonDocument` to detect them.
- **Validating in `OidMapService.UpdateMap`:** Decision locked. Validation belongs in `OidMapWatcherService`, before `UpdateMap` is called. `UpdateMap` receives clean entries.
- **Modifying `MergeWithHeartbeatSeed` to perform validation:** That method is called inside `OidMapService` which already receives clean entries. Heartbeat seed is added after validation per locked decisions.
- **Building reverse map from `entries` parameter instead of `_map`:** The heartbeat seed is added by `MergeWithHeartbeatSeed` inside `OidMapService` — build the reverse map from the final `FrozenDictionary` so Heartbeat is included in the reverse index.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting duplicate JSON keys | Custom JSON parser | `JsonDocument.RootElement.EnumerateObject()` | Already in BCL, handles comments/trailing commas with `JsonDocumentOptions` |
| Immutable reverse lookup | `Dictionary<string,string>` locked with a mutex | `FrozenDictionary<string,string>` with `volatile` swap | Matches existing forward-map pattern; no lock contention |
| OID case normalization | Custom string normalization | `StringComparer.OrdinalIgnoreCase` in `Dictionary` constructors | Matches existing `MergeWithHeartbeatSeed` which uses `OrdinalIgnoreCase` |

**Key insight:** The codebase already solves the atomic-swap problem with `volatile FrozenDictionary` + reference assignment. Replicate the exact same pattern for the reverse map.

## Common Pitfalls

### Pitfall 1: Duplicate-OID Detection Using JsonDocument but Missing First Occurrence

**What goes wrong:** The first occurrence of a duplicate OID is added to `rawEntries` and never flagged. If operator writes OID "X" twice, only the second occurrence is in `duplicateOids` set. Both occurrences must be skipped.

**Why it happens:** Naive implementation adds first occurrence to result, then detects second.

**How to avoid:** Collect ALL raw entries in a list first (including first occurrence), record duplicate keys in a `HashSet`. Then filter: skip any entry whose OID is in `duplicateOids`. This correctly removes both the first and second occurrence.

**Warning signs:** Test "duplicate OID key — both omitted" passes for second but fails for first occurrence.

### Pitfall 2: Duplicate-Name Detection Using Wrong StringComparer

**What goes wrong:** Metric names `"obp_channel_L1"` and `"OBP_CHANNEL_L1"` treated as distinct.

**Why it happens:** Using `StringComparer.OrdinalIgnoreCase` instead of `StringComparer.Ordinal` for name counting, or vice versa — depending on whether names are case-sensitive.

**How to avoid:** Metric names in this codebase are all lowercase snake_case. Use `StringComparer.Ordinal` for name-duplicate detection to match exact case. The existing `ContainsMetricName` on `_metricNames` uses the default `FrozenSet` which is case-sensitive.

**Warning signs:** False positive duplicate warnings for names that are case-variants.

### Pitfall 3: Reverse Map Excludes Heartbeat Seed

**What goes wrong:** `ResolveToOid("Heartbeat")` returns null even though forward map resolves `HeartbeatOid` to "Heartbeat".

**Why it happens:** Reverse map built from `entries` parameter before `MergeWithHeartbeatSeed` runs.

**How to avoid:** Build reverse map from `newMap` (the `FrozenDictionary` after `MergeWithHeartbeatSeed`), not from the `entries` parameter.

**Warning signs:** Unit test `ResolveToOid("Heartbeat")` returns null.

### Pitfall 4: JsonDocumentOptions Mismatch with Existing Config

**What goes wrong:** `JsonDocument.Parse` rejects valid `oidmaps.json` that uses `//` comments and trailing commas.

**Why it happens:** `oidmaps.json` uses both (visible in the file). `JsonDocumentOptions.CommentHandling = JsonCommentHandling.Skip` and `AllowTrailingCommas = true` are required.

**How to avoid:** Pass `JsonDocumentOptions` matching the existing `JsonOptions` in `OidMapWatcherService` (`ReadCommentHandling = Skip`, `AllowTrailingCommas = true`).

**Warning signs:** `JsonException` thrown during parse of local dev `oidmaps.json`.

### Pitfall 5: Two Separate Volatile Writes Not Atomic

**What goes wrong:** A reader calls `Resolve()` on the new `_map` but `ResolveToOid()` on the old `_reverseMap` during the brief window between the two assignments in `UpdateMap`.

**Why it happens:** `_map = newMap; _reverseMap = BuildReverseMap(newMap);` — there is a window where map is new but reverse is old.

**How to avoid:** This is acceptable per the existing pattern — `_metricNames` already has the same window relative to `_map`. Both the forward and reverse map are eventually consistent across a single hot-reload. Document the window in a code comment. Phase 31 (which depends on this) should not rely on cross-map consistency within a single hot-reload cycle.

**Warning signs:** Attempting to use `Interlocked.Exchange` or locking — unnecessary complexity not matching project patterns.

## Code Examples

Verified patterns from official sources and codebase inspection:

### Current OidMapService Constructor (reference for reverse map integration)

```csharp
// Source: OidMapService.cs (verified)
public OidMapService(Dictionary<string, string> initialEntries, ILogger<OidMapService> logger)
{
    _logger = logger;
    var seeded = MergeWithHeartbeatSeed(initialEntries);
    _map = BuildFrozenMap(seeded);
    _metricNames = _map.Values.ToFrozenSet();
    // ADD: _reverseMap = BuildReverseMap(_map);
    _logger.LogInformation("OidMapService initialized with {EntryCount} entries", _map.Count);
}
```

### Current UpdateMap (reference for where to add reverse swap)

```csharp
// Source: OidMapService.cs (verified)
// Atomic swap -- volatile write ensures all readers see the new map immediately
_map = newMap;
_metricNames = newMap.Values.ToFrozenSet();
// ADD: _reverseMap = BuildReverseMap(newMap);
```

### FrozenDictionary.Empty for Initial State

```csharp
// Source: System.Collections.Frozen BCL (.NET 8+, verified in use)
private volatile FrozenDictionary<string, string> _reverseMap =
    FrozenDictionary<string, string>.Empty;
```

### Test Pattern: CreateService Factory

```csharp
// Source: OidMapServiceTests.cs (verified)
private static OidMapService CreateService(Dictionary<string, string> entries)
    => new OidMapService(entries, NullLogger<OidMapService>.Instance);
```

New tests for `ResolveToOid` should follow this exact pattern.

### Structured Warning Log Format (matching existing style)

```csharp
// Source: OidMapService.cs UpdateMap() pattern (verified)
_logger.LogWarning(
    "OidMap duplicate OID key skipped: {Oid} -> {MetricName}",
    oid, name);

_logger.LogWarning(
    "OidMap duplicate metric name skipped: {Oid} -> {MetricName}",
    oid, name);

_logger.LogError(
    "OidMap validation produced empty map for {ConfigMap}/{Key} -- all entries were duplicates. Map will be empty after reload.",
    ConfigMapName, ConfigKey);
```

No numeric EventId is used anywhere in this codebase — do not add one.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| N/A — new feature | `JsonDocument.EnumerateObject()` for duplicate detection | Phase 30 | Operator-visible errors surface at load time |
| N/A — new feature | `volatile FrozenDictionary` reverse index | Phase 30 | O(1) name-to-OID lookup for Phase 31 |

**Deprecated/outdated:**

- Nothing deprecated in this phase. The `JsonSerializer.Deserialize<Dictionary<string,string>>` in `HandleConfigMapChangedAsync` is REPLACED (not supplemented) by the `JsonDocument` path. The old deserialize call is removed.

## Open Questions

1. **OID key comparison: OrdinalIgnoreCase vs Ordinal for duplicate detection**
   - What we know: `MergeWithHeartbeatSeed` uses `StringComparer.OrdinalIgnoreCase` — meaning OID key lookup is case-insensitive.
   - What's unclear: Should duplicate OID detection also be case-insensitive? i.e., is `"1.3.6.1.4.1.47477.10.21.1.3.1.0"` a duplicate of `"1.3.6.1.4.1.47477.10.21.1.3.1.0"` (different case)?
   - Recommendation: Use `StringComparer.OrdinalIgnoreCase` for the `seen`/`duplicateOids` sets to match `MergeWithHeartbeatSeed` behavior. OIDs are dot-numeric so case variance is unlikely, but consistency matters.

2. **Property value type validation**
   - What we know: `oidmaps.json` values are all strings. `property.Value.GetString()` returns `null` for non-string JSON values.
   - What's unclear: Should a null/empty metric name value be skipped with a warning?
   - Recommendation: Skip entries where `property.Value.GetString()` returns null or empty string, with a structured warning. This prevents empty strings entering the map and the reverse index.

## Sources

### Primary (HIGH confidence)

- Codebase direct read: `OidMapService.cs` — forward map pattern, `MergeWithHeartbeatSeed`, `volatile FrozenDictionary`, `BuildFrozenMap`
- Codebase direct read: `IOidMapService.cs` — current interface contract
- Codebase direct read: `OidMapWatcherService.cs` — `HandleConfigMapChangedAsync`, `JsonOptions`, `SemaphoreSlim` serialization
- Codebase direct read: `OidMapServiceTests.cs` — `CreateService` test factory pattern, test naming conventions
- Codebase direct read: `SnmpCollector.csproj` — .NET 9, no additional JSON deps needed
- Codebase direct read: `SnmpCollector.Tests.csproj` — xunit 2.9.3, NSubstitute 5.3.0
- Codebase direct read: `oidmaps.json` — uses `//` comments and trailing commas; `JsonDocumentOptions` must match
- Codebase direct read: `HeartbeatJobOptions.cs` — `HeartbeatOid = "1.3.6.1.4.1.9999.1.1.1.0"`, heartbeat name `"Heartbeat"`
- Codebase direct read: `Program.cs` (line 113) — existing `JsonDocument.Parse` usage confirms pattern is established

### Secondary (MEDIUM confidence)

- None needed — all findings are from direct codebase reads.

### Tertiary (LOW confidence)

- None.

## Metadata

**Confidence breakdown:**

- Standard stack: HIGH — all APIs already in use in the codebase, no new deps
- Architecture: HIGH — based on direct reading of all three files to be modified
- Pitfalls: HIGH — derived from concrete codebase constraints (existing `OrdinalIgnoreCase`, heartbeat seed timing, `oidmaps.json` format with comments)

**Research date:** 2026-03-13
**Valid until:** 2026-04-13 (stable domain — .NET 9 BCL, no external dependencies)
