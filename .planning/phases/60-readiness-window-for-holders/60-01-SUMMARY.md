---
phase: 60-readiness-window-for-holders
plan: 01
subsystem: pipeline
tags: [MetricSlotHolder, readiness, sentinel, time-series, ImmutableArray]

# Dependency graph
requires:
  - phase: 59-advance-gate-fix-and-simulation
    provides: advance gate fix and starvation simulation (context for why sentinel corrupts evaluation)
provides:
  - Sentinel-free MetricSlotHolder with empty ImmutableArray initial state
  - ConstructedAt timestamp property on each holder
  - ReadinessGrace TimeSpan computed from TimeSeriesSize * IntervalSeconds * GraceMultiplier
  - IsReady bool: false for fresh/empty holder, true once data exists or grace elapsed
affects:
  - 60-02-PLAN.md (SnapshotJob: skip not-ready holders via IsReady)
  - Any code that previously relied on ReadSlot() returning non-null on fresh holders

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Readiness grace window: time-based + data-presence short-circuit for holder participation"
    - "Sentinel-free initialization: empty ImmutableArray.Empty is the sole initial state"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/MetricSlotHolder.cs
    - tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs

key-decisions:
  - "IsReady short-circuits true when ReadSeries().Length > 0 — CopyFrom with real data makes holder immediately ready despite new ConstructedAt"
  - "ReadinessGrace = TimeSeriesSize * IntervalSeconds * GraceMultiplier — no time-based sentinel replacement; pure computed property"
  - "ConstructedAt uses property initializer (DateTimeOffset.UtcNow) rather than constructor body assignment — equivalent behavior, cleaner code"

patterns-established:
  - "IsReady pattern: data-presence short-circuit first, then elapsed-time fallback"

# Metrics
duration: 2min
completed: 2026-03-19
---

# Phase 60 Plan 01: Readiness Window for Holders Summary

**Sentinel-free MetricSlotHolder with ConstructedAt, ReadinessGrace, and IsReady — empty series before first write, time-based grace window gates SnapshotJob participation**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-19T17:03:49Z
- **Completed:** 2026-03-19T17:06:38Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Removed sentinel sample from MetricSlotHolder constructor — `_box` starts as `SeriesBox.Empty`, `ReadSlot()` returns null before first write
- Added `ConstructedAt` property (set via property initializer at construction time)
- Added `ReadinessGrace` = `TimeSeriesSize * IntervalSeconds * GraceMultiplier` as a computed TimeSpan
- Added `IsReady` bool: short-circuits true when data is present, otherwise checks elapsed time vs grace window
- Updated 3 existing tests (sentinel → null/empty), added 6 new tests covering all readiness paths; all 21 pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Remove sentinel and add readiness properties to MetricSlotHolder** - `7ffb349` (feat)
2. **Task 2: Update MetricSlotHolderTests for sentinel removal** - `8aeb9eb` (test)

**Plan metadata:** (committed with this summary)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` - Sentinel removed, ConstructedAt/ReadinessGrace/IsReady added, XML docs updated
- `tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs` - 3 tests rewritten, 6 new tests added, all 21 pass

## Decisions Made

- `IsReady` short-circuits `true` when `ReadSeries().Length > 0` — this ensures that when config reloads copy real data into a fresh holder via `CopyFrom`, the holder is immediately ready despite having a brand-new `ConstructedAt`.
- `ReadinessGrace` is a pure computed property (no caching) — TimeSeriesSize and IntervalSeconds are immutable on the holder, so recomputing is free.
- `ConstructedAt` uses a property initializer (`= DateTimeOffset.UtcNow`) rather than being set in the constructor body — functionally identical, syntactically cleaner.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `MetricSlotHolder.IsReady` is ready for consumption by `SnapshotJob` (Plan 02)
- Plan 02 will use `IsReady` to skip holders during evaluation — fresh holders after config reload will be excluded until their grace window elapses or they receive their first poll result
- No blockers.

---
*Phase: 60-readiness-window-for-holders*
*Completed: 2026-03-19*
