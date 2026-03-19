---
phase: 58-snapshot-tier-simulation-tests
plan: 03
subsystem: testing
tags: [e2e, bash, snmp, snapshot-job, staleness, synthetic, aggregate, tier4, commands]

requires:
  - phase: 58-01
    provides: report.sh Snapshot Evaluation range extended to include scenarios 33+
  - phase: 58-02
    provides: STS-06 poll staleness-to-commands pattern (reference for STS-07)
  - phase: 55-01
    provides: tenant-cfg04-aggregate.yaml and agg_breach scenario for synthetic aggregate testing

provides:
  - Scenario 39 (STS-07): synthetic staleness-to-commands via e2e-tenant-agg aggregate fixture
  - report.sh Snapshot Evaluation range updated to 28|40 covering scenarios through index 39

affects: [phase-59-if-any, run-all.sh]

tech-stack:
  added: []
  patterns:
    - "Prime with agg_breach before stale switch — synthetic holder needs populated timestamps to age out"
    - "Scope tier log grep to specific tenant name to avoid false positives from prior scenario logs"
    - "poll_until for counter assertions; poll_until_log 90s for tier log assertions"

key-files:
  created:
    - tests/e2e/scenarios/39-sts-07-synthetic-stale-to-commands.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "Prime with agg_breach (not healthy) to populate synthetic aggregate timestamps before stale switch"
  - "tier=1 log grep scoped to e2e-tenant-agg prefix to avoid matching other tenants in buffer"

patterns-established:
  - "STS-07 mirrors STS-06 structure but substitutes agg_breach priming for healthy priming"

duration: 2min
completed: 2026-03-19
---

# Phase 58 Plan 03: STS-07 Synthetic Staleness-to-Commands Summary

**Scenario 39 proves synthetic aggregate metric staleness (e2e-tenant-agg, e2e_total_util) triggers tier=1 stale detection and tier=4 command dispatch via agg_breach priming + stale switch sequence**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-19T08:32:53Z
- **Completed:** 2026-03-19T08:34:12Z
- **Tasks:** 1
- **Files modified:** 2

## Accomplishments

- Created scenario 39 (STS-07) covering the synthetic staleness row in the tier behavior table — the gap not covered by STS-06 (poll staleness)
- Uses agg_breach priming to ensure the synthetic holder has populated timestamps before switching to stale; without this HasStaleness returns false for null slots
- Extended report.sh Snapshot Evaluation range from `28|38` to `28|40` to include scenario 39

## Task Commits

Each task was committed atomically:

1. **Task 1: Create STS-07 synthetic staleness-to-commands scenario + update report.sh** - `d0d1271` (feat)

**Plan metadata:** (see final commit below)

## Files Created/Modified

- `tests/e2e/scenarios/39-sts-07-synthetic-stale-to-commands.sh` - STS-07 scenario: 3 sub-assertions (tier=1 stale log, tier=4 commands enqueued, sent counter delta)
- `tests/e2e/lib/report.sh` - Snapshot Evaluation end index updated from 38 to 40

## Decisions Made

- **Prime with agg_breach (not healthy):** The synthetic holder (e2e_total_util) is computed from poll group OIDs .4.5 and .4.6. Only agg_breach scenario populates those OIDs with non-zero values, causing the aggregate to compute and set holder timestamps. The healthy scenario sets those OIDs to 0 which may still compute but with sum=0. Priming ensures timestamps exist to age out when stale is applied.
- **tier=1 log grep scoped to e2e-tenant-agg:** Prior scenarios may have logged tier=1 stale for other tenants still in the pod log buffer; scoping to the tenant name prevents false positives.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 58 scenario suite complete: STS-01 through STS-07 + ADV-01, ADV-02, ADV-03 all covered
- Snapshot Evaluation report range now covers indices 28-40 (scenarios 29-41 in 1-based numbering)
- No blockers or concerns

---
*Phase: 58-snapshot-tier-simulation-tests*
*Completed: 2026-03-19*
