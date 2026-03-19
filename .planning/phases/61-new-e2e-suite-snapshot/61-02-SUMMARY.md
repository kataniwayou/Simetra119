---
phase: 61-new-e2e-suite-snapshot
plan: 02
subsystem: testing
tags: [e2e, bash, k8s, snapshot-job, tier-evaluation, per-oid-control, prometheus]

# Dependency graph
requires:
  - phase: 61-01
    provides: sim_set_oid/sim_set_oid_stale/reset_oid_overrides helpers, tenant-cfg05-four-tenant-snapshot.yaml, 1s SnapshotJob interval
  - phase: 60-pre-tier-readiness
    provides: ReadinessGrace/AreAllReady semantics — not-ready and stale scenarios depend on these
provides:
  - SNS-01: Not Ready scenario (41-sns-01-not-ready.sh)
  - SNS-02: Stale-to-Commands scenario (42-sns-02-stale-to-commands.sh)
  - SNS-03: All Resolved Violated + partial violation sub-assertion (43-sns-03-resolved.sh)
  - SNS-04: Unresolved/Commands scenario (44-sns-04-unresolved.sh)
  - SNS-05: Healthy scenario (45-sns-05-healthy.sh)
affects: [61-03]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Negative counter assertion pattern: snapshot_counter before, sleep 10, snapshot_counter after, assert delta==0
    - Positive counter assertion pattern: snapshot_counter baseline before OID change, poll_until after
    - Partial resolved violation sub-assertion: reset_oid_overrides + re-prime + sleep 3 + set partial, then poll tier=3 and grep absence of tier=2
    - tier=4 log pattern uses both em-dash and double-dash variants for robustness

key-files:
  created:
    - tests/e2e/scenarios/41-sns-01-not-ready.sh
    - tests/e2e/scenarios/42-sns-02-stale-to-commands.sh
    - tests/e2e/scenarios/43-sns-03-resolved.sh
    - tests/e2e/scenarios/44-sns-04-unresolved.sh
    - tests/e2e/scenarios/45-sns-05-healthy.sh
  modified: []

key-decisions:
  - "SNS-01 uses poll_until_log 15 1 with since=15 — grace is 6s, 1s cycle interval means log appears within seconds"
  - "SNS-02 BEFORE_SENT baseline captured BEFORE sim_set_oid_stale calls — delta measures only post-stale dispatches"
  - "SNS-03 negative counter assertion uses snapshot+sleep+snapshot pattern (not poll_until) — absence is a snapshot"
  - "SNS-03 partial violation sub-assertion uses reset_oid_overrides + re-prime + sleep 3 to flush prior tier=2 logs before asserting tier=3"
  - "SNS-04/SNS-05 positive/negative counter patterns mirror SNS-03 for consistency"
  - "All tier=4 log patterns include both em-dash and double-dash variants to handle log format differences"

patterns-established:
  - "All 5 SNS scenarios: save/apply/reload + prime 4 tenants + sleep 8 setup block is the standard for cfg05 scenarios"
  - "Negative assertion (delta=0) is synchronous: no polling needed, just snapshot before + sleep 10 + snapshot after"
  - "Partial violation sub-assertion requires explicit reset_oid_overrides and re-prime to clear log contamination"

# Metrics
duration: 3min
completed: 2026-03-19
---

# Phase 61 Plan 02: SNS Scenario Scripts (41-45) Summary

**5 single-tenant snapshot evaluation state scenarios (Not Ready, Stale-to-Commands, Resolved/partial, Unresolved, Healthy) each using per-OID HTTP control to produce exactly one tier result and assert log + counter evidence.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-19T20:12:49Z
- **Completed:** 2026-03-19T20:16:12Z
- **Tasks:** 2
- **Files modified:** 5 (all created)

## Accomplishments

- All 5 evaluation tree leaf states (Not Ready, Stale, Resolved, Unresolved, Healthy) are now covered by distinct runnable E2E scenario scripts
- SNS-03 includes sub-assertion 43c proving partial resolved violation (one=0, one=1) does NOT trigger tier=2 and evaluation correctly continues to tier=3
- Counter assertions follow two distinct patterns: positive (poll_until increment) for command-dispatching states, negative (snapshot+sleep+snapshot, delta=0) for non-dispatching states

## Task Commits

Each task was committed atomically:

1. **Task 1: SNS-01 Not Ready and SNS-02 Stale-to-Commands scripts** - `f427c39` (feat)
2. **Task 2: SNS-03 Resolved, SNS-04 Unresolved, SNS-05 Healthy scripts** - `babace1` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/scenarios/41-sns-01-not-ready.sh` - Asserts G1-T1 "not ready" log within 15s of fixture load (no priming, grace=6s, 1s cycle)
- `tests/e2e/scenarios/42-sns-02-stale-to-commands.sh` - Primes T1, baselines counter, switches T1 OIDs to stale, asserts tier=1 + tier=4 + sent counter (42a/42b/42c)
- `tests/e2e/scenarios/43-sns-03-resolved.sh` - Sets T1 res1+res2=0, asserts tier=2 + zero counter delta; sub-assertion 43c proves partial violation continues to tier=3 (43a/43b/43c + 43c absence check = 8 record calls)
- `tests/e2e/scenarios/44-sns-04-unresolved.sh` - Sets T1 eval=0, asserts tier=4 log and sent counter increment (44a/44b)
- `tests/e2e/scenarios/45-sns-05-healthy.sh` - Primes T1 to healthy (eval=10, res=1), asserts tier=3 log and zero counter delta (45a/45b)

## Decisions Made

- SNS-01 uses poll_until_log 15 1 with since=15: grace is 6s, 1s SnapshotJob interval means not-ready log appears within 1-2s of reload — 15s timeout is sufficient
- SNS-02 counter baseline captured BEFORE sim_set_oid_stale calls: ensures delta measures only post-stale dispatches, not any priming-phase commands
- Negative counter assertions use snapshot+sleep 10+snapshot (not poll_until): absence of increment is a point-in-time check — polling would just time out
- SNS-03 partial violation sub-assertion calls reset_oid_overrides + re-prime + sleep 3 before setting partial state: flushes previous tier=2 logs from pod buffer so absence check of tier=2 in --since=10s window is clean
- tier=4 log patterns use both `—` (em-dash) and `--` (double-dash) variants via `\|` alternation: defensive against log format differences across environments

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 5 Part 1 scenarios complete; plan 03 can proceed with Part 2 advance gate scenarios (scenarios 46-51)
- Pattern established: prime 4 tenants with sim_set_oid for readiness, then modify per-tenant OIDs for specific gate conditions
- SNS-03's partial violation sub-assertion pattern (reset + re-prime + flush sleep + set partial) is available for reuse in plan 03 if needed

---
*Phase: 61-new-e2e-suite-snapshot*
*Completed: 2026-03-19*
