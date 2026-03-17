---
phase: 52-test-library-and-config-artifacts
plan: "03"
subsystem: testing
tags: [e2e, yaml, configmap, tenant, snapshot, kubernetes]

requires:
  - phase: 52-01
    provides: OID/command/device map entries for e2e .4.x metrics
  - phase: 52-02
    provides: sim.sh library with sim_set_scenario and poll_until_log

provides:
  - 4 tenant fixture YAML files covering CFG-04 through CFG-07 topologies
  - Single-tenant (e2e-tenant-A), two-same-priority (A+B), two-diff-priority (P1+P2), aggregate-evaluate (e2e-tenant-agg) topologies
  - Locked threshold design: resolved Min:1.0 (violated by default), evaluate Max:80.0 (not violated by default)

affects:
  - phases 53-55 (scenario scripts source these fixtures via kubectl apply -f)

tech-stack:
  added: []
  patterns:
    - "Tenant fixture pattern: K8s ConfigMap YAML with tenants.json inline JSON array"
    - "Threshold design: resolved Min:1.0 makes idle state ConfirmedBad; evaluate Max:80.0 avoids false triggers"
    - "SuppressionWindowSeconds:10 for testability (short enough to observe suppression in 30s window)"
    - "GraceMultiplier:2.0 on TimeSeriesSize metrics to satisfy must-have constraint"

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg01-single.yaml
    - tests/e2e/fixtures/tenant-cfg02-two-same-prio.yaml
    - tests/e2e/fixtures/tenant-cfg03-two-diff-prio.yaml
    - tests/e2e/fixtures/tenant-cfg04-aggregate.yaml
  modified: []

key-decisions:
  - "Resolved Min:1.0 only (no Max) — value 0 < 1 means violated; value >=1 means in-range. Enables command_trigger scenario to clear resolved by setting .4.2=2, .4.3=2"
  - "Evaluate Max:80.0 only (no Min) — default value 0 not violated; threshold_breach scenario sets .4.1=90 to trigger"
  - "All 4 fixtures share identical threshold values for consistency — scenario scripts rely on predictable fixture semantics"
  - "Tenant IDs are distinct within each fixture (no ID reuse) to prevent suppression cache bleed between scenarios"

patterns-established:
  - "Tenant fixture topology naming: cfg01=single, cfg02=two-same-prio, cfg03=two-diff-prio, cfg04=aggregate"
  - "Fixture structure mirrors simetra-tenants.yaml production ConfigMap exactly"

duration: 1min
completed: 2026-03-17
---

# Phase 52 Plan 03: Tenant Fixture YAMLs Summary

**4 K8s ConfigMap tenant fixture files covering single-tenant, two-same-priority, two-diff-priority, and aggregate-evaluate topologies with locked threshold design (resolved Min:1.0 violated by default, evaluate Max:80.0 not violated by default)**

## Performance

- **Duration:** 1 min
- **Started:** 2026-03-17T12:08:35Z
- **Completed:** 2026-03-17T12:09:50Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Created all 4 tenant fixture ConfigMap YAML files for E2E snapshot scenario use
- Established consistent threshold design: resolved metrics violated at idle (Tier 2 ConfirmedBad), evaluate metric not violated at idle (no false command triggers)
- All fixtures validated as YAML + JSON parseable with correct structure, IDs, and thresholds

## Task Commits

Each task was committed atomically:

1. **Task 1: Create single-tenant and aggregate fixture files** - `e0433c0` (feat)
2. **Task 2: Create multi-tenant fixture files** - `14926bf` (feat)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `tests/e2e/fixtures/tenant-cfg01-single.yaml` - Single tenant (e2e-tenant-A, Priority 1): 1 evaluate (e2e_port_utilization Max:80), 2 resolved (Min:1.0), 1 command (e2e_set_bypass)
- `tests/e2e/fixtures/tenant-cfg02-two-same-prio.yaml` - Two tenants (e2e-tenant-A, e2e-tenant-B) both Priority 1, same OIDs
- `tests/e2e/fixtures/tenant-cfg03-two-diff-prio.yaml` - Two tenants (e2e-tenant-P1 Priority 1, e2e-tenant-P2 Priority 2) for advance-gate testing
- `tests/e2e/fixtures/tenant-cfg04-aggregate.yaml` - Single tenant (e2e-tenant-agg): aggregate evaluate metric e2e_total_util (Max:80), 2 resolved (Min:1.0), 1 command

## Decisions Made

- **Resolved threshold Min:1.0 only:** Using `{ "Min": 1.0 }` without Max means violated when value < 1.0. Default value 0 is < 1.0, so idle state is always ConfirmedBad. The `command_trigger` simulator scenario (Phase 52-01) sets .4.2=2 and .4.3=2 to clear both resolved metrics.
- **Evaluate threshold Max:80.0 only:** Using `{ "Max": 80.0 }` means violated when value > 80.0. Default value 0 is not violated — no false command triggers at idle. The `threshold_breach` scenario sets .4.1=90 to breach.
- **SuppressionWindowSeconds: 10** in all fixtures — short enough that suppression scenarios can observe both a suppressed and an unsuppressed command within a ~30s test window.
- **GraceMultiplier: 2.0** on all TimeSeriesSize metrics — satisfies the must-have constraint from the plan frontmatter.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 4 tenant fixture files ready for `kubectl apply -f` in Phase 53-55 scenario scripts
- Threshold design is locked and documented: scenario scripts should use `command_trigger` to reach Tier 4 (sets evaluate=90, resolved=2 each)
- cfg03 advance-gate fixture ready for priority-gating test: P1 must be Healthy before P2 is evaluated

---
*Phase: 52-test-library-and-config-artifacts*
*Completed: 2026-03-17*
