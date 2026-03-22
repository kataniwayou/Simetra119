---
phase: 70-label-correctness
plan: 02
subsystem: testing
tags: [e2e, prometheus, snmp, label-correctness, source-label, pss, tier4, command]

# Dependency graph
requires:
  - phase: 65-e2e-runner-fixes
    provides: E2E runner infrastructure, poll_until_log, fixture helpers
  - phase: 70-label-correctness-plan-01
    provides: MLC-01 through MLC-08 scenarios except MLC-03 (plan 02 adds MLC-03)
provides:
  - Scenario 96 (MLC-03): E2E assertion that SET command response varbinds carry source="command" on snmp_gauge
  - Tier=4 dispatch triggered via tenant-cfg06-pss-single fixture; command response label verified in Prometheus
affects: [e2e-runner, phase-71-report, future-label-regression-checks]

# Tech tracking
tech-stack:
  added: []
  patterns: [poll-then-assert label value pattern for source=command verification]

key-files:
  created:
    - tests/e2e/scenarios/96-mlc03-source-command.sh
  modified: []

key-decisions:
  - "Poll snmp_gauge with exact label filter {source='command', resolved_name='e2e_command_response'} rather than checking label post-hoc; confirms entire label set atomically"
  - "device_name on snmp_gauge is E2E-SIM (community-derived), not e2e-pss-tenant (tenant name); distinguished explicitly in dispatch vs gauge queries"
  - "Wait for snmp_command_dispatched_total increment before polling for gauge series; avoids premature poll timeout"

patterns-established:
  - "Setup-trigger-poll-assert-cleanup: fixture apply -> OID prime -> OID violate -> counter poll -> label assert -> restore"
  - "Two-phase evidence on failure: check all series for resolved_name regardless of source to distinguish missing metric vs wrong label"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 70 Plan 02: Label Correctness - MLC-03 Source=Command Summary

**E2E scenario 96 asserts snmp_gauge SET response varbinds carry source="command" by triggering tier=4 PSS dispatch via tenant-cfg06-pss-single and polling Prometheus for the label-filtered series**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T18:33:19Z
- **Completed:** 2026-03-22T18:35:03Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Created scenario 96 (MLC-03) asserting source="command" label on snmp_gauge for e2e_command_response OID
- Scenario follows exact setup/teardown pattern of CCV-01 (scenario 83) for tier=4 PSS dispatch
- Produces exactly 1 SCENARIO_RESULTS entry; cleanup (reset_oid_overrides + restore_configmap) runs unconditionally

## Task Commits

Each task was committed atomically:

1. **Task 1: Create scenario 96 (MLC-03 source=command)** - `dcc819f` (feat)

**Plan metadata:** (this commit)

## Files Created/Modified
- `tests/e2e/scenarios/96-mlc03-source-command.sh` - MLC-03: saves tenant ConfigMap, applies PSS fixture, primes T2 OIDs (8s grace), violates evaluate OID, waits for dispatch counter increment, polls for snmp_gauge{source="command", resolved_name="e2e_command_response"} with 30s deadline, asserts label value, restores fixture

## Decisions Made
- Used `poll_until` on `snmp_command_dispatched_total` to confirm dispatch before polling for the gauge series — ensures the SET response varbind has actually been processed by the pipeline
- Queried Prometheus with the full 3-label filter `{device_name="E2E-SIM", resolved_name="e2e_command_response", source="command"}` directly; if found, verifies `.data.result[0].metric.source == "command"` (which is always true given the filter, but makes the assertion explicit)
- On failure: queries all `snmp_gauge{resolved_name="e2e_command_response"}` series (without source filter) to distinguish "metric never appeared" from "metric appeared with wrong source label"

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- MLC-03 scenario complete; combined with plan 01's 7 scenarios (MLC-01, MLC-02, MLC-04 through MLC-08), the full MLC suite of 8 scenarios covers all four source values and all label dimensions
- Phase 70 label correctness suite fully complete at indices 94-101
- Ready for E2E runner integration and report category addition (if not already done in plan 01)

---
*Phase: 70-label-correctness*
*Completed: 2026-03-22*
