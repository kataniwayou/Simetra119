# Phase 32: Command Map Infrastructure - Research

**Researched:** 2026-03-13
**Domain:** In-process lookup table with K8s ConfigMap hot-reload (mirrors OidMap pattern)
**Confidence:** HIGH

## Summary

Phase 32 is a structural mirror of the existing OidMap infrastructure (Phase 30). The codebase already has a fully working, battle-tested pattern: `OidMapService` + `IOidMapService` + `OidMapWatcherService`. Phase 32 replicates this pattern verbatim, substituting `CommandName` for `MetricName` and adding two additional interface members (`GetAllCommandNames()`, `Contains()`). There is no new library work, no architectural decisions, and no unknowns. Everything is answered by reading the existing sources.

The primary research task was to extract the exact implementation details of the OidMap pattern so that the planner can produce precise, copy-ready task instructions. This research documents those details with zero ambiguity.

One key difference from OidMap: `CommandMapService` has no heartbeat seed. `OidMapService.MergeWithHeartbeatSeed()` injects the heartbeat OID at construction and on every `UpdateMap` call. `CommandMapService` must not do this — it starts empty, and an empty map is an explicit valid state (CMD-04 requirement). The planner must be aware this method does not port over.

**Primary recommendation:** Copy the OidMap pattern exactly. Any deviation from the established pattern creates inconsistency with no benefit.

## Standard Stack

No new libraries. Phase 32 uses exclusively what is already in the project.

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Collections.Frozen` | .NET 8 BCL | `FrozenDictionary` for lock-free reads after atomic swap | Already used by OidMapService |
| `k8s` (KubernetesClient) | 18.x | `IKubernetes`, `WatchAsync`, `V1ConfigMap` | Already used by OidMapWatcherService |
| `System.Text.Json` | .NET 8 BCL | `JsonDocument` for 3-pass validation parsing | Already used by OidMapWatcherService |
| `Microsoft.Extensions.Logging` | .NET 8 BCL | Structured logging | Project-wide standard |
| `Microsoft.Extensions.Hosting` | .NET 8 BCL | `BackgroundService` base class | Already used by OidMapWatcherService |

### Alternatives Considered
None. The pattern is established. Deviating would create inconsistency.

**Installation:** None required — all dependencies already present.

## Architecture Patterns

### Recommended Project Structure

New files follow the exact same locations as the OidMap equivalents:

```
src/SnmpCollector/
├── Pipeline/
│   ├── ICommandMapService.cs         # new (mirrors IOidMapService.cs)
│   └── CommandMapService.cs          # new (mirrors OidMapService.cs)
├── Services/
│   └── CommandMapWatcherService.cs   # new (mirrors OidMapWatcherService.cs)
└── config/
    └── commandmaps.json              # new (mirrors oidmaps.json)

deploy/k8s/
├── snmp-collector/
│   └── simetra-commandmaps.yaml      # new (mirrors simetra-oidmaps.yaml)
└── production/
    └── configmap.yaml                # add --- simetra-commandmaps section

tests/SnmpCollector.Tests/
├── Pipeline/
│   └── CommandMapServiceTests.cs     # new (mirrors OidMapServiceTests.cs)
└── Services/
    └── CommandMapWatcherValidationTests.cs  # new (mirrors OidMapWatcherValidationTests.cs)
```

### Pattern 1: Volatile FrozenDictionary Swap (CommandMapService)

**What:** Three volatile fields — forward map (OID → name), reverse map (name → OID), and a `FrozenSet<string>` for `Contains()`. All three are replaced atomically on `UpdateMap`.

**Source:** `OidMapService.cs` (lines 21-23, 64-98) — read directly from codebase.

```csharp
// Source: src/SnmpCollector/Pipeline/OidMapService.cs
public sealed class CommandMapService : ICommandMapService
{
    private readonly ILogger<CommandMapService> _logger;
    private volatile FrozenDictionary<string, string> _forwardMap;   // OID → CommandName
    private volatile FrozenDictionary<string, string> _reverseMap;   // CommandName → OID
    private volatile FrozenSet<string> _commandNames;

    public CommandMapService(
        Dictionary<string, string> initialEntries,
        ILogger<CommandMapService> logger)
    {
        _logger = logger;
        _forwardMap = initialEntries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _commandNames = _forwardMap.Values.ToFrozenSet(StringComparer.Ordinal);
        _reverseMap = BuildReverseMap(_forwardMap);

        _logger.LogInformation("CommandMapService initialized with {EntryCount} entries", _forwardMap.Count);
    }

    // NOTE: NO heartbeat seed — CommandMapService starts empty, empty is valid.
    // This is the key difference from OidMapService.MergeWithHeartbeatSeed().
}
```

**Key difference from OidMapService:** No `MergeWithHeartbeatSeed()`. The OidMapService always injects `HeartbeatJobOptions.HeartbeatOid → "Heartbeat"` at construction and on every reload. CommandMapService must NOT do this. Empty map is valid.

**Impact on EntryCount:** OidMapServiceTests asserts `EntryCount = supplied entries + 1` (heartbeat seed). CommandMapServiceTests must assert `EntryCount = supplied entries` exactly.

### Pattern 2: Interface Shape (ICommandMapService)

**Source:** `IOidMapService.cs` read directly. The command map interface adds `GetAllCommandNames()` and `Contains()` that the OidMap interface does not have. Full interface:

```csharp
public interface ICommandMapService
{
    // Forward: OID → CommandName (mirrors IOidMapService.Resolve)
    string? ResolveCommandName(string oid);

    // Reverse: CommandName → OID (mirrors IOidMapService.ResolveToOid)
    string? ResolveCommandOid(string commandName);

    // List all names (new — not on IOidMapService)
    IReadOnlyCollection<string> GetAllCommandNames();

    // Check membership (new — not on IOidMapService)
    bool Contains(string commandName);

    // Entry count (mirrors IOidMapService.EntryCount)
    int Count { get; }

    // Atomic reload (mirrors IOidMapService.UpdateMap)
    void UpdateMap(Dictionary<string, string> entries);
}
```

**Note on naming:** `IOidMapService.Resolve()` returns `OidMapService.Unknown` for missing OIDs (never null). `ResolveCommandName()` should return `null` for missing OIDs — there is no "Unknown" command sentinel value in the spec. Similarly `ResolveCommandOid()` returns `null` (same as `IOidMapService.ResolveToOid`). The property is named `Count` (not `EntryCount`) per the CONTEXT.md decisions.

**Note on `ContainsMetricName` vs `Contains`:** `IOidMapService.ContainsMetricName(string metricName)` checks if a value exists. The parallel here is `ICommandMapService.Contains(string commandName)` — same semantics, different name per CONTEXT.md.

### Pattern 3: 3-Pass Validation in CommandMapWatcherService

**Source:** `OidMapWatcherService.ValidateAndParseOidMap()` (lines 196-328) — read directly.

The method is `internal static` and uses `JsonDocument` with three passes:
- Pass 1: Enumerate array, collect raw entries, detect duplicate OID keys
- Pass 2: Count command name frequencies, find duplicates
- Pass 3: Build clean dictionary excluding all entries with duplicate OIDs or duplicate names

For CommandMapWatcherService, the field names change but the algorithm is identical:
- Read `"Oid"` property (same field name as oidmaps)
- Read `"CommandName"` property (instead of `"MetricName"`)
- Log warnings use "CommandMap" prefix instead of "OidMap"

The method signature:
```csharp
// Source: mirrors OidMapWatcherService.ValidateAndParseOidMap()
internal static Dictionary<string, string>? ValidateAndParseCommandMap(string jsonContent, ILogger logger)
```

### Pattern 4: BackgroundService Watch Loop (CommandMapWatcherService)

**Source:** `OidMapWatcherService.ExecuteAsync()` (lines 55-133) — read directly.

The exact same structure:
1. Initial load via `LoadFromConfigMapAsync()` before watch loop starts
2. `while (!stoppingToken.IsCancellationRequested)` outer loop for reconnect
3. `ListNamespacedConfigMapWithHttpMessagesAsync` with `fieldSelector: $"metadata.name={ConfigMapName}"`
4. `WatchAsync<V1ConfigMap, V1ConfigMapList>` with `#pragma warning disable/restore CS0618`
5. Handle `Added`/`Modified` events; on `Deleted` log warning and retain current map
6. Reconnect on normal close (server-side ~30min timeout)
7. 5-second delay on unexpected disconnects
8. `_reloadLock` (`SemaphoreSlim(1,1)`) serializes concurrent reload requests

Constants to define:
```csharp
internal const string ConfigMapName = "simetra-commandmaps";  // locked in STATE.md
internal const string ConfigKey = "commandmaps.json";
```

### Pattern 5: DI Registration in ServiceCollectionExtensions

**Source:** `ServiceCollectionExtensions.cs` lines 239-241 (OidMapWatcherService registration) and lines 319-321 (OidMapService registration) — read directly.

In `AddSnmpConfiguration`, inside the `if (KubernetesClientConfiguration.IsInCluster())` block, add after the existing watcher registrations:

```csharp
// Phase 32: Command map watcher — parallel to OidMapWatcherService
services.AddSingleton<CommandMapWatcherService>();
services.AddHostedService(sp => sp.GetRequiredService<CommandMapWatcherService>());
```

In the pipeline singletons section, after OidMapService:
```csharp
// Phase 32: CommandMapService — initial empty map, populated by watcher in K8s or Program.cs in local dev
services.AddSingleton<CommandMapService>(sp =>
    new CommandMapService(new Dictionary<string, string>(), sp.GetRequiredService<ILogger<CommandMapService>>()));
services.AddSingleton<ICommandMapService>(sp => sp.GetRequiredService<CommandMapService>());
```

**Critical:** Use the same two-step singleton registration (concrete type first, then interface resolved from concrete). This ensures K8s watcher and local dev code both update the same instance.

### Pattern 6: Local Dev Fallback in Program.cs

**Source:** `Program.cs` lines 67-88 (OidMap local dev loading) — read directly.

Inside the `if (!KubernetesClientConfiguration.IsInCluster())` block, add after the OidMap loading block:

```csharp
// Load command map from commandmaps.json (array-of-objects format)
var commandmapsPath = Path.Combine(configDir, "commandmaps.json");
if (File.Exists(commandmapsPath))
{
    var cmdJson = File.ReadAllText(commandmapsPath);
    var cmdMapLogger = app.Services.GetRequiredService<ILogger<CommandMapWatcherService>>();
    var cmdMap = CommandMapWatcherService.ValidateAndParseCommandMap(cmdJson, cmdMapLogger);
    if (cmdMap != null)
    {
        var commandMapService = app.Services.GetRequiredService<CommandMapService>();
        commandMapService.UpdateMap(cmdMap);
    }
}
```

**Note:** If `commandmaps.json` does not exist in `config/`, that is fine — empty map is valid. The `File.Exists` guard handles this silently.

### Pattern 7: K8s ConfigMap YAML Files

The simetra-commandmaps ConfigMap mirrors simetra-oidmaps exactly in structure:

**`deploy/k8s/snmp-collector/simetra-commandmaps.yaml`** (standalone file):
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-commandmaps
  namespace: simetra
data:
  commandmaps.json: |
    [
      { "Oid": "1.3.6.1.4.1.47477.10.21.1.4.1.0", "CommandName": "obp_set_bypass_L1" },
      ...
    ]
```

**`deploy/k8s/production/configmap.yaml`**: Add a new `---` section at the end with the same ConfigMap. This mirrors how both simetra-oidmaps and simetra-devices appear both in standalone YAML files and in the production configmap.yaml.

**RBAC:** No changes needed. `rbac.yaml` already grants `get`, `list`, `watch` on all `configmaps` resources for the `simetra-role`. The new `simetra-commandmaps` ConfigMap is automatically covered.

### Anti-Patterns to Avoid

- **Heartbeat seeding:** Do not add a `MergeWithHeartbeatSeed()` call. CommandMapService starts empty and must not inject any synthetic entries. The test for `EntryCount` must reflect 0 for an empty constructor call (unlike OidMapServiceTests which asserts 1 for an empty constructor call due to heartbeat).
- **Shared validation logic:** Do not extract a generic shared parser between OidMapWatcherService and CommandMapWatcherService. CONTEXT.md explicitly decided "own copy of validation logic." Keep them independent.
- **Single-instance registration mistake:** Do not use `AddSingleton<ICommandMapService, CommandMapService>()` and `AddHostedService<CommandMapWatcherService>()` as separate registrations — this creates two instances. Use the explicit `sp.GetRequiredService<CommandMapService>()` pattern shown above (same Pitfall documented in ServiceCollectionExtensions.cs line 231-236 comment).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe reads without locks | Custom reader-writer lock | `volatile` + `FrozenDictionary` | Already proven in OidMapService; FrozenDictionary is read-only after creation |
| K8s ConfigMap watching | Manual polling | `WatchAsync` with reconnect loop | Already proven in OidMapWatcherService; handles server-side timeouts |
| Duplicate key detection | Custom logic | 3-pass `JsonDocument` traversal | Already proven in OidMapWatcherService; handles both OID and name duplication |

**Key insight:** Every "how do I implement this?" question is answered by reading the existing OidMap code. There is nothing to invent.

## Common Pitfalls

### Pitfall 1: Accidentally Including Heartbeat Seed
**What goes wrong:** Developer copies OidMapService verbatim and keeps `MergeWithHeartbeatSeed()`. CommandMapService now always contains an entry for the heartbeat OID mapped to "Heartbeat" as a command name, which is nonsensical.
**Why it happens:** Direct copy-paste without reading the "no heartbeat seed" requirement.
**How to avoid:** Delete `MergeWithHeartbeatSeed()` and any calls to it. The constructor body assigns `_forwardMap` directly from `initialEntries` without merging.
**Warning signs:** `commandMapService.Count` returns 1 when constructed with empty dictionary.

### Pitfall 2: DI Double-Instance Registration
**What goes wrong:** Two separate `CommandMapService` instances exist in the container. `CommandMapWatcherService` updates one; callers resolve the other. The command map never appears populated.
**Why it happens:** Using `services.AddSingleton<ICommandMapService, CommandMapService>()` instead of the two-step pattern.
**How to avoid:** Register concrete type first, then resolve interface from concrete: `services.AddSingleton<CommandMapService>(...)` then `services.AddSingleton<ICommandMapService>(sp => sp.GetRequiredService<CommandMapService>())`.
**Warning signs:** `ICommandMapService.Count` always returns 0 in K8s mode even after watcher fires.

### Pitfall 3: Forgetting Local Dev Fallback
**What goes wrong:** Program.cs loads OidMap and devices from `config/` in local dev mode, but does not load commandmaps.json. Command map is always empty in local dev.
**Why it happens:** Adding the DI registration but missing the Program.cs addition.
**How to avoid:** The local dev block in Program.cs must mirror the OidMap loading block exactly — read `commandmaps.json`, call `ValidateAndParseCommandMap`, call `UpdateMap`.
**Warning signs:** `ICommandMapService.Count` returns 0 in local dev mode, 12 in K8s mode.

### Pitfall 4: Wrong Field Name in Parser
**What goes wrong:** `ValidateAndParseCommandMap` reads `"MetricName"` property instead of `"CommandName"`. All 12 seed entries produce "missing CommandName" warnings and the map is empty.
**Why it happens:** Copying the OidMap parser without changing the property name.
**How to avoid:** In the `foreach` loop, change `element.TryGetProperty("MetricName", ...)` to `element.TryGetProperty("CommandName", ...)`. The `"Oid"` property name stays the same.
**Warning signs:** Parser logs 12 "missing CommandName" warnings on startup; `Count` is 0.

### Pitfall 5: ResolveCommandName Returns "Unknown" Instead of null
**What goes wrong:** Developer copies `OidMapService.Resolve()` which returns `OidMapService.Unknown` for missing OIDs. `ResolveCommandName` returns "Unknown" string instead of `null`.
**Why it happens:** OidMapService has a deliberate fallback value for Grafana label compatibility. CommandMapService has no such requirement.
**How to avoid:** `ResolveCommandName` returns `null` for missing OIDs (same as `ResolveToOid` on OidMapService). Return type is `string?`, not `string`.
**Warning signs:** Test `ResolveCommandName_UnknownOid_ReturnsNull` fails; returns "Unknown" instead.

### Pitfall 6: Missing simetra-commandmaps Section in production/configmap.yaml
**What goes wrong:** K8s cluster uses `deploy/k8s/production/configmap.yaml` as the production deployment manifest, but the command map ConfigMap is only in the standalone file. Applying `kubectl apply -f production/configmap.yaml` does not create `simetra-commandmaps`.
**Why it happens:** Only creating the standalone file without the production configmap.yaml addition.
**How to avoid:** Add a `---` separator and the full `simetra-commandmaps` ConfigMap block to `production/configmap.yaml`. Check how `simetra-oidmaps` appears in that file (lines 112-226) for the exact format.
**Warning signs:** `kubectl get configmap simetra-commandmaps -n simetra` returns "not found" after applying the production manifest.

## Code Examples

Verified patterns from codebase (HIGH confidence — read directly):

### commandmaps.json Local Dev File
```json
[
  // OBP bypass per link -- control subtree .4.1.0
  { "Oid": "1.3.6.1.4.1.47477.10.21.1.4.1.0", "CommandName": "obp_set_bypass_L1" },
  { "Oid": "1.3.6.1.4.1.47477.10.21.2.4.1.0", "CommandName": "obp_set_bypass_L2" },
  { "Oid": "1.3.6.1.4.1.47477.10.21.3.4.1.0", "CommandName": "obp_set_bypass_L3" },
  { "Oid": "1.3.6.1.4.1.47477.10.21.4.4.1.0", "CommandName": "obp_set_bypass_L4" },
  // NPB counter reset per port -- control subtree .3.{port}.1.0
  { "Oid": "1.3.6.1.4.1.47477.100.3.1.1.0", "CommandName": "npb_reset_counters_P1" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.2.1.0", "CommandName": "npb_reset_counters_P2" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.3.1.0", "CommandName": "npb_reset_counters_P3" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.4.1.0", "CommandName": "npb_reset_counters_P4" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.5.1.0", "CommandName": "npb_reset_counters_P5" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.6.1.0", "CommandName": "npb_reset_counters_P6" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.7.1.0", "CommandName": "npb_reset_counters_P7" },
  { "Oid": "1.3.6.1.4.1.47477.100.3.8.1.0", "CommandName": "npb_reset_counters_P8" }
]
```

Note: Comments (`//`) are valid — `JsonDocumentOptions.CommentHandling = JsonCommentHandling.Skip` is already set in the parser.

### UpdateMap Diff Logging Pattern
```csharp
// Source: OidMapService.cs lines 65-97 -- exact pattern to replicate
var oldMap = _forwardMap;
var newMap = entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

var added = newMap.Keys.Except(oldMap.Keys).ToList();
var removed = oldMap.Keys.Except(newMap.Keys).ToList();
var changed = newMap.Keys
    .Intersect(oldMap.Keys)
    .Where(k => oldMap[k] != newMap[k])
    .ToList();

_forwardMap = newMap;
_commandNames = newMap.Values.ToFrozenSet(StringComparer.Ordinal);
_reverseMap = BuildReverseMap(newMap);

_logger.LogInformation(
    "CommandMap hot-reloaded: {EntryCount} entries total, +{Added} added, -{Removed} removed, ~{Changed} changed",
    newMap.Count, added.Count, removed.Count, changed.Count);
```

### FrozenDictionary Comparer Notes
- Forward map (OID keys): `StringComparer.OrdinalIgnoreCase` — same as OidMapService, OIDs are case-insensitive
- Reverse map (CommandName keys): `StringComparer.Ordinal` — command names are case-sensitive (same as OidMapService reverse map)
- FrozenSet (for Contains): `StringComparer.Ordinal` — case-sensitive membership check

### Namespace Conventions
```csharp
// ICommandMapService.cs and CommandMapService.cs
namespace SnmpCollector.Pipeline;

// CommandMapWatcherService.cs
namespace SnmpCollector.Services;
```

### Test Structure for CommandMapServiceTests
```csharp
// Source: OidMapServiceTests.cs structure -- direct mirror
private static CommandMapService CreateService(Dictionary<string, string> entries)
    => new CommandMapService(entries, NullLogger<CommandMapService>.Instance);

[Fact]
public void Count_EmptyMap_ReturnsZero()
{
    // KEY DIFFERENCE FROM OidMapServiceTests:
    // OidMapService seeds heartbeat, so empty constructor = EntryCount 1.
    // CommandMapService has no seed, so empty constructor = Count 0.
    var sut = CreateService(new Dictionary<string, string>());
    Assert.Equal(0, sut.Count);
}
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| `IOptionsMonitor<OidMapOptions>` (config-section binding) | Array-of-objects JSON via `JsonDocument` parsed by watcher service | This project already moved to the newer pattern in Phase 30; Phase 32 follows Phase 30's approach, not the legacy options approach |

**Deprecated/outdated:**
- `OidMapOptions.cs` class: The project originally used IConfiguration section binding for the OID map. OidMapWatcherService now handles the JSON directly. There is no `CommandMapOptions.cs` equivalent to create — Phase 32 does not use IConfiguration section binding at all.

## Open Questions

None. All implementation details are resolved by reading the existing OidMap source code.

## Sources

### Primary (HIGH confidence)
All sources are the existing codebase, read directly:
- `src/SnmpCollector/Pipeline/IOidMapService.cs` — interface shape to mirror
- `src/SnmpCollector/Pipeline/OidMapService.cs` — service implementation to mirror
- `src/SnmpCollector/Services/OidMapWatcherService.cs` — watcher implementation to mirror; includes 3-pass validation, K8s watch loop, constants
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — DI registration pattern (lines 219-321)
- `src/SnmpCollector/Program.cs` — local dev fallback pattern (lines 67-88)
- `src/SnmpCollector/config/oidmaps.json` — data file format to mirror
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` — standalone ConfigMap YAML to mirror
- `deploy/k8s/production/configmap.yaml` — production configmap to add section to
- `deploy/k8s/rbac.yaml` — confirms no RBAC changes needed
- `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs` — test structure to mirror
- `tests/SnmpCollector.Tests/Services/OidMapWatcherValidationTests.cs` — validation test structure to mirror

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in project
- Architecture: HIGH — read directly from existing production code
- Pitfalls: HIGH — derived from actual code behavior observed in sources
- Test structure: HIGH — read directly from existing test files

**Research date:** 2026-03-13
**Valid until:** This research has no expiry — it describes the existing codebase, not external ecosystem state. Valid until the OidMap source files are modified.
