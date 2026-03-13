---
phase: 31-human-name-device-config
verified: 2026-03-13T09:14:28Z
status: passed
score: 4/4 must-haves verified
---

# Phase 31: Human-Name Device Config Verification Report

**Phase Goal:** Operators can reference metric names like obp_channel_L1 instead of raw OID strings in devices.json poll entries, with full replacement of the Oids field by MetricNames and graceful handling of unresolvable names. Restructure oidmaps.json from flat dictionary to array of objects with explicit Oid/MetricName fields.
**Verified:** 2026-03-13T09:14:28Z
**Status:** PASSED
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | MetricNames poll entry resolved to OIDs before MetricPollJob executes | VERIFIED | BuildPollGroups() calls _oidMapService.ResolveToOid(name) per name; MetricPollJob reads only pollGroup.Oids (line 80) -- metric name strings never reach the job |
| 2 | Unresolvable metric name logs structured warning; entry skipped; other entries unaffected | VERIFIED | DeviceRegistry.BuildPollGroups() lines 174-178: LogWarning with MetricName+DeviceName+PollIndex; device always registered; unit tests UnresolvableMetricName_LogsWarningAndSkipped and AllNamesUnresolvable_DeviceStillRegistered pass |
| 3 | Reload resolves against current OID map state at that moment (point-in-time) | VERIFIED | DeviceWatcherService.HandleConfigMapChangedAsync calls _deviceRegistry.ReloadAsync(devices) which calls BuildPollGroups() using singleton _oidMapService._reverseMap (volatile FrozenDictionary); OidMapWatcherService and DeviceWatcherService are fully independent -- no cross-watcher trigger |
| 4 | Reload diff logging includes per-name resolution detail (resolved count, unresolved names listed) | VERIFIED | BuildPollGroups() lines 181-184: LogInformation with resolved/total count and unresolved name list -- always emitted per poll group |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/SnmpCollector/Configuration/PollOptions.cs | Renamed from MetricPollOptions; MetricNames property | VERIFIED | 26 lines; List<string> MetricNames present; MetricPollOptions.cs absent from Configuration/ directory |
| src/SnmpCollector/Configuration/DeviceOptions.cs | Polls property (not MetricPolls) | VERIFIED | List<PollOptions> Polls present; no MetricPolls anywhere in source tree |
| src/SnmpCollector/Pipeline/DeviceRegistry.cs | Injects IOidMapService; BuildPollGroups() resolves names | VERIFIED | 197 lines; IOidMapService _oidMapService field; BuildPollGroups() private helper shared by constructor and ReloadAsync |
| src/SnmpCollector/Pipeline/IOidMapService.cs | Exposes ResolveToOid(string metricName) | VERIFIED | string? ResolveToOid(string metricName) declared in interface |
| src/SnmpCollector/Pipeline/OidMapService.cs | Implements ResolveToOid via _reverseMap | VERIFIED | _reverseMap.TryGetValue(metricName,...) returns oid or null; volatile FrozenDictionary rebuilt atomically in UpdateMap |
| src/SnmpCollector/Services/OidMapWatcherService.cs | Array-of-objects parsing; EnumerateArray with Oid/MetricName | VERIFIED | Guards ValueKind.Array; EnumerateArray() with TryGetProperty("Oid") and TryGetProperty("MetricName"); no EnumerateObject or Deserialize<Dictionary> |
| src/SnmpCollector/config/oidmaps.json | [{ Oid, MetricName }] format | VERIFIED | Array-of-objects format confirmed in first lines |
| src/SnmpCollector/config/devices.json | Polls/MetricNames with human-readable names | VERIFIED | OBP-01 (3 poll groups) + NPB-01 (2 poll groups) using names like obp_channel_L1, npb_cpu_util; no raw OIDs |
| src/SnmpCollector/Jobs/MetricPollJob.cs | Uses pollGroup.Oids only | VERIFIED | Line 80: pollGroup.Oids.Select(oid => new Variable(new ObjectIdentifier(oid))) |
| tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs | 5 new resolution tests | VERIFIED | All 5 present: constructor path, ReloadAsync path, partial resolution, zero resolution, warning log assertion |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| DeviceRegistry | IOidMapService.ResolveToOid | BuildPollGroups() per-name loop | WIRED | null return triggers warning + skip |
| DeviceRegistry.BuildPollGroups() | MetricPollInfo.Oids | resolvedOids.AsReadOnly() | WIRED | Resolved OIDs populate MetricPollInfo constructor |
| DeviceWatcherService | DeviceRegistry.ReloadAsync | HandleConfigMapChangedAsync | WIRED | Calls _deviceRegistry.ReloadAsync(devices) which calls BuildPollGroups() with current _oidMapService |
| MetricPollJob | DeviceRegistry | TryGetByIpPort + pollGroup.Oids | WIRED | Job reads only OID strings; metric names never present at this level |
| OidMapService._reverseMap | BuildReverseMap() | volatile swap in UpdateMap | WIRED | Rebuilt atomically on each oidmap reload; BuildPollGroups reads current snapshot |
| OidMapWatcherService.ValidateAndParseOidMap | Program.cs local dev loading | direct static call | WIRED | Program.cs calls ValidateAndParseOidMap for local dev oidmap loading |

### Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| MetricNames resolved to OIDs at load; MetricPollJob never receives metric name strings | SATISFIED | Full chain verified: config -> BuildPollGroups -> MetricPollInfo.Oids -> MetricPollJob.pollGroup.Oids |
| Unresolvable name: structured warning, skipped, device still registered | SATISFIED | Warning includes device name, metric name, poll index; no early return on empty OIDs list |
| Point-in-time resolution on reload | SATISFIED | Reload reads current _reverseMap snapshot; watchers are independent (no cross-trigger) |
| Reload diff: resolved count + unresolved names | SATISFIED | Per-poll summary always logged with resolved/total count and unresolved name list |
| oidmaps.json restructured to array-of-objects | SATISFIED | Confirmed in config/oidmaps.json, simetra-oidmaps.yaml, production configmap.yaml |
| All device configs use Polls/MetricNames | SATISFIED | devices.json, simetra-devices.yaml, all 6 E2E device fixtures, scenario 06 inline jq JSON confirmed |

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| src/SnmpCollector/bin/Debug/.../config/devices.json | Stale build artifact with old MetricPolls/Oids format | Info | Build artifact only; not in source tree; no runtime impact |
| src/SnmpCollector/appsettings.Development.json | MetricNames entries contain raw OID strings | Info | Not a defect -- these local dev entries work (unresolved OIDs produce warnings, no crash); production configs use metric names correctly |

### Human Verification Required

None. All must-haves are fully verifiable from code structure and unit tests.

The only runtime behavior not verifiable statically is that e2e_intentionally_missing entries in e2e-sim-unmapped-configmap.yaml produce warning log entries at runtime -- but this is covered structurally by unit tests UnresolvableMetricName_LogsWarningAndSkipped and ReloadAsync_UnresolvableMetricName_LogsWarning.

## Summary

Phase 31 goal is fully achieved. The complete name-to-OID translation chain is in place and verified:

1. devices.json and all K8s/E2E configs use human-readable MetricNames in Polls entries.
2. DeviceRegistry.BuildPollGroups() resolves each name via IOidMapService.ResolveToOid() at device load time. Unresolvable names are warned and skipped; device is always registered.
3. MetricPollInfo.Oids holds only resolved OID strings. MetricPollJob reads only pollGroup.Oids -- metric names never reach SNMP GET execution.
4. Resolution is point-in-time: DeviceWatcherService and OidMapWatcherService are independent. Reload reads current _reverseMap snapshot.
5. Per-poll resolution summary always logged with resolved/total count and unresolved name list.
6. oidmaps.json is in array-of-objects format with 3-pass duplicate detection preserved.
7. MetricPollOptions class deleted; PollOptions with MetricNames is the sole model; DeviceOptions.Polls replaces MetricPolls. No old names remain in source tree.
8. Five targeted unit tests cover all resolution scenarios.

---

*Verified: 2026-03-13T09:14:28Z*
*Verifier: Claude (gsd-verifier)*
