---
phase: Q049
plan: 01
subsystem: telemetry
tags: [histogram, otel, grafana, duration, snmp]
dependency-graph:
  requires: [Q048]
  provides: [snmp_gauge_duration histogram, snmp_info_duration histogram, P99 Grafana columns]
  affects: []
tech-stack:
  added: []
  patterns: [per-OID poll duration histograms with full label parity]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/SnmpOidReceived.cs
    - src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs
    - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
    - src/SnmpCollector/Jobs/MetricPollJob.cs
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    - tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs
    - tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
    - deploy/grafana/dashboards/simetra-business.json
decisions: []
metrics:
  duration: ~5 min
  completed: 2026-03-11
---

# Quick Task 049: Gauge/Info Duration Histograms Summary

**One-liner:** Per-OID poll duration histograms (snmp_gauge_duration, snmp_info_duration) with full label parity and P99 Grafana columns on the business dashboard.

## What Was Done

### Task 1: Add PollDurationMs to pipeline and create histogram instruments
- Added `PollDurationMs` nullable double property to `SnmpOidReceived`
- Added `RecordGaugeDuration` and `RecordInfoDuration` methods to `ISnmpMetricFactory`
- Implemented histogram instruments (`snmp_gauge_duration` with unit "ms", `snmp_info_duration` with unit "ms") on the LeaderMeterName meter in `SnmpMetricFactory`
- Added `GetOrCreateHistogram` private helper alongside existing `GetOrCreateGauge`
- Updated `MetricPollJob.DispatchResponseAsync` to accept `pollDurationMs` parameter and set it on each varbind from `sw.Elapsed.TotalMilliseconds`
- Updated `OtelMetricHandler` to conditionally record duration when `PollDurationMs.HasValue` after each gauge/info recording

### Task 2: Update tests for duration recording
- Added `GaugeDurationRecords` and `InfoDurationRecords` lists to `TestSnmpMetricFactory`
- Added `RecordGaugeDuration_IncludesAllSixLabels` and `RecordInfoDuration_IncludesAllSevenLabels` tests to `SnmpMetricFactoryTests`
- Added 4 new OtelMetricHandler tests: duration recorded with poll, duration recorded for info, skipped when null, skipped for traps
- Updated `MakeNotification` helper with optional `pollDurationMs` and `source` parameters
- Updated `CapturingSnmpMetricFactory` and `ThrowingSnmpMetricFactory` for new interface methods
- All 204 tests pass

### Task 3: Add P99 duration columns to Grafana business dashboard
- Added `histogram_quantile(0.99, ...)` query (refId C) to Gauge Metrics table using `snmp_gauge_duration_milliseconds_bucket`
- Added `histogram_quantile(0.99, ...)` query (refId B) to Info Metrics table using `snmp_info_duration_milliseconds_bucket`
- Added "P99 (ms)" field overrides with unit=ms, decimals=1, width=90 for both tables
- Switched Info table from `filterFieldsByName` to `merge` transform to support multi-query join
- Dashboard JSON validates cleanly

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed ThrowingSnmpMetricFactory in PipelineIntegrationTests**
- **Found during:** Task 2
- **Issue:** `ThrowingSnmpMetricFactory` did not implement the two new `ISnmpMetricFactory` methods, blocking test compilation
- **Fix:** Added throwing implementations for `RecordGaugeDuration` and `RecordInfoDuration`
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs`

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 0c2823c | feat(Q049): add PollDurationMs property and gauge/info duration histograms |
| 2 | 0a9fb06 | test(Q049): add duration histogram tests and update test factories |
| 3 | 9459222 | feat(Q049): add P99 duration columns to Grafana business dashboard |
