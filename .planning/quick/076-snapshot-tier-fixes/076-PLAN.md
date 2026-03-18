# Quick Task 076: Snapshot tier fixes — staleness, rename, verify thresholds

## Objective

1. Rename `ConfirmedBad` to `Violated` — violated means the threshold condition is met, not something bad
2. Verify threshold checks: all time series samples for Poll/Synthetic, newest only for Trap/Command — confirmed correct
3. Fix staleness bug: per spec, stale data should skip to commands (tier 4), not block them

## Tasks

### Task 1: All three fixes in SnapshotJob and tests

**Files:**
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — rename enum, fix staleness flow, remove Stale result
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — update all assertions, fix stale tests to verify commands

## Verification

- All 453 unit tests pass
- Staleness now dispatches commands (verified by new test assertions)
- Threshold checks confirmed correct for all source types
