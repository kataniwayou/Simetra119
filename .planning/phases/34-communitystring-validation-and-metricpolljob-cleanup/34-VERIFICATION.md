---
phase: 34-communitystring-validation-and-metricpolljob-cleanup
verified: 2026-03-14T00:00:00Z
status: passed
score: 19/19 must-haves verified
gaps: []
---

# Phase 34: CommunityString Validation and MetricPollJob Cleanup Verification Report

**Phase Goal:** Every CommunityString value in every config layer -- devices, tenant metrics, tenant commands -- is validated at load time against the Simetra.* pattern. Invalid entries are skipped with structured Error logs. Duplicate device names caught. Empty poll groups and the MetricPollJob CommunityString fallback are removed.
**Verified:** 2026-03-14
**Status:** passed
**Re-verification:** No -- initial verification

---

## Plan 01 Must-Haves

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Duplicate IP+Port in DeviceRegistry constructor and ReloadAsync logs Error and skips second device | VERIFIED | DeviceRegistry.cs lines 76-82 (constructor) and 151-157 (ReloadAsync): LogError + continue; no throw |
| 2 | Duplicate CommunityString across devices with different IP+Port logs Warning but both devices load normally (DEV-10) | VERIFIED | DeviceRegistry.cs lines 84-90 and 159-165: LogWarning with both loaded message; no continue |
| 3 | Poll group where all MetricNames resolve to zero OIDs is excluded from PollGroups; device still registers (DEV-08) | VERIFIED | BuildPollGroups lines 226-232: resolvedOids.Count == 0 triggers LogWarning and continue; device still added |
| 4 | BuildPollGroups returns only poll groups with at least one resolved OID; zero-OID groups log Warning | VERIFIED | Warning at line 228 contains deviceName and poll index |
| 5 | Constructor_DuplicateIpPort test updated to assert Error log + skip behavior | VERIFIED | DeviceRegistryTests.cs line 139: renamed SkipsSecondDeviceWithErrorLog; asserts Assert.Single and logger.Received(1).Log(LogLevel.Error) |
| 6 | All existing DeviceRegistryTests pass with new skip-based handling | VERIFIED | All original test names present; no Assert.Throws for duplicate IP+Port remains |
| 7 | ServiceCollectionExtensions.cs contains XML doc for config ordering (CS-07) | VERIFIED | Lines 362-381: remarks block in AddSnmpPipeline with numbered list: oidmaps/commandmaps, Devices, Tenants |

**Score:** 7/7 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/SnmpCollector/Pipeline/DeviceRegistry.cs | Skip-based duplicate IP+Port, CS Warning, zero-OID filtering | VERIFIED | 244 lines; seenCommunityStrings in constructor (line 47) and ReloadAsync (line 122); BuildPollGroups filters zero-OID groups |
| tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs | Updated tests for DEV-10 and DEV-08 | VERIFIED | 735 lines; DuplicateCommunityString, ZeroResolvedOids, MixedPollGroups, and ReloadAsync_Duplicate tests present |
| src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs | XML doc with config ordering for CS-07 | VERIFIED | Lines 362-381: remarks block with ordered list present in AddSnmpPipeline |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| DeviceRegistry constructor | DeviceRegistry.ReloadAsync | Symmetric seenCommunityStrings + skip-based IP+Port logic | VERIFIED | Both paths (lines 47/76-90 and 122/151-165) are symmetric |
| BuildPollGroups | DynamicPollScheduler.ReconcileAsync | Zero-OID poll groups excluded from PollGroups | VERIFIED | resolvedOids.Count == 0 guard at line 226; device PollGroups will not contain zero-OID entries |

---

## Plan 02 Must-Haves

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | TenantVectorRegistry constructor takes IOidMapService as parameter | VERIFIED | TenantVectorRegistry.cs lines 27-35: constructor takes IOidMapService; stored as _oidMapService |
| 2 | ServiceCollectionExtensions DI wiring passes IOidMapService to TenantVectorRegistry | VERIFIED | ServiceCollectionExtensions.cs lines 308-312: sp.GetRequiredService<IOidMapService>() as second argument |
| 3 | Tenant metric entry with invalid Role skipped with Error log | VERIFIED | TenantVectorRegistry.cs lines 141-147: Role != Evaluate and != Resolved triggers LogError and continue |
| 4 | Tenant metric entry whose MetricName is not in OID map skipped with Error log (TEN-05) | VERIFIED | Lines 150-156: ContainsMetricName returns false triggers LogError with TEN-05 and continue |
| 5 | Tenant metric/command entry whose IP+Port has no matching device skipped with Error log (TEN-07) | VERIFIED | Lines 159-165 (metrics) and 247-253 (commands): TryGetByIpPort false triggers LogError with TEN-07 and continue |
| 6 | Tenant command entry with invalid ValueType skipped with Error log (TEN-03) | VERIFIED | Lines 229-235: ValueType not in {Integer32, IpAddress, OctetString} triggers LogError with TEN-03 and continue |
| 7 | Tenant command entry with empty Value skipped with Error log | VERIFIED | Lines 238-244: IsNullOrWhiteSpace(cmd.Value) triggers LogError (Value is empty) and continue |
| 8 | Structural validation: empty Ip, port out of range, empty MetricName/CommandName all skip with Error log | VERIFIED | Metric guards at lines 114-138, command guards at lines 201-227; all six structural checks present |
| 9 | TEN-13 post-validation gate: tenant skipped if no Resolved OR no Evaluate OR no valid commands | VERIFIED | Lines 259-270: missing list populated per absent category; LogError with joined reason; continue |
| 10 | Per-entry skip semantics: one invalid entry does not affect sibling entries | VERIFIED | Each validation check uses continue within its loop body only |
| 11 | All existing TenantVectorRegistryTests pass with IOidMapService stub added | VERIFIED | CreateRegistry() at lines 17-24 uses CreatePassthroughOidMapService(); all pre-existing test bodies retained |
| 12 | IOidMapService deliberately re-added to TenantVectorRegistry for TEN-05 | VERIFIED | _oidMapService field (line 18), constructor parameter (line 29), ContainsMetricName call (line 150) |

**Score:** 12/12 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/SnmpCollector/Pipeline/TenantVectorRegistry.cs | IOidMapService dependency, per-entry validation, TEN-13 gate | VERIFIED | 354 lines; _oidMapService field; ContainsMetricName at line 150; full validation loops; TEN-13 gate at lines 259-270 |
| src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs | Updated DI wiring passing IOidMapService | VERIFIED | Lines 308-312: factory lambda with IOidMapService as second argument |
| tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs | IOidMapService stub and 12 new validation tests | VERIFIED | 1228 lines; CreatePassthroughOidMapService() at lines 42-47; 12 validation tests in section 12 starting at line 679 |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| TenantVectorRegistry constructor | ServiceCollectionExtensions DI wiring | sp.GetRequiredService<IOidMapService>() | VERIFIED | Lines 308-312 match constructor signature |
| TenantVectorRegistry.Reload metric loop | IOidMapService.ContainsMetricName | MetricName checked before acceptance | VERIFIED | Line 150: !ContainsMetricName with Error+continue wired |
| TenantVectorRegistry.Reload command loop | Post-validation TEN-13 gate | commandCount must be > 0 | VERIFIED | commandCount incremented at line 255; checked at line 262 |

---

## Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| CS-03 | SATISFIED | Per-entry skip semantics on CommunityString validation in DeviceRegistry and TenantVectorRegistry |
| CS-04 | SATISFIED | Invalid entries skip with structured Error logs in both registries |
| CS-07 | SATISFIED | XML doc remarks on AddSnmpPipeline documents oidmaps/commandmaps -> devices -> tenants ordering |
| DEV-08 | SATISFIED | Zero-OID poll groups filtered from BuildPollGroups; device still registers; Warning logged |
| DEV-09 | SATISFIED | CommunityString validation via CommunityStringHelper in DeviceRegistry constructor and ReloadAsync |
| DEV-10 | SATISFIED | Duplicate CommunityString with different IP+Port logs Warning; both devices load |
| TEN-03 | SATISFIED | Command ValueType must be Integer32/IpAddress/OctetString; else Error+skip |
| TEN-05 | SATISFIED | Metric MetricName checked via IOidMapService.ContainsMetricName; else Error+skip |
| TEN-07 | SATISFIED | Metric and command IP+Port checked via IDeviceRegistry.TryGetByIpPort; else Error+skip |
| TEN-08 | OUT OF SCOPE | Mapped to Phase 35 per traceability table |
| TEN-11 | SATISFIED | All skip logs use structured fields: TenantId, entry index, invalid value, reason |
| TEN-13 | SATISFIED | Post-validation completeness gate: tenant skipped if resolvedCount==0 OR evaluateCount==0 OR commandCount==0 |

---

## Anti-Patterns Found

None. No TODO/FIXME/placeholder patterns or empty-return stubs found in modified files.

---

## Human Verification Required

None. All must-haves are structurally verifiable and covered by the test suite.

---

## Gaps Summary

No gaps. All 19 must-haves (7 from Plan 01, 12 from Plan 02) are verified at all three levels.

Key observations:
- DeviceRegistry constructor and ReloadAsync are exactly symmetric: seenCommunityStrings in both, skip+Error for duplicate IP+Port in both, Warning-only for duplicate CommunityString in both.
- TenantVectorRegistry.Reload has complete validation for all structural and semantic checks for metrics and commands, followed by the TEN-13 completeness gate.
- DI factory lambda in ServiceCollectionExtensions correctly passes IOidMapService as second parameter matching the new constructor signature.
- AddSnmpPipeline has the CS-07 remarks block with the numbered config-ordering list.
- Test suite covers all new behaviors; existing tests updated for TEN-13 requirement.

---

_Verified: 2026-03-14_
_Verifier: Claude (gsd-verifier)_