---
phase: 62-single-tenant-evaluation-states
plan: 01
subsystem: e2e-testing
tags: [pss, e2e, snapshot, single-tenant, bash]
dependency-graph:
  requires: [61-new-e2e-suite-snapshot]
  provides: [pss-fixture, pss-report-category, pss-scenarios-53-55]
  affects: [62-02]
tech-stack:
  added: []
  patterns: [single-tenant-fixture, progressive-snapshot-suite]
key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg06-pss-single.yaml
    - tests/e2e/scenarios/53-pss-01-not-ready.sh
    - tests/e2e/scenarios/54-pss-02-stale-to-commands.sh
    - tests/e2e/scenarios/55-pss-03-resolved.sh
  modified:
    - tests/e2e/lib/report.sh
decisions: []
metrics:
  duration: "~3 min"
  completed: 2026-03-20
---

# Phase 62 Plan 01: PSS Fixture, Report Category, and Scenarios 53-55 Summary

Single-tenant PSS fixture with T2 OIDs from 1s poll group, report category for scenarios 53-57, and three scenario scripts covering Not Ready, Stale-to-Commands, and Resolved gate evaluation.

## Tasks Completed

### Task 1: Create PSS fixture and update report category
- Created `tenant-cfg06-pss-single.yaml` with single tenant "e2e-pss-tenant" at Priority=1
- 1 Evaluate metric (e2e_eval_T2, Min:10), 2 Resolved metrics (e2e_res1_T2/e2e_res2_T2, Min:1), 1 Command (e2e_set_bypass)
- Uses T2 OIDs (.999.5.x) in the 1s poll group; grace window = 6s
- Added "Progressive Snapshot Suite|52|57" category to report.sh (7 total categories)
- Commit: `906d2e6`

### Task 2: Create PSS scenarios 53-55
- **53-pss-01-not-ready.sh**: Asserts "not ready" log within grace window with no priming (1 assertion pair)
- **54-pss-02-stale-to-commands.sh**: Primes T2, switches to stale, asserts tier=1 stale + tier=4 commands + sent counter increment (3 assertion pairs)
- **55-pss-03-resolved.sh**: All-violated tier=2 with zero commands + partial violation reaching tier=3 with tier=2 absence check (4 assertion pairs)
- All scripts follow established SNS v2.1 pattern with PSS-prefixed names and cfg06 fixture
- All pass `bash -n` syntax validation
- Commit: `927911d`

## Deviations from Plan

None -- plan executed exactly as written.

## Verification Results

| Check | Result |
|-------|--------|
| Fixture YAML valid | PASS |
| report.sh has PSS category (52-57) | PASS |
| 3 scenario scripts exist | PASS |
| bash -n syntax (all 3) | PASS |
| Assertions: 53 >= 1 | PASS (2) |
| Assertions: 54 >= 3 | PASS (6) |
| Assertions: 55 >= 4 | PASS (8) |

## Next Phase Readiness

Phase 62-02 can proceed. The PSS fixture and report infrastructure are in place. Scenarios 53-55 cover the first three evaluation outcomes (not ready, stale-to-commands, resolved gate).
