---
phase: 29-k8s-deployment-and-e2e-validation
plan: 02
subsystem: testing
tags: [e2e, bash, kubectl, prometheus, snmp, tenantvector, k8s]

requires:
  - phase: 29-01
    provides: tenantvector ConfigMap with PLACEHOLDER_NPB_IP/OBP_IP markers committed to deploy/k8s/snmp-collector/

provides:
  - E2E scenario 28 verifying tenantvector routing end-to-end in K8s
  - Dynamic ClusterIP substitution pattern for ConfigMap-at-deploy-time
  - Hot-reload E2E test pattern (apply 4th tenant, assert diff log)
  - report.sh Tenant Vector category (indices 33-36)
  - kubectl.sh snapshot/restore coverage for simetra-tenantvector

affects:
  - future e2e scenarios adding tenants (start indices at 37+)
  - run-all.sh (scenario 28 automatically picked up by glob)

tech-stack:
  added: []
  patterns:
    - "ClusterIP substitution: sed -e 's/PLACEHOLDER_X/${VAR}/g' | kubectl apply -f -"
    - "Inline heredoc ConfigMap apply with live shell variable interpolation"
    - "Watcher diff log detection: grep 'added' && grep 'tenant-id' across pod logs"

key-files:
  created:
    - tests/e2e/scenarios/28-tenantvector-routing.sh
  modified:
    - tests/e2e/lib/report.sh
    - tests/e2e/lib/kubectl.sh

key-decisions:
  - "FIXTURES_DIR derived from BASH_SOURCE[0] inside scenario to be safe when sourced"
  - "Snapshot taken per-scenario (not only in snapshot_configmaps) because scenario 28 manages its own pre-apply snapshot"
  - "Hot-reload uses obp_r3_power_L1 and obp_r4_power_L1 — confirmed present in oidmaps so validator accepts them"
  - "Cleanup has two-level fallback: snapshot restore -> re-apply dev file with IP substitution"

patterns-established:
  - "Scenario 28 sub-scenario pattern: 4 record_pass/fail calls with early-return on prerequisite failure"
  - "grep > /dev/null 2>&1 used consistently for all log searches to avoid SIGPIPE with pipefail"

duration: 2min
completed: 2026-03-10
---

# Phase 29 Plan 02: TenantVector E2E Scenario Summary

**E2E scenario 28 verifies full tenantvector pipeline in K8s: ClusterIP substitution, volume mount check, watcher load log, routing counter increment, and hot-reload diff detection**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-10T21:00:11Z
- **Completed:** 2026-03-10T21:01:48Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Created tests/e2e/scenarios/28-tenantvector-routing.sh with 4 sub-scenarios covering the full tenantvector pipeline
- Added "Tenant Vector" category to report.sh (indices 33-36) for scenario 28 sub-scenarios
- Extended kubectl.sh snapshot_configmaps/restore_configmaps to include simetra-tenantvector

## Task Commits

Each task was committed atomically:

1. **Task 1: Create E2E scenario 28-tenantvector-routing.sh** - `657c787` (feat)
2. **Task 2: Update report.sh categories and kubectl.sh snapshot for tenantvector** - `a98ad0f` (feat)

## Files Created/Modified
- `tests/e2e/scenarios/28-tenantvector-routing.sh` - 4-sub-scenario E2E test for tenantvector routing; derives ClusterIPs dynamically; hot-reload tests with obp-poll-2 4th tenant
- `tests/e2e/lib/report.sh` - Added "Tenant Vector|33|36" category
- `tests/e2e/lib/kubectl.sh` - snapshot_configmaps and restore_configmaps now include simetra-tenantvector

## Decisions Made
- FIXTURES_DIR is re-derived inside the scenario script using `BASH_SOURCE[0]` rather than relying on the parent's `FIXTURES_DIR` from kubectl.sh. This is safe since kubectl.sh is always sourced first and `FIXTURES_DIR` is a global, but the per-scenario derivation ensures correctness when the scenario is run standalone.
- The scenario takes its own snapshot (outside `snapshot_configmaps`) immediately before applying the IP-substituted ConfigMap so that `restore_configmaps` (which also snapshots) doesn't clobber it.
- Hot-reload 4th tenant uses `obp_r3_power_L1` and `obp_r4_power_L1` which are guaranteed to exist in the OBP oidmap and will pass the TenantVectorOptionsValidator.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Scenario 28 is ready to run as part of the full e2e suite
- report.sh Tenant Vector category will show scenario 28 sub-scenarios (indices 33-36) in the generated Markdown report
- No blockers for remaining plans in phase 29

---
*Phase: 29-k8s-deployment-and-e2e-validation*
*Completed: 2026-03-10*
