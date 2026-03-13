# Phase 31: Human-Name Device Config - Research

**Researched:** 2026-03-13
**Domain:** C# model rename, JSON schema migration, name-resolution logic, K8s ConfigMap updates
**Confidence:** HIGH (all findings from direct codebase inspection)

## Summary

Phase 31 is a mechanical rename plus schema restructuring across the codebase, with one new behavioral piece: name resolution in `DeviceWatcherService`. The rename (`MetricPollOptions` -> `PollOptions`, `Oids` -> `MetricNames`, `MetricPolls` -> `Polls`) touches 11 C# source files and 2 test files. The oidmaps.json schema change (flat dict -> array of objects) requires rewriting `OidMapWatcherService.ValidateAndParseOidMap` from `EnumerateObject()` to `EnumerateArray()`, rewriting all 9 validation tests, and updating both local and K8s oidmaps.json files. Local dev loading in `Program.cs` also parses oidmaps.json and must change.

Name resolution must be placed in `DeviceRegistry` (not only `DeviceWatcherService`) because `DeviceRegistry` is also constructed from `IOptions<DevicesOptions>` at startup and called from `Program.cs` in local dev mode — both paths bypass `DeviceWatcherService`. The C# model `PollOptions.MetricNames` holds human-readable names from config; `MetricPollInfo.Oids` holds resolved OID strings built at load time via `IOidMapService.ResolveToOid(metricName)` (Phase 30, returns null on miss). Unresolvable names log a warning and are excluded from the poll group's OIDs. `CardinalityAuditService` and `MetricPollJob` both access only the runtime `DeviceInfo.PollGroups` (`MetricPollInfo`) — confirmed by code inspection — so neither file requires any changes.

The E2E fixtures are the largest mechanical change: 6 YAML fixture files, 1 YAML fixture used inline via jq in scenario 06, plus the K8s `simetra-devices.yaml` and `simetra-oidmaps.yaml`. Additionally, `appsettings.Development.json` has `MetricPolls`/`Oids` and must be updated. The oidmaps.json K8s ConfigMap is missing 6 entries that local file has — those must be added as part of the oidmaps restructuring.

**Primary recommendation:** Implement in 5 tasks: (1) oidmaps.json schema change + OidMapWatcherService rewrite + validation test rewrite, (2) C# model rename, (3) name resolution in DeviceWatcherService, (4) devices.json/ConfigMap rewrites, (5) E2E fixture/scenario updates.

## Standard Stack

This phase uses only the existing stack — no new libraries.

### Core (existing, no changes to add)
| Component | Version | Purpose | Notes |
|-----------|---------|---------|-------|
| `System.Text.Json` | .NET 9 built-in | JSON parsing (JsonDocument.EnumerateArray) | Replaces EnumerateObject in ValidateAndParseOidMap |
| `IOidMapService.ResolveToOid` | Phase 30 | Reverse lookup: metric name -> OID | Already implemented, no changes needed |
| `IOptions<DevicesOptions>` | Microsoft.Extensions | Config binding | Rename `MetricPolls`->`Polls` in binding |

## Architecture Patterns

### Pattern 1: OidMap Array Parsing (replaces dict parsing)

The existing `ValidateAndParseOidMap` iterates `doc.RootElement.EnumerateObject()` treating JSON properties as OID keys. The new format is an array of objects with explicit `Oid` and `MetricName` fields.

**New array parsing approach:**
```csharp
// Source: direct codebase — OidMapWatcherService.ValidateAndParseOidMap
// Replace: foreach (var property in doc.RootElement.EnumerateObject())
// With:
foreach (var element in doc.RootElement.EnumerateArray())
{
    var oid = element.GetProperty("Oid").GetString();
    var name = element.GetProperty("MetricName").GetString();
    // same null/empty checks, same 3-pass duplicate logic
}
```

The 3-pass duplicate detection logic (OIDs, then names, then clean build) stays intact — only the source of `(oid, name)` pairs changes.

**New array format (both local oidmaps.json and K8s ConfigMap):**
```json
[
  { "Oid": "1.3.6.1.4.1.47477.10.21.1.3.1.0", "MetricName": "obp_link_state_L1" },
  { "Oid": "1.3.6.1.4.1.47477.10.21.1.3.4.0", "MetricName": "obp_channel_L1" }
]
```

**Note:** The local oidmaps.json currently uses JSON comments (`// ---- OBP OIDs ----`) which are stripped by `JsonCommentHandling.Skip`. JSON arrays do NOT support comments between elements in standard JSON — comments only work between object properties. However `JsonDocumentOptions.CommentHandling = Skip` handles C-style `//` comments in the token stream regardless of location, so comments between array elements work fine with System.Text.Json. Comments CAN be kept in the new format.

**Validation test rewrite:** All 9 tests in `OidMapWatcherValidationTests.cs` use flat dict JSON strings. They must be rewritten to use array-of-objects JSON. The behavior under test is unchanged — only the JSON format changes. Each test must be updated to supply valid array format, e.g.:
```json
[
  { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
  { "Oid": "1.3.6.1.2.1.1.3.0", "MetricName": "sysUpTime" }
]
```

### Pattern 2: C# Model Rename

Full rename of `MetricPollOptions` -> `PollOptions` and property `Oids` -> `MetricNames` throughout. The runtime record `MetricPollInfo.Oids` keeps its name (it holds resolved OID strings post-resolution). The property rename must trace through all 11 affected files.

**Files requiring rename (confirmed by grep):**

| File | What Changes |
|------|-------------|
| `Configuration/MetricPollOptions.cs` | Class name, property name |
| `Configuration/DeviceOptions.cs` | Property type `List<MetricPollOptions>` -> `List<PollOptions>`, property name `MetricPolls` -> `Polls` |
| `Configuration/Validators/DevicesOptionsValidator.cs` | Type refs, property refs, error message prefix strings |
| `Pipeline/DeviceRegistry.cs` | `.MetricPolls` -> `.Polls`, `.Oids` -> `.MetricNames` (in two places: constructor + ReloadAsync) |
| `Pipeline/MetricPollInfo.cs` | XML doc comment reference to `MetricPolls` |
| `Pipeline/CardinalityAuditService.cs` | If it accesses `.MetricPolls` (needs inspection — accessed indirectly via DeviceRegistry) |
| `Extensions/ServiceCollectionExtensions.cs` | `device.MetricPolls.Count` (3 places), `device.MetricPolls[pi]` (1 place) |
| `Jobs/MetricPollJob.cs` | Any references (needs inspection) |
| `appsettings.Development.json` | JSON keys `MetricPolls`->`Polls`, `Oids`->`MetricNames` |

**Tests requiring rename:**
| File | What Changes |
|------|-------------|
| `tests/.../Pipeline/DeviceRegistryTests.cs` | `MetricPolls = [...]`, `new MetricPollOptions { Oids = ... }` -> `Polls`, `PollOptions`, `MetricNames` |
| `tests/.../Pipeline/PipelineIntegrationTests.cs` | `MetricPolls = []` -> `Polls = []` |

**DevicesOptionsValidator error message strings:** The validator produces messages like `"Devices[0].MetricPolls[0].Oids must contain at least one entry"`. These must become `"Devices[0].Polls[0].MetricNames must contain at least one entry"`. If any tests assert on these exact strings, they must be updated too — check `tests/SnmpCollector.Tests/Configuration/` for validator tests.

### Pattern 3: Name Resolution in DeviceWatcherService

`DeviceWatcherService.HandleConfigMapChangedAsync` currently:
1. Deserializes JSON to `List<DeviceOptions>`
2. Calls `_deviceRegistry.ReloadAsync(devices)` — passes options with `MetricNames` (human names)
3. Calls `_pollScheduler.ReconcileAsync(...)`

After Phase 31, step 2 must resolve names before calling `ReloadAsync`. Two implementation options exist:

**Option A (recommended): Resolve in DeviceWatcherService, before ReloadAsync**
- Inject `IOidMapService` into `DeviceWatcherService` constructor
- After deserialization, iterate `device.Polls` and for each `metricName` in `poll.MetricNames`, call `_oidMapService.ResolveToOid(metricName)` to get the OID
- Resolved OIDs are passed somehow to DeviceRegistry... but `DeviceRegistry.ReloadAsync` takes `List<DeviceOptions>` and internally reads `.MetricNames`. So either:
  - Pass resolved OIDs back into `DeviceOptions.Polls[].MetricNames` (awkward — replaces names with OIDs in the model)
  - Create an intermediate `List<(DeviceOptions device, List<List<string>> resolvedOids)>` structure
  - OR change `DeviceRegistry.ReloadAsync` to also accept an `IOidMapService` for resolution

**Option B (recommended by context): Resolve inside DeviceRegistry.ReloadAsync**
- Inject `IOidMapService` into `DeviceRegistry`
- In `ReloadAsync`, for each `poll.MetricNames`, call `ResolveToOid(name)` to build `MetricPollInfo.Oids`
- Handles both the K8s path (DeviceWatcherService) AND local dev path (Program.cs calls `ReloadAsync` directly)
- Constructor also needs the same resolution (for startup from `IOptions<DevicesOptions>`)
- This is architecturally cleaner: resolution is co-located with the construction of `MetricPollInfo`

**The context says:** "Config-level only: Name → OID resolution happens at device config load time" and "Claude's discretion: Internal implementation of name resolution in DeviceWatcherService." This suggests Option A but with a complication: the local dev path in Program.cs also calls `deviceRegistry.ReloadAsync(devices)`. If resolution is only in `DeviceWatcherService`, the local dev path bypasses it.

**Resolution:** Option B (inject `IOidMapService` into `DeviceRegistry`) covers all paths. The DeviceWatcherService remains the trigger but the resolution logic lives in DeviceRegistry.

**Log format for unresolvable names:**
```
"MetricName '{name}' on device '{deviceName}' poll {pollIndex} not found in OID map -- skipping"
```
Per-name detail in reload diff:
```
"Device '{deviceName}' poll {pollIndex}: resolved {resolved}/{total} metric names; skipped: [{name1}, {name2}]"
```

### Pattern 4: Local Dev oidmaps.json Loading in Program.cs

The current code in `Program.cs`:
```csharp
var oidMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(oidJson, jsonOptions);
```

This must change to parse the new array format. Use `OidMapWatcherService.ValidateAndParseOidMap` (which is already `internal static`) OR parse directly:
```csharp
// Option 1: Reuse ValidateAndParseOidMap (already internal static)
var oidMap = OidMapWatcherService.ValidateAndParseOidMap(oidJson, logger);

// Option 2: Use JsonDocument directly
// ... same logic as OidMapWatcherService
```

Option 1 is better — single source of truth for parsing. `OidMapWatcherService` is `internal` to the main assembly, Program.cs is in the same assembly, so this is accessible.

### Pattern 5: K8s ConfigMap oidmaps.json Sync

The K8s simetra-oidmaps.yaml is missing 6 entries from the local oidmaps.json:
- OBP: `obp_device_type` (1.3.6.1.4.1.47477.10.21.60.1.0), `obp_sw_version` (1.3.6.1.4.1.47477.10.21.60.13.0), `obp_serial` (1.3.6.1.4.1.47477.10.21.60.15.0)
- NPB: `npb_model` (1.3.6.1.4.1.47477.100.1.5.0), `npb_serial` (1.3.6.1.4.1.47477.100.1.6.0), `npb_sw_version` (1.3.6.1.4.1.47477.100.1.7.0)

These must be added during the array-format migration. The K8s ConfigMap has 92 entries + 7 E2E sim entries = 99 after the fix (vs. 98 local + 7 E2E = 105 total in K8s after adding the 6 missing ones).

Wait — reconciling: local oidmaps.json has 98 entries (27 OBP + 71 NPB). K8s has 92 entries (missing 6) + 7 E2E sim entries. After sync: K8s gets the 6 missing entries added (total 98 + 7 E2E = 105 entries in K8s). Local oidmaps.json does NOT have the E2E sim entries (they only live in K8s). This is by design — local dev doesn't use the E2E simulator.

### Pattern 6: E2E Fixture Files

**Files to update (all have `MetricPolls` and `Oids` in their JSON):**
- `tests/e2e/fixtures/device-added-configmap.yaml`
- `tests/e2e/fixtures/device-modified-interval-configmap.yaml`
- `tests/e2e/fixtures/device-removed-configmap.yaml`
- `tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml`
- `tests/e2e/fixtures/fake-device-configmap.yaml`
- `tests/e2e/fixtures/.original-devices-configmap.yaml` (hidden file — saved at runtime by scenario 06)
- `deploy/k8s/snmp-collector/simetra-devices.yaml`

**E2E scenario 06:** Contains inline jq that builds JSON with `"MetricPolls": [{"IntervalSeconds": 10, "Oids": [...]}]`. Must change to `"Polls": [{"IntervalSeconds": 10, "MetricNames": [...]}]`. After the rename, device configs use metric names — the E2E-sim poll group entry in scenario 06 uses OID `1.3.6.1.4.1.47477.999.1.1.0`. The metric name for this OID is `e2e_gauge_test` (from simetra-oidmaps.yaml). So the jq inline JSON becomes `"Polls": [{"IntervalSeconds": 10, "MetricNames": ["e2e_gauge_test"]}]`.

**Devices JSON translation (OIDs to names):** The full devices.json rewrite must translate every OID in `MetricPolls[].Oids[]` to the corresponding `MetricNames[]` entry using the oidmaps.json. All OIDs in the current devices.json are present in oidmaps.json — confirmed by cross-referencing. The OBP device has 3 poll groups; NPB has 2 poll groups. The E2E-SIM device in K8s has 7 OIDs all mappable to e2e_* names.

### Anti-Patterns to Avoid

- **Dual-format parsing:** Do not write a parser that accepts both flat dict and array-of-objects. Full replacement only (as decided).
- **Resolving names at poll time:** Resolution must happen at device load time, not in `MetricPollJob`. The `MetricPollInfo.Oids` field holds the already-resolved OIDs.
- **Skipping .original-devices-configmap.yaml:** This hidden fixture file is created at runtime but already exists in the repo — it must be updated manually. Runtime re-creation will produce old format if the file is not updated.
- **Leaving appsettings.Development.json unchanged:** This file also has `MetricPolls`/`Oids` and is used for local dev testing. It must be updated even though local dev doesn't use the K8s watchers.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID map parsing | Custom recursive parser | `JsonDocument.EnumerateArray()` with `GetProperty("Oid")` | Already used in the codebase; straightforward extension |
| Name-to-OID reverse lookup | Manual dictionary | `IOidMapService.ResolveToOid(name)` | Phase 30 already implemented this |
| ConfigMap update | kubectl patch | `kubectl apply -f` on updated YAML | Consistent with existing deployment pattern |

**Key insight:** All the moving parts already exist. This phase is mechanical rewiring, not new capability.

## Common Pitfalls

### Pitfall 1: DevicesOptionsValidator Error Message Prefix

**What goes wrong:** Validator produces error messages like `"Devices[0].MetricPolls[0].Oids must contain at least one entry"`. After rename these become `"Devices[0].Polls[0].MetricNames must contain at least one entry"`. If any tests assert on the exact error message string format, they break.

**Why it happens:** The prefix string is built from the property name: `$"Devices[{deviceIndex}].MetricPolls[{pollIndex}]"`.

**How to avoid:** Search for validator test assertions on these strings in `tests/SnmpCollector.Tests/Configuration/`. Check `OidMapAutoScanTests.cs` and any `DevicesOptionsValidatorTests.cs` for string matches.

**Warning signs:** Compile succeeds but tests fail with string mismatch.

### Pitfall 2: ServiceCollectionExtensions Startup Binding

**What goes wrong:** `AddSnmpScheduling` does its own `Configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices)` to calculate thread pool size. It iterates `device.MetricPolls.Count`. After rename, this becomes `device.Polls.Count`. There are 3 occurrences of `MetricPolls` in this file.

**Why it happens:** The startup binding uses raw options objects before DI is built, not injected `IOptions<T>`.

**How to avoid:** Run a grep for `MetricPolls` across the full file after rename to confirm zero occurrences.

### Pitfall 3: DeviceRegistry Requires IOidMapService for Resolution

**What goes wrong:** The `DeviceRegistry` constructor takes `IOptions<DevicesOptions>` and builds `MetricPollInfo.Oids` from `poll.Oids.AsReadOnly()`. After the rename, `poll.MetricNames` contains human names, not OIDs. `MetricPollInfo.Oids` must be populated with resolved OIDs. If `IOidMapService` is not injected into `DeviceRegistry`, the constructor builds empty or wrong `Oids`.

**Why it happens:** Constructor and `ReloadAsync` both build `MetricPollInfo` from `DeviceOptions`. Both paths must resolve names.

**How to avoid:** Inject `IOidMapService` into `DeviceRegistry`. Add resolution inside the shared helper or inline in both constructor and `ReloadAsync`. The local dev `Program.cs` path calls `ReloadAsync` after the OID map has been loaded, so resolution will work correctly.

**Warning signs:** `MetricPollInfo.Oids` is empty or contains strings like `"obp_channel_L1"` instead of `"1.3.6.1.4.1.47477.10.21.1.3.4.0"`.

### Pitfall 4: Local Dev OID Map Load Order

**What goes wrong:** In `Program.cs`, the OID map is loaded BEFORE devices. The OID map must be populated into `OidMapService` before `DeviceRegistry.ReloadAsync(devices)` runs name resolution.

**Current Program.cs order (correct):**
1. Load oidmaps.json → call `oidMapService.UpdateMap(oidMap)`
2. Load devices.json → call `deviceRegistry.ReloadAsync(devices)` (resolution happens here)

**Why it happens:** If the order is swapped or if resolution is attempted before UpdateMap, all names resolve to null → all poll groups have zero OIDs.

**How to avoid:** Keep existing Program.cs load order. Add a warning log if OID map has 0 entries when device resolution runs (device registered with 0 poll groups).

### Pitfall 5: oidmaps.json Comments in Array Format

**What goes wrong:** The local oidmaps.json has section comments like `// ---- OBP OIDs ----` between object properties. In array format, these comments appear between array elements. System.Text.Json with `JsonCommentHandling.Skip` handles this correctly for both positions. But if comments are added between the fields of an object element (e.g., between `"Oid"` and `"MetricName"`), this also works fine.

**How to avoid:** Keep the existing `JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }` — already set. No code change needed.

### Pitfall 6: E2E Fixture .original-devices-configmap.yaml Is a Hidden File

**What goes wrong:** The file `.original-devices-configmap.yaml` starts with a dot (hidden) and is created at runtime by scenario 06 via `save_configmap`. But it already exists in the repo with the old format. If scenario 06 runs after Phase 31 deploy but before the repo file is updated, `save_configmap` overwrites it with the new format (from the live ConfigMap) — which is fine. But if it's used by scenario 07 restore before the live ConfigMap has been updated, it has the old format.

**How to avoid:** Update the file in the repo as part of this phase. The file is just a saved copy of the K8s ConfigMap — it should match the updated simetra-devices.yaml format.

### Pitfall 7: The E2E-SIM Device in Devices ConfigMap

**What goes wrong:** The K8s `simetra-devices.yaml` has an E2E-SIM device with OIDs `1.3.6.1.4.1.47477.999.1.1.0` through `.7`. These must be translated to metric names `e2e_gauge_test`, `e2e_integer_test`, `e2e_counter32_test`, `e2e_counter64_test`, `e2e_timeticks_test`, `e2e_info_test`, `e2e_ip_test`. These names exist in simetra-oidmaps.yaml but NOT in local oidmaps.json. This is correct — local dev doesn't have the E2E simulator.

**How to avoid:** When rewriting K8s simetra-devices.yaml, use the K8s oidmaps entries (which include E2E sim names). Don't try to add E2E sim OIDs to local oidmaps.json.

## Code Examples

### OidMapWatcherService: New Array Parsing Logic

```csharp
// Source: direct codebase analysis of OidMapWatcherService.ValidateAndParseOidMap
// Replace EnumerateObject() with EnumerateArray() + property access

internal static Dictionary<string, string>? ValidateAndParseOidMap(string jsonContent, ILogger logger)
{
    JsonDocument doc;
    try
    {
        doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "Failed to parse {ConfigKey} -- skipping reload", ConfigKey, ConfigMapName);
        return null;
    }

    using (doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            logger.LogError("OidMap JSON must be an array of objects -- skipping reload");
            return null;
        }

        // Pass 1: Enumerate array elements, extract Oid/MetricName, detect duplicate OIDs
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateOids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawEntries = new List<(string oid, string name)>();

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("Oid", out var oidProp) ||
                !element.TryGetProperty("MetricName", out var nameProp))
            {
                logger.LogWarning("OidMap array element missing Oid or MetricName -- skipping entry");
                continue;
            }

            var oid = oidProp.GetString();
            var name = nameProp.ValueKind == JsonValueKind.Null ? null : nameProp.GetString();

            if (string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(name))
            {
                logger.LogWarning("OidMap entry skipped: Oid or MetricName is null or empty");
                continue;
            }

            if (!seen.Add(oid))
                duplicateOids.Add(oid);
            else
                rawEntries.Add((oid, name));
        }

        // Passes 2 and 3 remain unchanged from current implementation
        // ...
    }
}
```

### DeviceRegistry: Name Resolution During Load

```csharp
// Source: direct codebase analysis — DeviceRegistry.cs, both constructor and ReloadAsync
// IOidMapService injected as new constructor parameter

var pollGroups = d.Polls
    .Select((poll, index) =>
    {
        var resolvedOids = poll.MetricNames
            .Select(name =>
            {
                var oid = _oidMapService.ResolveToOid(name);
                if (oid is null)
                {
                    _logger.LogWarning(
                        "MetricName '{MetricName}' on device '{DeviceName}' poll {PollIndex} not found in OID map -- skipping",
                        name, d.Name, index);
                }
                return oid;
            })
            .Where(oid => oid is not null)
            .Select(oid => oid!)
            .ToList();

        return new MetricPollInfo(
            PollIndex: index,
            Oids: resolvedOids.AsReadOnly(),
            IntervalSeconds: poll.IntervalSeconds,
            TimeoutMultiplier: poll.TimeoutMultiplier);
    })
    .ToList()
    .AsReadOnly();
```

### New devices.json format (abbreviated)

```json
[
  {
    "Name": "OBP-01",
    "IpAddress": "127.0.0.1",
    "Port": 10161,
    "Polls": [
      {
        "IntervalSeconds": 10,
        "MetricNames": [
          "obp_channel_L1",
          "obp_r1_power_L1",
          "obp_r2_power_L1",
          "obp_r3_power_L1",
          "obp_r4_power_L1",
          "obp_link_state_L2",
          "obp_channel_L2",
          "obp_r1_power_L2",
          "obp_r2_power_L2",
          "obp_r3_power_L2",
          "obp_r4_power_L2",
          "obp_link_state_L3",
          "obp_channel_L3",
          "obp_r1_power_L3",
          "obp_r2_power_L3",
          "obp_r3_power_L3",
          "obp_r4_power_L3",
          "obp_link_state_L4",
          "obp_channel_L4",
          "obp_r1_power_L4",
          "obp_r2_power_L4",
          "obp_r3_power_L4",
          "obp_r4_power_L4"
        ]
      },
      {
        "IntervalSeconds": 30,
        "MetricNames": [
          "obp_link_state_L1"
        ]
      },
      {
        "IntervalSeconds": 60,
        "MetricNames": [
          "obp_device_type",
          "obp_sw_version",
          "obp_serial"
        ]
      }
    ]
  }
]
```

### Complete OID-to-Name Translation for devices.json

**OBP-01, poll group 0 (IntervalSeconds: 10) — 23 metric names:**
```
obp_channel_L1, obp_r1_power_L1, obp_r2_power_L1, obp_r3_power_L1, obp_r4_power_L1,
obp_link_state_L2, obp_channel_L2, obp_r1_power_L2, obp_r2_power_L2, obp_r3_power_L2, obp_r4_power_L2,
obp_link_state_L3, obp_channel_L3, obp_r1_power_L3, obp_r2_power_L3, obp_r3_power_L3, obp_r4_power_L3,
obp_link_state_L4, obp_channel_L4, obp_r1_power_L4, obp_r2_power_L4, obp_r3_power_L4, obp_r4_power_L4
```

**OBP-01, poll group 1 (IntervalSeconds: 30) — 1 metric name:**
```
obp_link_state_L1
```

**OBP-01, poll group 2 (IntervalSeconds: 60) — 3 metric names:**
```
obp_device_type, obp_sw_version, obp_serial
```

**NPB-01, poll group 0 (IntervalSeconds: 10) — 68 metric names:**
```
npb_cpu_util, npb_mem_util, npb_sys_temp, npb_uptime,
npb_port_status_P1 through npb_port_rx_drops_P8 (8 metrics x 8 ports = 64)
```

**NPB-01, poll group 1 (IntervalSeconds: 60) — 3 metric names:**
```
npb_model, npb_serial, npb_sw_version
```

**E2E-SIM (K8s only), poll group 0 (IntervalSeconds: 10) — 7 metric names:**
```
e2e_gauge_test, e2e_integer_test, e2e_counter32_test, e2e_counter64_test,
e2e_timeticks_test, e2e_info_test, e2e_ip_test
```

### Updated appsettings.Development.json snippet

```json
"Devices": [
  {
    "Name": "npb-core-01",
    "IpAddress": "10.0.10.1",
    "Polls": [
      { "MetricNames": ["1.3.6.1.4.1.2636.3.1.13.1.8"], "IntervalSeconds": 30 },
      { "MetricNames": ["1.3.6.1.2.1.2.2.1.10", "1.3.6.1.2.1.2.2.1.16"], "IntervalSeconds": 300 }
    ]
  }
]
```

Note: `appsettings.Development.json` currently has raw OIDs in the `Oids` field. After rename, the field is `MetricNames`. These OIDs are not in oidmaps.json (they're test values), so they will log "not found in OID map" warnings during local dev. This is acceptable — the dev config just demonstrates the format.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `{ "OID": "name" }` flat dict | `[{ "Oid": "...", "MetricName": "..." }]` array | Phase 31 | Enables explicit validation of both fields, cleaner structure |
| `MetricPollOptions.Oids[]` (raw OIDs) | `PollOptions.MetricNames[]` (human names) | Phase 31 | Operators write human names; runtime resolves to OIDs |
| No name resolution | `IOidMapService.ResolveToOid` at load time | Phase 31 | Devices work with unresolvable names (0-OID poll groups excluded from runtime) |

**Deprecated/outdated after Phase 31:**
- `EnumerateObject()` pattern in oidmap parsing: replaced by `EnumerateArray()` + property access
- `MetricPollOptions` class: deleted, replaced by `PollOptions`
- `DeviceOptions.MetricPolls` property: deleted, replaced by `Polls`
- Flat dict format in oidmaps.json: deleted from both local and K8s

## Open Questions

1. **Injection point for IOidMapService: DeviceRegistry vs DeviceWatcherService**
   - What we know: Resolution must happen before `MetricPollInfo.Oids` is populated. Both constructor (startup) and `ReloadAsync` (hot-reload) build `MetricPollInfo`. Program.cs local dev path also calls `ReloadAsync`.
   - What's unclear: The CONTEXT says "Claude's discretion: Internal implementation in DeviceWatcherService" but also "config-level only: at device config load time." If DeviceWatcherService resolves names and then passes already-resolved OIDs somehow, DeviceRegistry doesn't need IOidMapService. But then the startup constructor path and local dev path don't get resolution.
   - Recommendation: **Inject IOidMapService into DeviceRegistry**. This single change covers all paths (constructor, ReloadAsync from DeviceWatcherService, ReloadAsync from Program.cs). DeviceWatcherService only needs to inject IOidMapService if resolution must log device-context-aware messages (which DeviceRegistry can also do since it has the device name). DeviceRegistry injection is cleaner.

2. **CardinalityAuditService.cs actual MetricPolls usage**
   - What we know: File references `_registry` (IDeviceRegistry) and `_oidMap` (IOidMapService). It may iterate poll groups via `deviceRegistry.AllDevices[i].PollGroups`.
   - What's unclear: Whether it accesses `MetricPolls` directly or via `DeviceInfo.PollGroups` (the resolved runtime form).
   - Recommendation: Inspect the file fully before planning — included in the grep list but the content was only partially read.

3. **MetricPollJob.cs: CONFIRMED no change needed**
   - Verified: Uses `device.PollGroups[pollIndex]` (MetricPollInfo) and `pollGroup.Oids` — zero references to DeviceOptions, MetricPollOptions, or MetricPolls. No file changes needed.

4. **DevicesOptionsValidator test coverage**
   - What we know: `OidMapAutoScanTests.cs` is unrelated to DevicesOptionsValidator. There may also be `DevicesOptionsValidatorTests.cs` — not confirmed present.
   - Recommendation: Check before planning for any test asserting exact validator error message strings containing "MetricPolls" or "Oids must contain".

## File Inventory: Complete Change List

### C# Source Files (src/)
| File | Change Type |
|------|------------|
| `Configuration/MetricPollOptions.cs` | Rename class + property |
| `Configuration/DeviceOptions.cs` | Rename property type + name |
| `Configuration/Validators/DevicesOptionsValidator.cs` | Rename refs + error message strings |
| `Pipeline/DeviceRegistry.cs` | Rename refs + add IOidMapService injection + name resolution |
| `Pipeline/MetricPollInfo.cs` | XML doc comment update only |
| `Pipeline/CardinalityAuditService.cs` | No changes needed (uses DeviceInfo.PollGroups, not DeviceOptions) |
| `Extensions/ServiceCollectionExtensions.cs` | Rename `.MetricPolls.Count` (3 places) + `.MetricPolls[pi]` |
| `Jobs/MetricPollJob.cs` | No changes needed (uses MetricPollInfo, not DeviceOptions) |

### C# Test Files (tests/)
| File | Change Type |
|------|------------|
| `Pipeline/DeviceRegistryTests.cs` | Rename `MetricPolls`, `MetricPollOptions`, `Oids` |
| `Pipeline/PipelineIntegrationTests.cs` | Rename `MetricPolls = []` -> `Polls = []` |
| `Services/OidMapWatcherValidationTests.cs` | Rewrite all 9 tests with array-of-objects JSON |
| `Configuration/OidMapAutoScanTests.cs` | Verify no MetricPollOptions refs (likely unrelated) |

### Config Files (src/SnmpCollector/)
| File | Change Type |
|------|------------|
| `config/oidmaps.json` | Rewrite flat dict -> array of objects (98 entries) |
| `config/devices.json` | Rewrite OID strings -> metric names, `MetricPolls`->`Polls`, `Oids`->`MetricNames` |
| `appsettings.Development.json` | `MetricPolls`->`Polls`, `Oids`->`MetricNames` |
| `Program.cs` | Change oidmaps.json parsing from `Deserialize<Dictionary>` to array parsing |

### K8s Manifests (deploy/)
| File | Change Type |
|------|------------|
| `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` | Rewrite flat dict -> array of objects + add 6 missing entries |
| `deploy/k8s/snmp-collector/simetra-devices.yaml` | OIDs -> metric names, `MetricPolls`->`Polls`, `Oids`->`MetricNames` |

### E2E Fixtures (tests/e2e/fixtures/)
| File | Change Type |
|------|------------|
| `device-added-configmap.yaml` | `MetricPolls`->`Polls`, OIDs->`MetricNames` |
| `device-modified-interval-configmap.yaml` | `MetricPolls`->`Polls`, OIDs->`MetricNames` |
| `device-removed-configmap.yaml` | `MetricPolls`->`Polls`, OIDs->`MetricNames` |
| `e2e-sim-unmapped-configmap.yaml` | `MetricPolls`->`Polls`, OIDs->`MetricNames` |
| `fake-device-configmap.yaml` | `MetricPolls`->`Polls`, OIDs->`MetricNames` |
| `.original-devices-configmap.yaml` | `MetricPolls`->`Polls`, OIDs->`MetricNames` |

### E2E Scenarios (tests/e2e/scenarios/)
| File | Change Type |
|------|------------|
| `06-poll-unreachable.sh` | Inline jq JSON: `MetricPolls`->`Polls`, `Oids`->`MetricNames`, OID->`e2e_gauge_test` |

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection — all findings verified by reading actual files
  - `src/SnmpCollector/Services/OidMapWatcherService.cs` — full ValidateAndParseOidMap logic
  - `src/SnmpCollector/Services/DeviceWatcherService.cs` — current flow, no IOidMapService injection
  - `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — both constructor and ReloadAsync paths
  - `src/SnmpCollector/Pipeline/OidMapService.cs` — ResolveToOid exists and returns null on miss
  - `src/SnmpCollector/Configuration/MetricPollOptions.cs` — current class/property names
  - `src/SnmpCollector/Configuration/DeviceOptions.cs` — MetricPolls property
  - `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — error message format
  - `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — MetricPolls refs (3 locations)
  - `src/SnmpCollector/Program.cs` — local dev oidmaps.json parsing pattern
  - `src/SnmpCollector/config/oidmaps.json` — 98 entries, flat dict format
  - `src/SnmpCollector/config/devices.json` — 2 devices, raw OIDs
  - `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` — 92+7=99 entries, missing 6 OBP/NPB info OIDs
  - `deploy/k8s/snmp-collector/simetra-devices.yaml` — 3 devices (OBP-01, NPB-01, E2E-SIM)
  - `tests/SnmpCollector.Tests/Services/OidMapWatcherValidationTests.cs` — 9 tests, all flat dict JSON
  - `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` — MetricPollOptions usage
  - `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` — MetricPolls = []
  - `tests/e2e/fixtures/fake-device-configmap.yaml` — fixture format
  - `tests/e2e/scenarios/06-poll-unreachable.sh` — inline JSON with Oids

## Metadata

**Confidence breakdown:**
- C# model rename scope: HIGH — verified by grep of all affected files
- OidMap parsing change: HIGH — ValidateAndParseOidMap logic read in full; EnumerateArray approach is straightforward
- Name resolution location: HIGH — Inject IOidMapService into DeviceRegistry; covers all three load paths (startup constructor, DeviceWatcherService hot-reload, Program.cs local dev)
- E2E fixture scope: HIGH — all fixture files enumerated and confirmed to contain MetricPolls/Oids
- OID->name translation accuracy: HIGH — cross-referenced devices.json OIDs against oidmaps.json; all present

**Research date:** 2026-03-13
**Valid until:** 2026-04-13 (stable codebase — no external library concerns)
