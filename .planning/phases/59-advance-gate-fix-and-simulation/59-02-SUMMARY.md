---
phase: 59-advance-gate-fix-and-simulation
plan: 02
subsystem: e2e-testing
tags: [e2e, advance-gate, priority-starvation, MTS-02, MTS-03, TierResult, suppression]

# Dependency graph
requires:
  - phase: 59-01
    provides: TierResult.Unresolved for tier=4, advance gate blocks on Unresolved

provides:
  - Scenario 35 rewritten: MTS-02B gate-pass via P1 Healthy (sim switch to default), not suppression
  - Scenario 40 new: MTS-03 priority starvation proof (P2 never evaluated in 120s window)

affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Gate-pass via Healthy transition: MTS-02B proves gate opens when P1 switches to tier=3 Healthy, not via suppression state"
    - "Starvation proof pattern: direct pod log scan with --since=120s across all replicas to prove P2 tier log completely absent"

key-files:
  created:
    - tests/e2e/scenarios/40-mts-03-starvation-proof.sh
  modified:
    - tests/e2e/scenarios/35-mts-02-advance-gate.sh

key-decisions:
  - "MTS-02B gate-pass triggered by sim_set_scenario default (P1 Healthy): the corrected advance gate blocks on Unresolved, so gate-pass must come from P1 transitioning to tier=3 Healthy, not from suppression"
  - "report.sh range |28|40| unchanged: new scenario 40 is at 0-based index 39, already within existing range"

patterns-established:
  - "MTS-02A: P1 Unresolved blocks gate — P2 tier log absent in --since=15s, P1 sent counter increments"
  - "MTS-02B: sim_set_scenario default -> P1 tier=3 Healthy -> P2 tier log appears (gate passed)"
  - "MTS-03: P1 always Unresolved (sent+suppressed cycle) -> P2 tier log absent in 120s across all pods"

# Metrics
duration: 3min
completed: 2026-03-19
---

# Phase 59 Plan 02: Priority Starvation Simulation Summary

**E2E scenario 35 rewritten for correct Healthy-based gate-pass semantics and scenario 40 created as the starvation proof — P2 never evaluated in a 120s window while P1 cycles through sent and suppressed commands.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-19T15:16:42Z
- **Completed:** 2026-03-19T15:19:39Z
- **Tasks:** 2
- **Files modified:** 1 (scenario 35 rewrite), 1 created (scenario 40)

## Accomplishments

- Rewrote scenario 35 MTS-02B: gate-pass now triggered by switching simulator to `default` (P1 returns tier=3 Healthy) rather than the old buggy path where suppressed P1 returned ConfirmedBad and accidentally unblocked the gate
- Removed the quiescence sub-scenario 35d, which was predicated on the old ConfirmedBad behavior and no longer applies
- Created scenario 40 (MTS-03) — the starvation proof: P1 cycles through tier=4 commands (first sent, then suppressed, then sent again) while P2 is completely starved; 120s window across all replicas confirms zero P2 tier logs

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite scenario 35 (MTS-02) for correct gate semantics** - `8ec78be` (feat)
2. **Task 2: Create scenario 40 (MTS-03 starvation proof) and extend report.sh** - `df5f235` (feat)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `tests/e2e/scenarios/35-mts-02-advance-gate.sh` - MTS-02B rewritten: sim_set_scenario default triggers P1 Healthy, which passes gate for P2; sub-scenario 35d (quiescence) removed; all ConfirmedBad/Commanded terminology replaced with Unresolved/Healthy
- `tests/e2e/scenarios/40-mts-03-starvation-proof.sh` - New scenario: MTS-03A (P1 tier=4 + sent counter), MTS-03B (P1 suppressed counter after 12s wait), MTS-03C (P2 tier log absent in 120s window); 4 sub-scenarios, 8 pass/fail branches

## Decisions Made

- **MTS-02B gate-pass via Healthy (sim_set_scenario default):** After plan 59-01 fixed tier=4 to always return Unresolved, the old MTS-02B path (wait for P1 to go ConfirmedBad from suppression) became invalid. The correct gate-pass trigger is P1 transitioning to tier=3 Healthy when all evaluate metrics return to baseline (e2e_port_utilization=0 < Max:80 in the default simulator scenario).
- **report.sh range unchanged:** The Snapshot Evaluation category range `|28|40|` (0-based, inclusive) already covers index 39 where the new scenario 40 falls. No extension required.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 59 complete: advance gate bug fixed (59-01) and E2E proof scenarios updated/created (59-02)
- Scenario 35 correctly validates gate-blocking and gate-passing via Healthy transition
- Scenario 40 is the starvation proof — the core deliverable of phase 59
- No blockers for future phases

---
*Phase: 59-advance-gate-fix-and-simulation*
*Completed: 2026-03-19*
