# Phase 31: Human-Name Device Config - Research

**Researched:** 2026-03-13
**Domain:** C# model rename, config schema migration, name-to-OID resolution at device load time
**Confidence:** HIGH

## Summary

This phase is a mechanical rename + resolution layer insertion. The codebase has a clear, well-understood structure where `MetricPollOptions.Oids` flows through `DeviceOptions.MetricPolls` into `MetricPollInfo.Oids` and finally into `MetricPollJob` which builds SNMP Variable lists. The rename (`MetricPolls` to `Polls`, `Oids` to `Names`, `MetricPollOptions` to `PollOptions`, `MetricPollInfo` to `PollInfo`) is straightforward but touches many files. The resolution layer (calling `IOidMapService.ResolveToOid` per name) must be inserted in `DeviceWatcherService.HandleConfigMapChangedAsync` between JSON deserialization and `DeviceRegistry.ReloadAsync`.

The K8s oidmap ConfigMaps are MISSING 6 entries that exist in the local `oidmaps.json`: the OBP 60s group (obp_device_type, obp_sw_version, obp_serial) and NPB 60s group (npb_model, npb_serial, npb_sw_version). These must be added to ALL oidmap ConfigMaps as a prerequisite, or they will fail name resolution.

**Primary recommendation:** Split into (1) oidmap sync prerequisite, (2) C# model rename, (3) resolution logic + DeviceWatcherService injection, (4) JSON config rewrite, (5) E2E fixture rewrite.

## Standard Stack

No new libraries needed. This phase uses only existing codebase patterns.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Text.Json | built-in | JSON deserialization of device config | Already used throughout |
| IOidMapService | internal | ResolveToOid for name-to-OID translation | Phase 30 deliverable |
| FrozenDictionary | built-in | Atomic swap in DeviceRegistry | Already used throughout |

## Architecture Patterns

### Recommended Resolution Flow

```
JSON Config (Names[])
    |
    v
DeviceWatcherService.HandleConfigMapChangedAsync
    |  -- deserialize to List<DeviceOptions> (Names[] = metric names)
    |  -- for each device, for each poll group:
    |       resolve each Name via IOidMapService.ResolveToOid
    |       collect resolved OIDs, log unresolved names
    |  -- build "resolved" DeviceOptions with OIDs in the Oids field
    |       (or more cleanly: resolve into PollInfo.Oids directly)
    v
DeviceRegistry.ReloadAsync (receives resolved OID lists)
    |
    v
DynamicPollScheduler.ReconcileAsync (schedules Quartz jobs with OID lists)
    |
    v
MetricPollJob.Execute (uses OIDs for SNMP GET -- unchanged)
```

### Key Design Decision: Where to Resolve

Resolution happens in DeviceWatcherService AFTER deserialization but BEFORE passing to DeviceRegistry. This is the "Strategy A" from STATE.md. The config model (`PollOptions`) holds `Names[]` (metric names from JSON). The runtime model (`PollInfo`) holds `Oids` (resolved OID strings). The translation bridge lives in DeviceWatcherService.

Two approaches for the bridge:

**Approach A (recommended): Resolve in DeviceWatcherService, keep PollInfo.Oids**
- `PollOptions.Names` = what the JSON says (metric names)
- Resolution in DeviceWatcherService maps Names to OIDs
- `PollInfo.Oids` = resolved OID strings (unchanged runtime type)
- DeviceRegistry, DynamicPollScheduler, MetricPollJob: UNCHANGED
- Less blast radius, simpler

**Approach B: Rename PollInfo.Oids to PollInfo.Oids**
Same as A but also renames the PollInfo field. Since MetricPollInfo is also being renamed to PollInfo, the Oids field name is fine -- it still holds OID strings at runtime.

Recommendation: **Approach A**. Keep `PollInfo.Oids` named `Oids` since it holds actual OID strings at runtime. Only the config-level model (`PollOptions.Names`) uses metric names.

### File Change Map

**C# Model Renames (15+ files):**

| Old Name | New Name | File |
|----------|----------|------|
| `MetricPollOptions` | `PollOptions` | Configuration/MetricPollOptions.cs (rename file too) |
| `MetricPollOptions.Oids` | `PollOptions.Names` | Same file |
| `DeviceOptions.MetricPolls` | `DeviceOptions.Polls` | Configuration/DeviceOptions.cs |
| `MetricPollInfo` | `PollInfo` | Pipeline/MetricPollInfo.cs (rename file too) |
| `DeviceInfo.PollGroups` | Keep as `PollGroups` | Pipeline/DeviceInfo.cs (no rename needed) |

**Files referencing MetricPollOptions/MetricPolls/Oids:**

Source code (must rename):
1. `Configuration/MetricPollOptions.cs` -- class + property rename
2. `Configuration/DeviceOptions.cs` -- `MetricPolls` to `Polls`, type `MetricPollOptions` to `PollOptions`
3. `Configuration/Validators/DevicesOptionsValidator.cs` -- references `MetricPolls`, `Oids`, `MetricPollOptions`
4. `Pipeline/MetricPollInfo.cs` -- class rename to `PollInfo`
5. `Pipeline/DeviceInfo.cs` -- `MetricPollInfo` to `PollInfo` (doc comments)
6. `Pipeline/DeviceRegistry.cs` -- `d.MetricPolls`, `poll.Oids`, `MetricPollInfo`
7. `Services/DeviceWatcherService.cs` -- injection point for resolution logic
8. `Services/DynamicPollScheduler.cs` -- `MetricPollInfo` type references
9. `Jobs/MetricPollJob.cs` -- `pollGroup.Oids`
10. `Extensions/ServiceCollectionExtensions.cs` -- `device.MetricPolls`
11. `Pipeline/TenantVectorRegistry.cs` -- `pollGroup.Oids`
12. `Pipeline/CardinalityAuditService.cs` -- `p.Oids`

Test code (must rename):
13. `Tests/Pipeline/DeviceRegistryTests.cs` -- `MetricPollOptions`, `MetricPolls`, `Oids`, `MetricPollInfo`
14. `Tests/Jobs/MetricPollJobTests.cs` -- `MetricPollInfo`
15. `Tests/Services/DynamicPollSchedulerTests.cs` -- `MetricPollInfo`
16. `Tests/Pipeline/PipelineIntegrationTests.cs` -- references
17. `Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` -- references

**JSON/YAML Config Files (must rewrite):**

Local dev:
18. `config/devices.json` -- `MetricPolls`/`Oids` to `Polls`/`Names`, OIDs to metric names

K8s manifests:
19. `deploy/k8s/snmp-collector/simetra-devices.yaml` -- same rewrite
20. `deploy/k8s/production/configmap.yaml` -- same rewrite (the simetra-devices section)

E2E fixtures (must rewrite):
21. `tests/e2e/fixtures/device-added-configmap.yaml`
22. `tests/e2e/fixtures/device-removed-configmap.yaml`
23. `tests/e2e/fixtures/device-modified-interval-configmap.yaml`
24. `tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml`
25. `tests/e2e/fixtures/fake-device-configmap.yaml`
26. `tests/e2e/fixtures/invalid-json-devices-schema-configmap.yaml` (if referencing MetricPolls)

E2E scenarios (may reference OIDs inline):
27. `tests/e2e/scenarios/06-poll-unreachable.sh`
28. `tests/e2e/scenarios/07-poll-recovered.sh`
29. Other scenarios referencing `MetricPolls` or `Oids` in grep/log assertions

**OID Map prerequisite -- missing entries in K8s ConfigMaps:**

The following OIDs are in local `oidmaps.json` but MISSING from K8s oidmap ConfigMaps:
- `obp_device_type` (1.3.6.1.4.1.47477.10.21.60.1.0)
- `obp_sw_version` (1.3.6.1.4.1.47477.10.21.60.13.0)
- `obp_serial` (1.3.6.1.4.1.47477.10.21.60.15.0)
- `npb_model` (1.3.6.1.4.1.47477.100.1.5.0)
- `npb_serial` (1.3.6.1.4.1.47477.100.1.6.0)
- `npb_sw_version` (1.3.6.1.4.1.47477.100.1.7.0)

These must be added to:
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml`
- `deploy/k8s/production/configmap.yaml` (oidmaps section)

E2E-SIM entries (999.x) already exist in the K8s oidmap.

### OID-to-Name Translation Table

For rewriting devices.json, here is the complete OID-to-metric-name mapping needed:

**OBP-01 10s group (22 entries):**
obp_channel_L1, obp_r1_power_L1, obp_r2_power_L1, obp_r3_power_L1, obp_r4_power_L1,
obp_link_state_L2, obp_channel_L2, obp_r1_power_L2, obp_r2_power_L2, obp_r3_power_L2, obp_r4_power_L2,
obp_link_state_L3, obp_channel_L3, obp_r1_power_L3, obp_r2_power_L3, obp_r3_power_L3, obp_r4_power_L3,
obp_link_state_L4, obp_channel_L4, obp_r1_power_L4, obp_r2_power_L4, obp_r3_power_L4, obp_r4_power_L4

**OBP-01 30s group (1 entry):**
obp_link_state_L1

**OBP-01 60s group (3 entries):**
obp_device_type, obp_sw_version, obp_serial

**NPB-01 10s group (68 entries):**
npb_cpu_util, npb_mem_util, npb_sys_temp, npb_uptime,
npb_port_status_P1..P8, npb_port_rx_octets_P1..P8, npb_port_tx_octets_P1..P8,
npb_port_rx_packets_P1..P8, npb_port_tx_packets_P1..P8,
npb_port_rx_errors_P1..P8, npb_port_tx_errors_P1..P8, npb_port_rx_drops_P1..P8

**NPB-01 60s group (3 entries):**
npb_model, npb_serial, npb_sw_version

**E2E-SIM 10s group (7 entries):**
e2e_gauge_test, e2e_integer_test, e2e_counter32_test, e2e_counter64_test,
e2e_timeticks_test, e2e_info_test, e2e_ip_test

### Anti-Patterns to Avoid

- **Resolving at poll time:** Do NOT move resolution into MetricPollJob. Resolution is config-level only, at device load time. The user decision is explicit on this.
- **Renaming PollInfo.Oids:** Keep as `Oids` since it holds actual OID strings at runtime. Only the config model uses `Names`.
- **Changing MetricPollJob:** The job reads `pollGroup.Oids` which will still be OID strings. No changes needed in MetricPollJob beyond the type rename.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Name-to-OID resolution | Custom lookup dictionary | `IOidMapService.ResolveToOid` | Already exists, thread-safe, volatile FrozenDictionary |
| Config file deserialization | Manual JSON parsing | `System.Text.Json.JsonSerializer.Deserialize` | Already used in DeviceWatcherService |

## Common Pitfalls

### Pitfall 1: Missing OID Map Entries in K8s ConfigMaps
**What goes wrong:** OBP 60s and NPB 60s metrics (6 entries) exist in local oidmaps.json but are MISSING from K8s oidmap ConfigMaps. After migration to names, these would fail to resolve and poll jobs would lose those metrics.
**Why it happens:** K8s ConfigMaps were created separately from local config and drifted.
**How to avoid:** Add the 6 missing entries to simetra-oidmaps.yaml and production configmap.yaml BEFORE or as part of this phase.
**Warning signs:** 3 unresolved names in OBP-01 60s group, 3 in NPB-01 60s group.

### Pitfall 2: Validator Rejects Empty Names List
**What goes wrong:** `DevicesOptionsValidator` currently validates `poll.Oids.Count == 0` as an error. After rename to `Names`, a group with ALL unresolvable names would have zero resolved OIDs. But validation runs on the raw config (Names), not resolved OIDs. This is fine -- the validator should check Names, not resolved OIDs.
**How to avoid:** Rename the validator to check `poll.Names.Count == 0` (config-level validation). Resolution failures are handled separately via warning logs at resolve time.

### Pitfall 3: E2E Fixtures with FAKE-UNREACHABLE Device
**What goes wrong:** The FAKE-UNREACHABLE device in E2E fixtures uses OID `1.3.6.1.4.1.47477.999.1.1.0` (maps to `e2e_gauge_test`). Need to translate this too.
**How to avoid:** Use `e2e_gauge_test` as the name in FAKE-UNREACHABLE's poll config.

### Pitfall 4: E2E-SIM Unmapped ConfigMap Has Extra OIDs
**What goes wrong:** `e2e-sim-unmapped-configmap.yaml` includes OIDs `999.2.1.0` and `999.2.2.0` which are NOT in the oidmap -- they are intentionally unmapped for the "unknown OID" test. With names, these would need to be kept as intentionally unresolvable names (e.g., "e2e_unmapped_1", "e2e_unmapped_2") to trigger the unresolvable-name warning logs.
**How to avoid:** Use placeholder names that deliberately don't exist in the OID map. The E2E scenario testing unknown OIDs is about the wire-level Unknown resolution, not the config-level name resolution -- but the e2e-sim-unmapped config still needs valid names for the other 7 OIDs, and deliberate bad names for the 2 extras.

### Pitfall 5: ServiceCollectionExtensions Initial Quartz Job Count
**What goes wrong:** `ServiceCollectionExtensions.cs` counts `device.MetricPolls.Count` to size the Quartz thread pool. After rename, this becomes `device.Polls.Count`. This is a simple rename, but the poll count is based on config groups, not resolved names. A group with 0 resolved names creates no job, so the count could be slightly over-estimated. This is acceptable -- over-estimating thread pool is harmless.
**How to avoid:** Just rename. Don't try to account for resolution failures in thread pool sizing.

### Pitfall 6: DeviceRegistry Constructor vs ReloadAsync Symmetry
**What goes wrong:** Both the constructor and ReloadAsync build poll groups from `d.MetricPolls`. Both need the same rename AND the same resolution logic.
**How to avoid:** The resolution layer should be in DeviceWatcherService (before calling ReloadAsync) AND in the initial startup path. The constructor path also loads from DevicesOptions. Ensure both paths resolve names.

### Pitfall 7: Startup Order -- OidMapService Must Be Ready Before Devices Load
**What goes wrong:** If DeviceWatcherService tries to resolve names before OidMapService has been initialized with the OID map, all names will fail to resolve.
**How to avoid:** OidMapService is initialized in its constructor with initial entries from config. DeviceWatcherService runs as a BackgroundService (starts after DI container is built). By the time DeviceWatcherService.ExecuteAsync runs, OidMapService is already initialized. This should work naturally.

### Pitfall 8: DeviceRegistry Constructor Path
**What goes wrong:** DeviceRegistry constructor takes `IOptions<DevicesOptions>` and builds from config at DI registration time. This is the STARTUP path (before DeviceWatcherService). At startup, the config still comes from `DevicesOptions` which is bound from appsettings. The DeviceWatcherService path (K8s watch) is the RELOAD path. Both need resolution.
**How to avoid:** The DeviceRegistry constructor currently maps `poll.Oids` directly. After rename to `poll.Names`, this constructor path needs to resolve names too. Either inject IOidMapService into DeviceRegistry, or handle initial resolution in ServiceCollectionExtensions where DevicesOptions is configured.

## Code Examples

### PollOptions (renamed from MetricPollOptions)

```csharp
// Configuration/PollOptions.cs (renamed from MetricPollOptions.cs)
public sealed class PollOptions
{
    /// <summary>
    /// Metric names to resolve to OIDs and poll in this group.
    /// Each name is resolved via IOidMapService.ResolveToOid at config load time.
    /// </summary>
    public List<string> Names { get; set; } = [];

    public int IntervalSeconds { get; set; }
    public double TimeoutMultiplier { get; set; } = 0.8;
}
```

### DeviceOptions (updated)

```csharp
// Configuration/DeviceOptions.cs
public sealed class DeviceOptions
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public string? CommunityString { get; set; }
    public List<PollOptions> Polls { get; set; } = [];
}
```

### Name Resolution in DeviceWatcherService

```csharp
// In DeviceWatcherService -- new method
private List<DeviceOptions> ResolveNames(List<DeviceOptions> devices)
{
    foreach (var device in devices)
    {
        foreach (var poll in device.Polls)
        {
            var resolved = new List<string>();
            var unresolved = new List<string>();

            foreach (var name in poll.Names)
            {
                var oid = _oidMapService.ResolveToOid(name);
                if (oid is not null)
                {
                    resolved.Add(oid);
                }
                else
                {
                    unresolved.Add(name);
                    _logger.LogWarning(
                        "Metric name '{MetricName}' not found in OID map for device {DeviceName}",
                        name, device.Name);
                }
            }

            _logger.LogInformation(
                "{DeviceName} group {Interval}s: resolved {Resolved}/{Total}, unresolved: {Unresolved}",
                device.Name, poll.IntervalSeconds,
                resolved.Count, poll.Names.Count,
                unresolved.Count > 0 ? string.Join(", ", unresolved) : "none");

            // Replace Names with resolved OIDs for downstream consumption
            // NOTE: This mutates the deserialized object -- fine since it's a fresh copy
            poll.Names.Clear();
            poll.Names.AddRange(resolved);
        }
    }
    return devices;
}
```

Wait -- this is a design issue. If PollOptions.Names holds metric names but we need to pass OIDs downstream, we have two options:

**Option 1: Mutate Names to hold OIDs (hacky)**
Replace the Names list with resolved OIDs. DeviceRegistry then reads `poll.Names` thinking they are names but they are actually OIDs. Confusing and error-prone.

**Option 2 (recommended): Build PollInfo directly in DeviceWatcherService**
DeviceWatcherService resolves names and builds PollInfo (with Oids) directly. Pass resolved DeviceInfo objects to DeviceRegistry.ReloadAsync instead of raw DeviceOptions.

**Option 3 (simplest): Add a ResolvedOids property to PollOptions**
DeviceWatcherService populates `poll.ResolvedOids` after resolution. DeviceRegistry reads `ResolvedOids` instead of `Names`.

**Recommendation: Option 2 with a small variant.** Since DeviceRegistry already builds PollInfo from PollOptions, inject IOidMapService into DeviceRegistry and let it do the resolution during ReloadAsync and constructor. This keeps the resolution close to where PollInfo is built.

Actually, reconsidering -- the cleanest approach:

**Option 4 (cleanest): Inject IOidMapService into DeviceWatcherService, resolve before passing to DeviceRegistry**

DeviceWatcherService receives deserialized `List<DeviceOptions>` with `Names[]`. Before calling `_deviceRegistry.ReloadAsync(devices)`, it transforms `Names[]` into OID strings and stores them back. DeviceRegistry continues to read `poll.Names` (which now contains OIDs) and builds PollInfo with those strings.

This works because:
- PollOptions.Names is a `List<string>` -- it can hold anything
- The property name in JSON is "Names" (for operators)
- The property value at runtime (after resolution) is OIDs
- DeviceRegistry just reads the strings and passes them through

Wait, but then PollInfo.Oids would be populated from PollOptions.Names. The property is named `Oids` in PollInfo, populated from `Names` in PollOptions. This is correct -- Names are metric names in JSON, which resolve to OID strings that fill PollInfo.Oids.

```csharp
// DeviceWatcherService -- resolution before registry reload
private void ResolveMetricNames(List<DeviceOptions> devices)
{
    foreach (var device in devices)
    {
        foreach (var poll in device.Polls)
        {
            var resolvedOids = new List<string>();
            var unresolvedNames = new List<string>();

            foreach (var name in poll.Names)
            {
                var oid = _oidMapService.ResolveToOid(name);
                if (oid is not null)
                    resolvedOids.Add(oid);
                else
                    unresolvedNames.Add(name);
            }

            if (unresolvedNames.Count > 0)
            {
                foreach (var name in unresolvedNames)
                {
                    _logger.LogWarning(
                        "Metric name '{MetricName}' not found in OID map for device {DeviceName}",
                        name, device.Name);
                }
            }

            _logger.LogInformation(
                "{DeviceName} group {Interval}s: resolved {ResolvedCount}/{TotalCount}{UnresolvedDetail}",
                device.Name, poll.IntervalSeconds,
                resolvedOids.Count, poll.Names.Count,
                unresolvedNames.Count > 0
                    ? $", unresolved: {string.Join(", ", unresolvedNames)}"
                    : "");

            // Replace metric names with resolved OIDs
            poll.Names.Clear();
            poll.Names.AddRange(resolvedOids);
        }
    }
}
```

Then DeviceRegistry reads `poll.Names` (now containing OIDs) into `PollInfo.Oids`. This is slightly confusing internally but avoids changing DeviceRegistry's interface.

The alternative: Have DeviceRegistry read from a different property. Add `internal List<string> ResolvedOids` to PollOptions, and DeviceRegistry reads that instead. But this adds complexity.

**Final recommendation:** Mutate `poll.Names` in place with resolved OIDs. It's a fresh deserialized object per reload, and the property is just `List<string>`. DeviceRegistry.ReloadAsync reads `poll.Names` and puts them in `PollInfo(Oids: poll.Names.AsReadOnly())`. Comment the code clearly.

### PollInfo (renamed from MetricPollInfo)

```csharp
// Pipeline/PollInfo.cs (renamed from MetricPollInfo.cs)
public sealed record PollInfo(
    int PollIndex,
    IReadOnlyList<string> Oids, // Resolved OID strings (from Names via ResolveToOid)
    int IntervalSeconds,
    double TimeoutMultiplier = 0.8)
{
    public string JobKey(string configAddress, int port) => $"metric-poll-{configAddress}_{port}-{PollIndex}";
}
```

### New devices.json Format

```json
[
  {
    "Name": "OBP-01",
    "IpAddress": "127.0.0.1",
    "Port": 10161,
    "Polls": [
      {
        "IntervalSeconds": 10,
        "Names": [
          "obp_channel_L1",
          "obp_r1_power_L1",
          "obp_r2_power_L1",
          ...
        ]
      }
    ]
  }
]
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `MetricPolls[].Oids[]` with raw OID strings | `Polls[].Names[]` with human-readable metric names | Phase 31 | Config readability, operator UX |
| Direct OID-to-job mapping | Name resolution at config load time | Phase 31 | Extra resolution step, unresolvable name handling |

## Open Questions

1. **DeviceRegistry constructor path resolution**
   - What we know: The constructor takes `IOptions<DevicesOptions>` and builds from config at DI time. It currently reads `poll.Oids` directly. After rename, it would read `poll.Names`.
   - What's unclear: Should the constructor also resolve names, or is it only used at startup where the initial load from DeviceWatcherService will overwrite it anyway?
   - Recommendation: The constructor IS used at startup (before any ConfigMap watch). The startup config from appsettings.Development.json/devices.json is loaded through this path. It MUST resolve names. Inject IOidMapService into DeviceRegistry for the constructor path. DeviceWatcherService handles the reload path.
   - **Alternative:** Move initial load logic to use the same path as reload (DeviceWatcherService initial load already calls LoadFromConfigMapAsync which calls HandleConfigMapChangedAsync which calls ReloadAsync). If the constructor just starts empty and the initial load populates everything, the constructor doesn't need resolution. But this would require DeviceRegistry to start empty -- check if anything depends on it being populated at DI time.

2. **E2E unmapped OIDs test strategy**
   - What we know: `e2e-sim-unmapped-configmap.yaml` adds 2 extra OIDs (999.2.1.0, 999.2.2.0) not in the oidmap, used to test "Unknown" metric name in pipeline.
   - What's unclear: How should this fixture work with names? We need names that resolve AND names that DON'T resolve (to test unresolvable name behavior). But we also need OIDs that don't exist in the oidmap (for the wire-level Unknown test).
   - Recommendation: Use the 7 valid e2e metric names + 2 fake names like "e2e_unmapped_1", "e2e_unmapped_2". The fake names won't resolve, won't create poll jobs for those OIDs, and thus WON'T test the wire-level Unknown scenario. The E2E scenario for unknown OIDs may need a different approach (e.g., raw OIDs in the oidmap with no metric name -- but that's the forward map, not reverse). This needs careful analysis during planning.

## Sources

### Primary (HIGH confidence)
- Direct codebase analysis of all source files listed above
- `IOidMapService.ResolveToOid` signature and implementation verified in OidMapService.cs
- All JSON config files and K8s manifests examined directly

### Confidence breakdown
- C# model rename: HIGH -- straightforward mechanical rename, all files identified
- Resolution logic: HIGH -- IOidMapService.ResolveToOid exists, DeviceWatcherService injection point clear
- JSON config rewrite: HIGH -- complete OID-to-name mapping available from oidmaps.json
- E2E fixture rewrite: HIGH -- all fixtures examined
- Missing oidmap entries: HIGH -- verified by comparing local oidmaps.json vs K8s ConfigMaps

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - no new libraries, existing patterns
- Architecture: HIGH - clear injection point, well-understood flow
- Pitfalls: HIGH - identified from direct codebase analysis

**Research date:** 2026-03-13
**Valid until:** 2026-04-13 (stable domain, no external dependencies)
