# Quick Task 076 Summary: Snapshot tier fixes

## What was done

### 1. Renamed ConfirmedBad to Violated
`TierResult.ConfirmedBad` → `TierResult.Violated`. "Violated" means the threshold condition is met — it's a state, not a judgment. Updated enum, all code references, all test names/comments/assertions.

### 2. Verified threshold source-aware checks
Confirmed correct: Poll/Synthetic sources check ALL time series samples. Trap/Command sources check newest sample only. Both tier 2 (Resolved) and tier 3 (Evaluate) follow this pattern. Tests cover all combinations.

### 3. Fixed staleness bug
Per spec (Docs/snapshotJob.txt line 24, 30): "command execution only if stale metric detected" and "is stale skip to last tier." Staleness now skips tiers 2-3 and falls through to tier 4 (command dispatch with suppression). Previously, staleness returned `TierResult.Stale` with no commands — directly contradicting the spec.

Also removed `TierResult.Stale` from the enum since staleness now produces `Commanded` (or `Violated` if all suppressed). Advance gate blocks on `Commanded` which covers both stale and evaluate command paths.

## Files changed

| File | Change |
|------|--------|
| `src/SnmpCollector/Jobs/SnapshotJob.cs` | Renamed enum, restructured EvaluateTenant flow, removed Stale tracking |
| `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` | Updated all assertions, stale tests now verify commands dispatched |

## Test results

- 453/453 unit tests pass
- Commit: 911dd08
