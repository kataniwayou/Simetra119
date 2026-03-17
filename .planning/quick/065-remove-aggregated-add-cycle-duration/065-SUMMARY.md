# Quick 065: Remove snmp.aggregated.computed + Add snmp.snapshot.cycle_duration_ms

**One-liner:** Removed unused aggregated.computed counter from pipeline metric surface; added SnapshotJob cycle duration histogram for operational visibility.

## Changes Made

### Change 1: Remove snmp.aggregated.computed

| File | Change |
|------|--------|
| PipelineMetricService.cs | Removed `_aggregatedComputed` Counter field, constructor registration, `IncrementAggregatedComputed` method; updated XML doc count to 14 counters + 1 histogram |
| MetricPollJob.cs | Removed `IncrementAggregatedComputed` call after synthetic dispatch in `DispatchAggregatedMetricAsync` |
| MetricPollJobTests.cs | Removed Test 18 (`Execute_AggregatedMetrics_Success_IncrementsAggregatedComputedCounter`), removed `CountAggregatedComputed` helper, removed assertion in Test 19 |
| simetra-operations.json | Replaced "Aggregated Computed" panel with "Snapshot Cycle Duration" at same grid position (x=12, y=31, w=12) |

### Change 2: Add snmp.snapshot.cycle_duration_ms

| File | Change |
|------|--------|
| PipelineMetricService.cs | Added `Histogram<double> _snapshotCycleDuration` field, registered with `_meter.CreateHistogram<double>("snmp.snapshot.cycle_duration_ms", "ms", ...)`, added `RecordSnapshotCycleDuration(double durationMs)` public method |
| SnapshotJob.cs | Added `System.Diagnostics` using, `Stopwatch.StartNew()` at Execute start, `sw.Stop()` + `RecordSnapshotCycleDuration` after priority group loop, duration included in cycle summary Debug log |
| simetra-operations.json | New "Snapshot Cycle Duration" panel with `histogram_quantile(0.99, ...)` PromQL, unit=ms |
| PipelineMetricServiceTests.cs | Added double measurement callback, added `RecordSnapshotCycleDuration_RecordsHistogramWithNoTags` test |
| SnapshotJobTests.cs | Added MeterListener with double callback, added `Execute_RecordsSnapshotCycleDuration` integration test |

## Test Results

- Build: 0 warnings, 0 errors
- Tests: 416 passed, 0 failed (net -1 removed, +2 added from 417 baseline)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed stale CountAggregatedComputed assertion in MetricPollJobTests Test 19**
- **Found during:** Change 1 removal
- **Issue:** Test 19 (`Execute_AggregatedMetrics_Exception_DoesNotRecordFailure`) asserted `CountAggregatedComputed() == 0` which references the removed metric
- **Fix:** Removed the assertion and helper method; test still validates no unreachability failure recorded
- **Files modified:** MetricPollJobTests.cs
- **Commit:** 45a14db

## Commits

| Hash | Message |
|------|---------|
| 45a14db | feat(065): remove snmp.aggregated.computed counter, add snmp.snapshot.cycle_duration_ms histogram |
