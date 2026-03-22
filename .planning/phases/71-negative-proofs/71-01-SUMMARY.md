---
phase: 71-negative-proofs
plan: 01
subsystem: testing
tags: [e2e, bash, prometheus, promql, snmp, kubernetes, negative-proofs]

# Dependency graph
requires:
  - phase: 70-label-correctness
    provides: MLC scenario scripts and SCENARIO_RESULTS indices 96-103 base
  - phase: 68-pipeline-counter-verification
    provides: MCV scenario patterns (auth-failed guard, delta==0 guard)
provides:
  - MNP-01 through MNP-05 negative-proof E2E scenario scripts (102-106)
  - Negative Proofs report category covering SCENARIO_RESULTS 104-108
  - Heartbeat snmp_info absence and Unknown-label absence proofs
  - Unmapped OID baseline-absence proofs
  - Bad-community business-metric absence proof extending MCV-09b
  - Trap-dropped activity-guarded zero-delta proof
  - Follower-pod metric gating proof via k8s_pod_name Prometheus label
affects:
  - future phases adding scenarios (next SCENARIO_RESULTS index is 109)
  - phase 72+ report category additions (next category starts at index 109)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - absence assertion via query_prometheus + jq .data.result length == 0
    - k8s_pod_name preflight pattern for follower identification
    - assert_delta_eq for zero-delta with activity guard (from MCV-07)
    - auth_failed guard pattern before business-metric absence check (from MCV-09b)

key-files:
  created:
    - tests/e2e/scenarios/102-mnp01-heartbeat-not-in-snmp-info.sh
    - tests/e2e/scenarios/103-mnp02-unmapped-oid-absent.sh
    - tests/e2e/scenarios/104-mnp03-bad-community-no-business-metric.sh
    - tests/e2e/scenarios/105-mnp04-trap-dropped-stays-zero.sh
    - tests/e2e/scenarios/106-mnp05-follower-no-snmp-gauge.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "MNP-01 reframed: heartbeat legitimately appears in snmp_gauge{resolved_name=Heartbeat} -- proof is absence from snmp_info and absence with resolved_name=Unknown"
  - "MNP-05 uses Prometheus k8s_pod_name label query (not kubectl port-forward) since snmp-collector pods expose no /metrics endpoint"
  - "MNP-04 uses assert_delta_eq pattern from MCV-07 (delegate to helper) consistent with project conventions"

patterns-established:
  - "Negative proof: query_prometheus + jq length == 0 (absence assertion pattern)"
  - "k8s_pod_name preflight: verify label exists before follower identification loop"
  - "Non-vacuous negative proof: always confirm pipeline activity before asserting zero-delta"

# Metrics
duration: 8min
completed: 2026-03-22
---

# Phase 71 Plan 01: Negative Proofs Summary

**Five E2E negative-proof scenarios (MNP-01 through MNP-05) proving the system suppresses heartbeat leaks, unmapped OIDs, bad-community traps, channel overflow, and follower-pod metric emission**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-22T19:03:30Z
- **Completed:** 2026-03-22T19:11:00Z
- **Tasks:** 2
- **Files modified:** 6 (5 created, 1 modified)

## Accomplishments

- Created five scenario scripts (102-106) covering all defined negative paths in v2.3
- MNP-01 correctly reframed: proves heartbeat absent from snmp_info and absent with resolved_name=Unknown (not snmp_gauge absence, which would fail -- heartbeat legitimately appears in snmp_gauge)
- MNP-05 identifies follower pods via Prometheus k8s_pod_name label with k8s_pod_name preflight guard
- Added Negative Proofs category to report.sh as 12th entry covering SCENARIO_RESULTS indices 104-108

## Task Commits

Each task was committed atomically:

1. **Task 1: Create MNP-01 through MNP-05 scenario scripts** - `e5ec7bc` (feat)
2. **Task 2: Add Negative Proofs report category** - `19694ee` (feat)

**Plan metadata:** (included in this docs commit)

## Files Created/Modified

- `tests/e2e/scenarios/102-mnp01-heartbeat-not-in-snmp-info.sh` - Heartbeat absent from snmp_info and never resolved_name=Unknown
- `tests/e2e/scenarios/103-mnp02-unmapped-oid-absent.sh` - OIDs .999.2.x absent from snmp_gauge and snmp_info
- `tests/e2e/scenarios/104-mnp03-bad-community-no-business-metric.sh` - Bad-community traps produce no snmp_gauge/snmp_info (extends MCV-09b)
- `tests/e2e/scenarios/105-mnp04-trap-dropped-stays-zero.sh` - trap.dropped delta==0 with activity guard
- `tests/e2e/scenarios/106-mnp05-follower-no-snmp-gauge.sh` - Follower pod exports no snmp_gauge/snmp_info via k8s_pod_name label query
- `tests/e2e/lib/report.sh` - Added "Negative Proofs|104|108" as 12th category entry

## Decisions Made

- **MNP-01 reframe:** The heartbeat OID legitimately produces `snmp_gauge{resolved_name="Heartbeat", device_name="Simetra"}` on the leader (OidMapService.MergeWithHeartbeatSeed injects it). Asserting snmp_gauge absence would always fail. MNP-01 instead proves: (1) heartbeat never appears in snmp_info (Counter32 goes to RecordGauge, not RecordInfo), and (2) heartbeat never has resolved_name="Unknown" (correctly seeded). Both are genuine negative proofs.

- **MNP-05 approach:** snmp-collector pods have no /metrics HTTP endpoint (metrics flow OTLP gRPC to otel-collector). Follower identification uses Prometheus k8s_pod_name labels (from resource_to_telemetry_conversion.enabled: true in otel-collector config). Leader will have snmp_gauge series; followers will have 0.

- **MNP-04 assert_delta_eq:** Uses the established project pattern (same as MCV-07) where assert_delta_eq delegates record_pass/record_fail to the common library helper.

## Deviations from Plan

None - plan executed exactly as written. MNP-01 reframe was pre-specified in the plan based on research findings.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- v2.3 Metric Validity & Correctness milestone is complete: positive paths (phases 68-70) and negative paths (phase 71) all covered
- Next SCENARIO_RESULTS index available: 109
- Next report category starts at index 109
- No blockers

---
*Phase: 71-negative-proofs*
*Completed: 2026-03-22*
