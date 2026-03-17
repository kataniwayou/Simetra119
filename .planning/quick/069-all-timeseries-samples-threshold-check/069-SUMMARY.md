---
phase: quick
plan: 069
subsystem: snapshot-evaluation
tags: [threshold, time-series, evaluate, snapshot-job]

dependency-graph:
  requires: [quick-068]
  provides: [all-samples-evaluate-threshold-check]
  affects: []

tech-stack:
  added: []
  patterns: [full-series-threshold-evaluation]

file-tracking:
  key-files:
    created: []
    modified:
      - src/SnmpCollector/Jobs/SnapshotJob.cs
      - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs

decisions:
  - id: "069-D1"
    choice: "Iterate all samples via ReadSeries() for Evaluate holders only"
    reason: "Resolved metrics intentionally check only latest sample (Tier 2 gate semantics unchanged)"

metrics:
  duration: "~4 min"
  completed: "2026-03-17"
---

# Quick Task 069: All Time Series Samples Threshold Check Summary

**One-liner:** AreAllEvaluateViolated now requires every sample in the time series to violate threshold, preventing false Tier 4 commands from transient spikes.

## What Changed

### Task 1: Update AreAllEvaluateViolated to check all time series samples
**Commit:** `4115cb7`

- Replaced `holder.ReadSlot()` with `holder.ReadSeries()` in `AreAllEvaluateViolated`
- Empty series (Length == 0) skips the holder (same as previous null-slot behavior)
- Each sample in the series is checked via `IsViolated(holder, sample)`
- If ANY sample is NOT violated, the holder is not violated (return false immediately)
- Only if ALL samples are violated does the holder count toward `checkedCount`
- `AreAllResolvedViolated` remains unchanged (still uses `ReadSlot()` for latest sample only)
- Updated XML doc comment to reflect all-samples semantics

### Task 2: Add tests for all-samples threshold check
**Commit:** `62a24e3`

- Added `timeSeriesSize` parameter to `MakeHolder` helper (default 1, backward compatible)
- **Execute_EvaluateAllSeriesSamplesViolated_ProceedsToTier4**: 3-sample series all below Min=10 triggers Tier 4
- **Execute_EvaluateOneSeriesSampleInRange_Healthy**: 2 violated + 1 in-range sample prevents Tier 4
- **Execute_EvaluatePartialSeriesFill_AllViolated_ProceedsToTier4**: timeSeriesSize=5 with only 3 present samples all violated triggers Tier 4
- All 427 tests pass (424 existing + 3 new)

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| 069-D1 | Iterate full series for Evaluate only | Resolved metrics gate (Tier 2) intentionally checks latest sample to detect confirmed-bad devices; changing it would alter staleness/gate semantics |

## Deviations from Plan

None -- plan executed exactly as written.

## Verification

- `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- succeeds
- `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- 427 passed, 0 failed
- `ReadSeries` confirmed in `AreAllEvaluateViolated`
- `ReadSlot` confirmed in `AreAllResolvedViolated` (unchanged)
