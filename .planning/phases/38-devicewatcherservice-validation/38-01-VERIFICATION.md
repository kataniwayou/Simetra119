---
phase: 38-devicewatcherservice-validation
verified: 2026-03-15T09:20:28Z
status: passed
score: 8/8 must-haves verified
---

# Phase 38: DeviceWatcherService Validation Verification Report

**Phase Goal:** Combined metric definitions validated at load time — invalid aggregator values rejected, co-presence enforced, name collisions caught, valid definitions stored on MetricPollInfo.
**Verified:** 2026-03-15T09:20:28Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | A poll group with Aggregator 'invalid' produces a structured Error log — combined metric skipped, individual OID polling still loads | VERIFIED | `CombinedMetric_InvalidAggregator_LogsErrorAndSkipsCombinedMetric` passes; `CombinedMetric_InvalidCombinedMetric_PollGroupStillLoadsIndividualOids` passes (Oids.Count == 2, AggregatedMetrics empty) |
| 2 | A poll group with AggregatedMetricName set but no Aggregator (or vice versa) produces an Error log — partial config never silently accepted | VERIFIED | `CombinedMetric_MissingAggregator_LogsErrorAndSkipsCombinedMetric` and `CombinedMetric_MissingName_LogsErrorAndSkipsCombinedMetric` both pass; co-presence check at DeviceWatcherService.cs:358-363 |
| 3 | A poll group with fewer than 2 resolved OIDs produces an Error log — minimum-2 check uses resolvedOids.Count | VERIFIED | `CombinedMetric_FewerThan2ResolvedOids_LogsErrorAndSkipsCombinedMetric` passes; DeviceWatcherService.cs:372 uses `resolvedOids.Count < 2` (not MetricNames.Count) |
| 4 | Two poll groups on the same device with the same AggregatedMetricName: first loads, second produces Error and is skipped | VERIFIED | `CombinedMetric_DuplicateNameOnSameDevice_LogsErrorAndSkipsSecond` passes; seenAggregatedNames HashSet at line 317, scoped inside BuildPollGroups per-device |
| 5 | An AggregatedMetricName that matches an existing OID map entry produces an Error log — combined metric skipped (real metric takes priority) | VERIFIED | `CombinedMetric_OidMapCollision_LogsErrorAndSkipsCombinedMetric` passes; DeviceWatcherService.cs:386 calls `oidMapService.ContainsMetricName` and logs at LogLevel.Error (not Warning) |
| 6 | A fully valid combined metric definition populates CombinedMetricDefinition on MetricPollInfo with correct MetricName, Kind, and SourceOids | VERIFIED | `CombinedMetric_ValidConfig_PopulatesAggregatedMetrics` passes; asserts MetricName == "combined_power", Kind == Sum, SourceOids.Count == 2 |
| 7 | All Error logs include DeviceName, PollGroupIndex, AggregatedMetricName, and Reason in the structured message template | VERIFIED | All 5 error paths use the identical template: `"Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric"` (DeviceWatcherService.cs lines 361, 368, 375, 382, 389) |
| 8 | Invalid combined metric config only skips the combined metric definition — poll group still loads for individual OID polling | VERIFIED | `result.Add(new MetricPollInfo(...))` at line 402 is unconditional — no `continue` follows any validation failure branch; `CombinedMetric_NeitherFieldSet_NoCombinedMetricNoError` confirms zero-error path |

**Score:** 8/8 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/SnmpCollector/Services/DeviceWatcherService.cs` | Combined metric validation block in BuildPollGroups with 5 validation rules; contains `seenAggregatedNames` | VERIFIED | 433 lines; `seenAggregatedNames` HashSet at line 317; 5-rule else-if chain at lines 358–399; `result.Add` unconditional at line 402 |
| `tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` | Unit tests for all 5 combined metric validation rules plus happy path; contains `AggregatedMetricName` | VERIFIED | 807 lines; 10 new Phase 38 tests (lines 519–806) plus 12 pre-existing tests; all reference `AggregatedMetricName` |
| `src/SnmpCollector/Pipeline/MetricPollInfo.cs` | `AggregatedMetrics` init property | VERIFIED | `IReadOnlyList<CombinedMetricDefinition> AggregatedMetrics { get; init; } = []` at line 21 |
| `src/SnmpCollector/Pipeline/CombinedMetricDefinition.cs` | Record with MetricName, Kind, SourceOids | VERIFIED | Sealed record with all 3 constructor parameters |
| `src/SnmpCollector/Pipeline/AggregationKind.cs` | 4 enum values: Sum, Subtract, AbsDiff, Mean | VERIFIED | All 4 values present |
| `src/SnmpCollector/Configuration/PollOptions.cs` | `AggregatedMetricName` and `Aggregator` nullable string properties | VERIFIED | Both nullable string properties present at lines 31 and 37 |
| `src/SnmpCollector/Pipeline/IOidMapService.cs` | `ContainsMetricName` method | VERIFIED | `bool ContainsMetricName(string metricName)` at line 28; implementation in OidMapService.cs |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `PollOptions.AggregatedMetricName + PollOptions.Aggregator` | `CombinedMetricDefinition on MetricPollInfo.AggregatedMetrics` | `BuildPollGroups` combined metric validation block | WIRED | Validate → build `CombinedMetricDefinition` → set via `AggregatedMetrics = combinedMetric is not null ? [combinedMetric] : []` at line 408 |
| `IOidMapService.ContainsMetricName` | OID map name collision check | `BuildPollGroups` calls `ContainsMetricName` for each `AggregatedMetricName` | WIRED | Line 386: `oidMapService.ContainsMetricName(poll.AggregatedMetricName!)` → Error log → `combinedMetric` remains null |

### Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| CM-02 (co-presence enforcement) | SATISFIED | Lines 358–363: `!hasName \|\| !hasAggregator` guard with Error log |
| CM-03 (Aggregator enum parse) | SATISFIED | Lines 365–370: `Enum.TryParse<AggregationKind>` with Error on parse failure |
| CM-11 (min 2 resolved OIDs) | SATISFIED | Lines 372–377: `resolvedOids.Count < 2` with Error log |
| CM-12 (OID map collision = Error) | SATISFIED | Lines 386–391: `ContainsMetricName` check with `LogError` (confirmed not Warning) |

### Anti-Patterns Found

None. No TODO/FIXME/placeholder patterns, no empty handlers, no stub returns in the modified files.

### Human Verification Required

None. All validation logic is fully structural and verifiable programmatically. The 28 DeviceWatcherValidationTests tests and the full 312-test suite all pass with no failures.

---

## Verification Detail Notes

**5 validation rules confirmed in order:**
1. Co-presence (line 358): `!hasName || !hasAggregator`
2. Aggregator enum parse (line 365): `!Enum.TryParse<AggregationKind>(...)`
3. Min 2 resolved OIDs (line 372): `resolvedOids.Count < 2` — uses resolved count, not `MetricNames.Count`
4. Per-device duplicate name (line 379): `!seenAggregatedNames.Add(...)` — HashSet scoped per BuildPollGroups call (per-device)
5. OID map collision (line 386): `oidMapService.ContainsMetricName(...)` — produces Error (not Warning)

**Per-entry skip pattern confirmed:** The `result.Add(new MetricPollInfo(...))` at line 402 is unconditional — no `continue` statement follows any of the 5 failure branches. `AggregatedMetrics` is set to empty list `[]` on any failure, preserving individual OID polling.

**Structured log template identical across all 5 error branches:** `"Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric"`

**Test count:** 12 pre-existing + 10 new Phase 38 tests = 22 tests in `DeviceWatcherValidationTests`; dotnet reports 28 (4 Theory InlineData cases expand the count). All 312 suite-wide tests pass.

---

_Verified: 2026-03-15T09:20:28Z_
_Verifier: Claude (gsd-verifier)_
