---
phase: 45-structural-prerequisites
plan: 01
subsystem: pipeline
tags: [enum, oid-resolution, refactor, command-source]
dependency-graph:
  requires: []
  provides: [SnmpSource.Command, data-driven-oid-bypass]
  affects: [47-command-worker, 46-command-map]
tech-stack:
  added: []
  patterns: [data-driven-guard-over-source-coupling]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/SnmpSource.cs
    - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
decisions:
  - id: 45-01-D1
    description: "MetricName guard uses `is not null && != Unknown` — Unknown is excluded because an Unknown MetricName means OID resolution failed or was never attempted, not that it was pre-set"
metrics:
  duration: ~1 min
  completed: 2026-03-16
---

# Phase 45 Plan 01: Add SnmpSource.Command and Refactor OidResolutionBehavior Summary

**One-liner:** Added Command enum value to SnmpSource and replaced Source-coupled Synthetic bypass with data-driven MetricName-already-set guard in OidResolutionBehavior.

## What Was Done

### Task 1: Add SnmpSource.Command enum value
- Added `Command` as 4th value in `SnmpSource` enum (Poll, Trap, Synthetic, Command)
- Pure addition with no downstream impact — no existing code references Command yet
- **Commit:** 5109bc5

### Task 2: Refactor OidResolutionBehavior to MetricName-already-set guard + add tests
- Replaced `if (msg.Source == SnmpSource.Synthetic)` with `if (msg.MetricName is not null && msg.MetricName != OidMapService.Unknown)`
- This makes the bypass data-driven: any message with a pre-set valid MetricName skips OID resolution
- Added 2 new tests for Command-source messages with pre-set MetricName
- All 10 OidResolutionBehavior tests pass (8 existing + 2 new)
- Full test suite: 340/340 pass
- **Commit:** ca8c145

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| 45-01-D1 | MetricName guard excludes Unknown | Unknown means OID resolution failed/never ran, not that MetricName was intentionally pre-set |

## Deviations from Plan

None - plan executed exactly as written.

## Verification Results

1. `dotnet build` - compiles with zero errors
2. OidResolutionBehavior tests - 10/10 pass
3. Full test suite - 340/340 pass
4. Grep for `SnmpSource.Synthetic` in OidResolutionBehavior.cs - zero matches

## Next Phase Readiness

Plan 45-02 can proceed. The Command enum value and data-driven guard are in place for the CommandWorkerService pipeline integration in later phases.
