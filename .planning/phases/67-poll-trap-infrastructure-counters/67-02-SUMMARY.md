---
phase: 67-poll-trap-infrastructure-counters
plan: 02
subsystem: testing
tags: [e2e, snmp, prometheus, poll, unreachable, recovery, tenantvector, configmap, kubectl]

requires:
  - phase: 67-01
    provides: Infrastructure counter instruments (snmp.poll.unreachable, snmp.poll.recovered, snmp.tenantvector.routed) registered in OTel

provides:
  - E2E scenario 80 (MCV-11): verifies snmp_poll_unreachable_total increments after 3 consecutive poll failures to unreachable IP
  - E2E scenario 81 (MCV-12): verifies snmp_poll_recovered_total increments when previously-unreachable device becomes reachable
  - E2E scenario 82 (MCV-13): verifies snmp_tenantvector_routed_total increments on tenant vector fan-out write

affects:
  - 67-03-PLAN (remaining infrastructure counter scenarios)
  - E2E run-all.sh (scenarios 80-82 automatically picked up by filename ordering)

tech-stack:
  added: []
  patterns:
    - "MCV scenario: pre-recovery step before unreachable test to reset DeviceUnreachabilityTracker singleton"
    - "Sequential scenario dependency: scenario N leaves cluster state for scenario N+1"
    - "Belt-and-suspenders: apply tenants ConfigMap unconditionally before routing counter test"

key-files:
  created:
    - tests/e2e/scenarios/80-mcv11-poll-unreachable.sh
    - tests/e2e/scenarios/81-mcv12-poll-recovered.sh
    - tests/e2e/scenarios/82-mcv13-tenantvector-routed.sh
  modified: []

key-decisions:
  - "Scenario 80 does NOT restore ConfigMap at end -- leaves FAKE-UNREACHABLE in unreachable state for scenario 81"
  - "Scenario 82 does NOT restart deployment -- watcher hot-reloads via file system watch, restart unnecessary and slow"
  - "Scenario 82 applies simetra-tenants.yaml unconditionally (idempotency guard against scenario 28 cleanup)"

patterns-established:
  - "Pre-recovery idempotency pattern: add device at reachable IP + sleep 20s before switching to unreachable IP"
  - "ORIGINAL_CM saved by scenario N, restored by scenario N+1 -- cross-scenario cleanup chain"

duration: 2min
completed: 2026-03-22
---

# Phase 67 Plan 02: Poll Unreachable, Recovered, and TenantVector Routed E2E Scenarios Summary

**Three E2E scenarios (80-82) verifying snmp.poll.unreachable, snmp.poll.recovered, and snmp.tenantvector.routed counters using ConfigMap mutation patterns and the idempotency pre-recovery step**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T15:32:43Z
- **Completed:** 2026-03-22T15:34:14Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Scenario 80 (MCV-11): poll.unreachable verification with mandatory idempotency pre-recovery step that resets DeviceUnreachabilityTracker before the unreachable test, ensuring consistent behavior across repeated runs
- Scenario 81 (MCV-12): poll.recovered verification that patches FAKE-UNREACHABLE back to a reachable IP, asserts recovery counter increment, then restores original ConfigMap — depends on scenario 80's state
- Scenario 82 (MCV-13): tenantvector.routed verification that applies simetra-tenants.yaml unconditionally and polls for fan-out write counter increment without needing a deployment restart

## Task Commits

Each task was committed atomically:

1. **Task 1: Create scenarios 80-81 (poll.unreachable and poll.recovered)** - `a794e7a` (feat)
2. **Task 2: Create scenario 82 (tenantvector.routed)** - `ef796f0` (feat)

**Plan metadata:** (pending docs commit)

## Files Created/Modified

- `tests/e2e/scenarios/80-mcv11-poll-unreachable.sh` - MCV-11: asserts snmp_poll_unreachable_total increments after 3 poll failures to 10.255.255.254
- `tests/e2e/scenarios/81-mcv12-poll-recovered.sh` - MCV-12: asserts snmp_poll_recovered_total increments when FAKE-UNREACHABLE IP patched to e2e-simulator
- `tests/e2e/scenarios/82-mcv13-tenantvector-routed.sh` - MCV-13: asserts snmp_tenantvector_routed_total increments after tenants ConfigMap applied

## Decisions Made

- Scenario 80 intentionally omits ConfigMap restore at the end: FAKE-UNREACHABLE must remain in unreachable state so scenario 81 can exercise the recovery transition from a known-unreachable device.
- Scenario 82 skips deployment restart: the tenant watcher uses file system watches and hot-reloads automatically. Adding a rollout restart would add 90+ seconds with no benefit.
- Scenario 82 applies simetra-tenants.yaml unconditionally: scenario 28 restores the original (possibly empty) tenants ConfigMap at cleanup, so scenario 82 must re-apply to guarantee tenants are active.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Scenarios 80-82 are ready for integration into the E2E suite; run-all.sh picks them up by filename order
- Phase 67 plan 03 can proceed to cover remaining infrastructure counter scenarios
- FAKE-UNREACHABLE fixture (fake-device-configmap.yaml) and the pre-recovery pattern are proven and reusable

---
*Phase: 67-poll-trap-infrastructure-counters*
*Completed: 2026-03-22*
