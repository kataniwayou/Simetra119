---
phase: 58-snapshot-tier-simulation-tests
plan: "02"
subsystem: testing
tags: [e2e, bash, snapshotjob, staleness, tier1, tier4, commands, snmp, prometheus]

# Dependency graph
requires:
  - phase: 58-01
    provides: fixture and simulator context for staleness scenarios
  - phase: quick-076
    provides: staleness-to-commands behavioral change (tier=1 stale skips to tier=4)

provides:
  - STS-06 scenario script proving complete staleness-to-commands path
  - E2E coverage for tier=1 stale -> tier=4 command dispatch with counter evidence

affects:
  - report.sh (scenario range may need extending to include index 38)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Prime-then-stale: always set healthy scenario + 20s sleep before switching to stale to ensure non-null poll slots"
    - "poll_until_log for log-based assertions, poll_until for counter-based assertions — never bare snapshot_counter for post-trigger checks"
    - "Scope tier=4 log pattern to tenant ID (e2e-tenant-A.*) to avoid false positive from prior scenarios"

key-files:
  created:
    - tests/e2e/scenarios/38-sts-06-stale-to-commands.sh
  modified: []

key-decisions:
  - "38a polls for 'tier=1 stale — skipping to commands' substring matching SnapshotJob.cs literal with Unicode em dash (U+2014)"
  - "38b scopes tier=4 log pattern to e2e-tenant-A to avoid cross-scenario contamination"
  - "38c uses poll_until (45s) before snapshot_counter for sent counter — matches STS-02 pattern for SNMP round-trip + OTel latency"
  - "Baseline captured after priming 20s wait but BEFORE stale switch — delta measures only post-stale dispatches"

patterns-established:
  - "STS-06 priming: healthy + 20s before stale — reused from STS-05 (33-sts-05-staleness.sh)"
  - "3 sub-assertions per scenario: log (tier=1), log (tier=4), counter (sent delta)"

# Metrics
duration: 2min
completed: 2026-03-19
---

# Phase 58 Plan 02: STS-06 Staleness-to-Commands Scenario Summary

**E2E scenario 38 proving tier=1 stale detection skips to tier=4 command dispatch with log and counter evidence, validating the quick-076 behavioral change end-to-end**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-19T08:29:10Z
- **Completed:** 2026-03-19T08:30:31Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Created `38-sts-06-stale-to-commands.sh` — first E2E scenario validating the staleness-to-commands path introduced by quick-076
- 3 sub-assertions (38a/38b/38c) with log pattern and counter proof, all using poll_until variants
- Standard prime-with-healthy pattern (20s) ensures null slots don't prevent stale detection
- Exact Unicode em dash (U+2014) used in log patterns to match SnapshotJob.cs string literals

## Task Commits

1. **Task 1: Create STS-06 staleness-to-commands scenario script** - `c96c5ce` (feat)

**Plan metadata:** (docs commit — see below)

## Files Created/Modified

- `tests/e2e/scenarios/38-sts-06-stale-to-commands.sh` — STS-06 scenario: primes healthy, switches to stale, polls for tier=1 stale+skip log, tier=4 commands enqueued log, and sent counter increment

## Decisions Made

- **38a log pattern uses Unicode em dash:** `"tier=1 stale — skipping to commands"` — matches the exact string in SnapshotJob.cs line 130; grep substring match does not require tenant ID prefix
- **38b scoped to e2e-tenant-A:** `"e2e-tenant-A.*tier=4 — commands enqueued"` — avoids false positives from tier=4 logs emitted in prior scenarios (STS-02, ADV scenarios) that remain in pod log buffer
- **38c uses poll_until 45s:** Counter assertion follows STS-02 pattern — SNMP SET round-trip + OTel export + Prometheus scrape has observable latency; bare snapshot immediately after log would return stale counter
- **Baseline after priming, before stale switch:** Captures only commands dispatched due to staleness, not any that occurred during the priming healthy phase

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- STS-06 (38) is ready to be integrated into run-all.sh auto-discovery (numeric prefix naming already correct)
- Plan 58-03 can proceed with any remaining phase 58 scenarios (STS-05 behavior fix, source-aware threshold scenario)
- report.sh category upper bound for "Snapshot Evaluation" may need extending from current value to include index 38 when report is next run

---
*Phase: 58-snapshot-tier-simulation-tests*
*Completed: 2026-03-19*
