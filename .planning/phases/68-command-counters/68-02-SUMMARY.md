---
phase: 68-command-counters
plan: 02
subsystem: testing
tags: [e2e, shell, prometheus, snmp, command-counters, kubernetes]

# Dependency graph
requires:
  - phase: 68-command-counters/68-01
    provides: report.sh Command Counter Verification category and scenarios 83-84 (CCV-01/02/03)
provides:
  - tenant-cfg09-ccv-failed.yaml fixture with unmapped CommandName e2e_set_unknown
  - scenario 85 (CCV-04) verifying snmp_command_failed_total increments on OID-not-found path
affects:
  - 68-command-counters/68-01 (report.sh category covers indices 82-88 including plan 02's 2 assertions)

# Tech tracking
tech-stack:
  added: []
  patterns: [empty-label-filter for IP:port device_name avoidance, dual-counter snapshot before trigger]

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml
    - tests/e2e/scenarios/85-ccv04-command-failed.sh
  modified: []

key-decisions:
  - "Use empty label filter '' for snmp_command_failed_total to avoid brittleness with IP:port device_name label (e2e-simulator.simetra.svc.cluster.local:161)"
  - "SuppressionWindowSeconds=5 in CCV-04 fixture to prevent suppression from masking repeated test runs"
  - "Snapshot both dispatched and failed baselines before trigger; poll dispatched first (synchronous), then wait 10s for CommandWorkerService async drain"

patterns-established:
  - "Dual-counter snapshot: capture both counter baselines before triggering tier=4 when two counters increment at different times"
  - "Empty label filter pattern: use snapshot_counter with '' when device_name is an IP:port string, not a friendly tenant name"

# Metrics
duration: 1min
completed: 2026-03-22
---

# Phase 68 Plan 02: Command Counters (CCV-04) Summary

**CCV-04 E2E scenario 85 with tenant-cfg09-ccv-failed.yaml: verifies snmp_command_failed_total and snmp_command_dispatched_total both increment when CommandWorkerService cannot resolve OID for unmapped CommandName e2e_set_unknown**

## Performance

- **Duration:** 1 min
- **Started:** 2026-03-22T16:25:00Z
- **Completed:** 2026-03-22T16:26:22Z
- **Tasks:** 1
- **Files modified:** 2 (created)

## Accomplishments
- Created tenant-cfg09-ccv-failed.yaml fixture with CommandName `e2e_set_unknown` (absent from simetra-oid-command-map), triggering the OID-not-found path in CommandWorkerService
- Created scenario 85 (CCV-04) with two sub-assertions: dispatched delta >= 1 (SnapshotJob enqueues at tier=4) and failed delta >= 1 (CommandWorkerService ResolveCommandOid returns null)
- Applied empty label filter strategy for `snmp_command_failed_total` to avoid label brittleness with `device_name=IP:port` format

## Task Commits

Each task was committed atomically:

1. **Task 1: Create tenant-cfg09-ccv-failed.yaml fixture and scenario 85 (CCV-04 command.failed)** - `065921f` (feat)

**Plan metadata:** (docs commit to follow)

## Files Created/Modified
- `tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml` - Tenant fixture with CommandName `e2e_set_unknown` (not in command map), SuppressionWindowSeconds=5, T2 OID structure
- `tests/e2e/scenarios/85-ccv04-command-failed.sh` - CCV-04 scenario asserting both dispatched and failed counters increment; uses empty filter for failed counter to avoid IP:port label brittleness

## Decisions Made
- **Empty label filter for command.failed:** The OID-not-found path in CommandWorkerService tags the failed counter with `device_name="{req.Ip}:{req.Port}"` (e.g., `"e2e-simulator.simetra.svc.cluster.local:161"`), not the tenant name. Querying with that label string risks brittleness if hostname rendering changes. Using `sum(snmp_command_failed_total) or vector(0)` (empty filter) is robust.
- **SuppressionWindowSeconds=5:** Short window prevents suppression from interfering between test run cycles. With IntervalSeconds=1, a 5s window expires quickly, allowing the next triggered evaluation to dispatch rather than suppress.
- **Wait 10s after dispatched poll for async drain:** CommandWorkerService drains the command channel asynchronously. Polling dispatched confirms SnapshotJob enqueued, then a 10s sleep allows the worker to dequeue and process, incrementing the failed counter.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- CCV-04 fixture and scenario 85 are ready for E2E execution
- Plan 68-01 (scenarios 83-84, report.sh category) must be executed before running the full E2E suite to produce a complete Command Counter Verification report section
- After 68-01 executes, all CCV scenarios (83, 84, 85) will cover indices 82-87 in the report

---
*Phase: 68-command-counters*
*Completed: 2026-03-22*
