---
phase: 48-snapshotjob-4-tier-evaluation
plan: 01
subsystem: scheduling
tags: [quartz, snapshot-job, liveness, di-registration]

dependency_graph:
  requires: [46-02, 47-01, 47-02]
  provides: [snapshot-job-skeleton, quartz-snapshot-registration]
  affects: [48-02, 48-03, 48-04]

tech_stack:
  added: []
  patterns: [quartz-job-shell, correlation-id-scoping, liveness-stamp-finally]

file_tracking:
  key_files:
    created:
      - src/SnmpCollector/Jobs/SnapshotJob.cs
    modified:
      - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

decisions: []

metrics:
  duration: ~1 min
  completed: 2026-03-16
---

# Phase 48 Plan 01: SnapshotJob Skeleton Summary

**SnapshotJob Quartz job skeleton with 8-param DI, correlation scoping, liveness stamp, and Quartz registration with interval from SnapshotJobOptions**

## What Was Done

### Task 1: Create SnapshotJob skeleton
Created `src/SnmpCollector/Jobs/SnapshotJob.cs` following the HeartbeatJob pattern:
- `[DisallowConcurrentExecution]` attribute
- `public sealed class SnapshotJob : IJob`
- Constructor injects all 8 dependencies: `ITenantVectorRegistry`, `ISuppressionCache`, `ICommandChannel`, `ICorrelationService`, `ILivenessVectorService`, `PipelineMetricService`, `IOptions<SnapshotJobOptions>`, `ILogger<SnapshotJob>`
- Execute method: scopes `OperationCorrelationId` at entry, placeholder `foreach` over `_registry.Groups`, catches `OperationCanceledException` (re-throws), catches generic `Exception` (logs error), stamps liveness and clears correlation in `finally`

### Task 2: Register SnapshotJob in AddSnmpScheduling
Modified `ServiceCollectionExtensions.AddSnmpScheduling`:
- Added local `SnapshotJobOptions` bind before `services.AddQuartz` call
- Registered `SnapshotJob` with Quartz: `AddJob<SnapshotJob>`, trigger with `IntervalSeconds` from options, `WithMisfireHandlingInstructionNextWithRemainingCount`
- Registered `"snapshot"` in `intervalRegistry` for `LivenessHealthCheck` staleness detection
- Updated `initialJobCount` from 2 to 3 (CorrelationJob + HeartbeatJob + SnapshotJob)

## Verification

- `dotnet build` compiles with 0 errors (1 expected CS1998 warning for empty async loop body)
- All 376 existing tests pass
- `grep DisallowConcurrentExecution SnapshotJob.cs` confirms attribute present
- `grep Register("snapshot"` confirms liveness interval registration

## Deviations from Plan

None - plan executed exactly as written.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 63584b2 | feat(48-01): create SnapshotJob skeleton with Quartz job shell |
| 2 | e78d22b | feat(48-01): register SnapshotJob in AddSnmpScheduling with Quartz trigger |

## Next Phase Readiness

Plans 48-02 through 48-04 will fill in the placeholder loop body with:
- 48-02: Tier 1 (staleness) and Tier 2 (resolved-gate) evaluation
- 48-03: Tier 3 (evaluate-threshold) and Tier 4 (command dispatch)
- 48-04: Priority group advance gate and cycle summary logging

All prerequisite infrastructure is in place. No blockers.
