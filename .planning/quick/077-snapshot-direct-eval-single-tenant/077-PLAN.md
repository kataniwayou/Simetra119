# Quick Task 077: Direct EvaluateTenant for single-tenant groups

## Objective

Skip ThreadPool hop when a priority group has only 1 tenant. Call EvaluateTenant directly on the Quartz thread instead of Task.Run, eliminating 500-1000ms outliers from ThreadPool hill-climbing.

## Tasks

### Task 1: Add single-tenant fast path in SnapshotJob.Execute

**File:** `src/SnmpCollector/Jobs/SnapshotJob.cs`

- If `group.Tenants.Count == 1`: call `EvaluateTenant` directly
- Else: use existing `Task.WhenAll` + `Task.Run` for parallel eval

### Task 2: Verify with 1s interval deployment

- Build and deploy with SnapshotJob IntervalSeconds=1
- Collect 3 minutes of data (~180 cycles per pod)
- Compare outlier profile against pre-fix baseline
