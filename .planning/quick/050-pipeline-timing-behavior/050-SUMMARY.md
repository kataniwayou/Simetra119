---
phase: quick
plan: "050"
subsystem: telemetry
tags: [pipeline, timing, behavior, histogram, mediatr]
dependency_graph:
  requires: [Q048]
  provides: [TimingBehavior as outermost pipeline behavior for snmp.pipeline.duration]
  affects: []
tech_stack:
  added: []
  patterns: [outermost-behavior-timing]
key_files:
  created:
    - src/SnmpCollector/Pipeline/Behaviors/TimingBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TimingBehaviorTests.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Jobs/MetricPollJob.cs
decisions:
  - id: Q050-D1
    description: "TimingBehavior registered as 0th (outermost) behavior, wrapping ExceptionBehavior so pipeline exceptions are still timed"
metrics:
  duration: ~3 min
  completed: 2026-03-11
---

# Quick Task 050: Pipeline Timing Behavior Summary

**One-liner:** Move snmp.pipeline.duration from MetricPollJob into TimingBehavior as outermost MediatR behavior, covering both poll and trap sources automatically.

## What Was Done

### Task 1: Create TimingBehavior and register as outermost pipeline behavior
- Created `TimingBehavior<TRequest, TResponse>` following LoggingBehavior pattern
- Uses Stopwatch to measure full pipeline processing time
- Records snmp.pipeline.duration histogram for SnmpOidReceived messages only (device_name tag, "unknown" fallback)
- Registered as 0th AddOpenBehavior in ServiceCollectionExtensions, shifting all others down
- **Commit:** 7a2a556

### Task 2: Remove RecordPipelineDuration from MetricPollJob and add tests
- Removed `_pipelineMetrics.RecordPipelineDuration()` call from MetricPollJob line 106
- Preserved Stopwatch and PollDurationMs (still used for snmp_gauge_duration/snmp_info_duration from Q049)
- Created 3 unit tests: duration recorded with device_name, non-SnmpOidReceived skipped, null DeviceName records as "unknown"
- All 207 tests pass (9 MetricPollJob tests unchanged)
- **Commit:** c337652

## Verification

1. `dotnet build` -- no errors
2. `dotnet test` -- 207/207 pass
3. `RecordPipelineDuration` appears in TimingBehavior.cs and PipelineMetricService.cs, NOT in MetricPollJob.cs
4. TimingBehavior registered before LoggingBehavior in AddSnmpPipeline
5. PollDurationMs still set in MetricPollJob.DispatchResponseAsync

## Deviations from Plan

None -- plan executed exactly as written.

## Pipeline Behavior Order (updated)

| Position | Behavior | Purpose |
|----------|----------|---------|
| 0th (outermost) | TimingBehavior | Pipeline duration histogram (Q050) |
| 1st | LoggingBehavior | Entry/exit logging + published counter |
| 2nd | ExceptionBehavior | Catches unhandled exceptions |
| 3rd | ValidationBehavior | Validates message and device |
| 4th | OidResolutionBehavior | Resolves OID to metric name |
| 5th | ValueExtractionBehavior | Extracts numeric/string value |
| 6th | TenantVectorFanOutBehavior | Routes to tenant vector slots |
