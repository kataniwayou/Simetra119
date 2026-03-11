---
phase: Q051
plan: 01
subsystem: telemetry
tags: [metrics, pipeline, grafana, cleanup]
dependency-graph:
  requires: [Q048, Q050]
  provides: [pipeline-duration-removed]
  affects: []
tech-stack:
  added: []
  patterns: []
key-files:
  created: []
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs
    - deploy/grafana/dashboards/simetra-operations.json
  deleted:
    - src/SnmpCollector/Pipeline/Behaviors/TimingBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TimingBehaviorTests.cs
decisions: []
metrics:
  duration: ~3 minutes
  completed: 2026-03-11
---

# Quick Task 051: Remove Pipeline Duration Metric Summary

**One-liner:** Removed snmp.pipeline.duration histogram, TimingBehavior, RecordPipelineDuration, and Pipeline Duration P99 Grafana panel -- noise metric measuring only in-memory behavior chain (~2-5ms)

## What Was Done

### Task 1: Delete TimingBehavior and its tests
- **Commit:** `4d4ae4e`
- Deleted `src/SnmpCollector/Pipeline/Behaviors/TimingBehavior.cs` (outermost MediatR behavior)
- Deleted `tests/SnmpCollector.Tests/Pipeline/Behaviors/TimingBehaviorTests.cs` (3 unit tests)

### Task 2: Remove all pipeline duration references
- **Commit:** `53d5184`
- Removed `TimingBehavior<,>` registration from `AddSnmpPipeline()` in ServiceCollectionExtensions.cs
- Renumbered remaining behaviors from 0-5 (LoggingBehavior now outermost at 0th)
- Removed `_pipelineDuration` histogram field, creation, and `RecordPipelineDuration()` method from PipelineMetricService.cs
- Removed Pipeline Duration P99 panel from simetra-operations.json dashboard
- Validated JSON with `python -m json.tool`

## Verification

- Build: 0 errors, 0 warnings
- Tests: 204 passed, 0 failed
- Dashboard JSON: valid
- Grep for `pipeline.duration`, `TimingBehavior`, `RecordPipelineDuration` across src/ and tests/: zero matches

## Deviations from Plan

None -- plan executed exactly as written.

## Pipeline Behavior Order (After)

| Position | Behavior | Purpose |
|----------|----------|---------|
| 0 (outermost) | LoggingBehavior | Logs entry/exit |
| 1 | ExceptionBehavior | Catches unhandled exceptions |
| 2 | ValidationBehavior | Validates message and device |
| 3 | OidResolutionBehavior | Resolves OID to metric name |
| 4 | ValueExtractionBehavior | Extracts numeric/string value |
| 5 | TenantVectorFanOutBehavior | Routes to tenant vector slots |

## What Still Exists (Untouched)

- `Stopwatch` + `PollDurationMs` in MetricPollJob.cs (feeds snmp_gauge_duration/snmp_info_duration)
- `RecordGaugeDuration()` / `RecordInfoDuration()` in SnmpMetricFactory.cs
- Business duration histogram panels in Grafana dashboard
