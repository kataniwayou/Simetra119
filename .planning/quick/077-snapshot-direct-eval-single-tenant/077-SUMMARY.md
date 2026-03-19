# Quick Task 077 Summary: Direct EvaluateTenant for single-tenant groups

## What was done

Added a fast path in `SnapshotJob.Execute`: when `group.Tenants.Count == 1`, call `EvaluateTenant` directly on the Quartz thread instead of `Task.Run`. This eliminates ThreadPool scheduling latency for single-tenant priority groups.

## Before vs After (1s interval, ~220 cycles per pod)

### Before (Task.Run for all groups)

| Pod | Role | Max | Outliers >100ms |
|-----|------|-----|-----------------|
| nmnwd | leader | 1004ms | 4 (967, 1000, 1001, 1004) |
| zpcwm | follower | 731ms | 4 (500, 546, 717, 731) |
| lgx49 | follower | 1000ms | 1 (1000) |

### After (direct call for single tenant)

| Pod | Role | Max | Outliers >100ms |
|-----|------|-----|-----------------|
| 5lmz2 | leader | 3.0ms | **0** |
| 6r4t5 | follower | 2.2ms | **0** |
| dtz8k | follower | 3.1ms | **0** |

**Zero outliers.** Max duration dropped from 1004ms to 3.1ms. No leader/follower gap — all pods perform identically.

## Duration distribution (after fix)

| Duration | Leader | Follower 1 | Follower 2 |
|----------|--------|------------|------------|
| 0.0ms | 175 (78%) | 176 (80%) | 162 (76%) |
| 0.1ms | 47 (21%) | 40 (18%) | 48 (22%) |
| 0.2ms | 2 | 2 | 2 |
| 0.3-3.1ms | 1 | 2 | 2 |

All three pods have identical distribution. No leader/follower difference.

## Files changed

| File | Change |
|------|--------|
| `src/SnmpCollector/Jobs/SnapshotJob.cs` | Added `if (group.Tenants.Count == 1)` direct call path |

## Test results

- 453/453 unit tests pass
- Commit: 9410015
