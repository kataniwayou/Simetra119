---
phase: 75-e2e-validation-scenarios
plan: 03
subsystem: testing
tags: [e2e, bash, prometheus, tenant-metrics, snapshotjob, k8s, otlp]

# Dependency graph
requires:
  - phase: 73-tenant-metric-service
    provides: TenantMetricService with 8 OTel instruments on SnmpCollector.Tenant meter
  - phase: 74-grafana-dashboard
    provides: confirmed Prometheus label names (tenant_id, priority, k8s_pod_name)
provides:
  - Scenario 111: Unresolved evaluation path (tier=4) with tenant_state=3 and command counter delta verification
  - Scenario 112: All-instances export verification across leader + followers, confirms SnmpCollector.Tenant meter is not leader-gated
affects: [run-all.sh ordering, report.sh category coverage, TE2E-02, TE2E-03, TE2E-04]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Gauge point-in-time query pattern (query_prometheus + jq .data.result[0].value[1])
    - Per-pod metric export verification using k8s_pod_name label with kubectl pod list iteration
    - Preflight k8s_pod_name label check before per-pod loop (fail-fast if OTel label conversion not working)

key-files:
  created:
    - tests/e2e/scenarios/111-tvm05-unresolved.sh
    - tests/e2e/scenarios/112-tvm06-all-instances.sh
  modified: []

key-decisions:
  - "111 uses poll_until for command_dispatched (counter polling pattern) rather than fixed sleep, matching scenario 56 pattern"
  - "112 preflight aborts gracefully with cleanup if k8s_pod_name label absent, preventing misleading follower count=0 failure"
  - "TVM-05C measures duration count delta (not gauge point-in-time) to confirm evaluation ran without asserting specific tier counter"

patterns-established:
  - "Gauge assertion: query_prometheus direct + jq .data.result[0].value[1] | cut -d. -f1 (not snapshot_counter)"
  - "All-instances: kubectl get pods -n simetra -l app=snmp-collector + per-pod PromQL loop with k8s_pod_name label"
  - "Follower identification: GAUGE_COUNT==0 and TENANT_COUNT>0 per pod"

# Metrics
duration: 3min
completed: 2026-03-23
---

# Phase 75 Plan 03: E2E Validation Scenarios (Unresolved + All-Instances) Summary

**Bash E2E scenarios proving Unresolved tier=4 path dispatches commands (tenant_state=3, dispatched delta>0) and tenant metrics export on all replicas while snmp_gauge stays absent on followers (third meter architecture confirmed)**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-03-23T15:14:38Z
- **Completed:** 2026-03-23T15:17:18Z
- **Tasks:** 2
- **Files modified:** 2 (created)

## Accomplishments

- Scenario 111 (TVM-05): Unresolved path via evaluate violation — triggers tier=4 by setting OID 5.1=0, asserts tenant_state gauge==3, tenant_command_dispatched_total delta>0 via poll_until, and duration histogram_count delta>0
- Scenario 112 (TVM-06): All-instances export — preflight confirms k8s_pod_name label on tenant_state, then per-pod loop asserts tenant_state present on every replica; follower identification asserts snmp_gauge==0 with tenant_state>0 on at least one pod, confirming SnmpCollector.Tenant meter bypasses MetricRoleGatedExporter
- Both scenarios follow fixture-prime-assert-cleanup pattern with reset_oid_overrides before restore_configmap

## Task Commits

Each task was committed atomically:

1. **Task 1: Create scenario 111-tvm05-unresolved.sh** - `d484b1b` (feat)
2. **Task 2: Create scenario 112-tvm06-all-instances.sh** - `5ffa159` (feat)

**Plan metadata:** (pending docs commit)

## Files Created/Modified

- `tests/e2e/scenarios/111-tvm05-unresolved.sh` - TVM-05: Unresolved path, evaluate violation trigger, 3 sub-assertions (state, dispatched, duration)
- `tests/e2e/scenarios/112-tvm06-all-instances.sh` - TVM-06: All-instances export, per-pod tenant_state and snmp_gauge checks with preflight

## Decisions Made

- Used `poll_until` for TVM-05B (command_dispatched) rather than fixed sleep — matches scenario 56 behavior and avoids brittle timing dependency
- TVM-05C checks duration histogram `_count` delta rather than a tier counter — the Unresolved path does NOT increment tier3_evaluate (evaluate IS violated, so CountEvaluateNotViolated=0); duration is the cleanest proof that evaluation ran
- Scenario 112 preflight uses `tenant_state` (not `snmp_gauge`) for k8s_pod_name label check, since tenant metrics on all instances is the feature under test
- Follower identification uses dual-condition (snmp_gauge==0 AND tenant_state>0) rather than just snmp_gauge==0, to distinguish genuine followers from pods that haven't yet exported any metrics

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plans 01 and 02 (scenarios 107-110, report.sh update) remain unexecuted; they do not block 111-112 but complete the TVM suite
- All 6 TVM scenarios (107-112) use the same fixture and pattern — run-all.sh picks them up automatically via sort -V
- Phase 75 is complete once plans 01 and 02 execute

---
*Phase: 75-e2e-validation-scenarios*
*Completed: 2026-03-23*
