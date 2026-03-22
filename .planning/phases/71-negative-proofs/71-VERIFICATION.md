---
phase: 71-negative-proofs
verified: 2026-03-22T19:08:51Z
status: passed
score: 7/7 must-haves verified
gaps: []
---

# Phase 71: Negative Proofs Verification Report

**Phase Goal:** The system provably suppresses, rejects, or withholds metrics in every defined negative-path scenario.
**Verified:** 2026-03-22T19:08:51Z
**Status:** PASSED
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| #  | Truth                                                                                  | Status     | Evidence                                                                 |
|----|----------------------------------------------------------------------------------------|------------|--------------------------------------------------------------------------|
| 1  | Heartbeat OID never appears in snmp_info (Counter32 never reaches RecordInfo path)    | VERIFIED  | 102: queries snmp_info{device_name="Simetra"}, asserts count == 0       |
| 2  | Heartbeat OID never has resolved_name=Unknown in snmp_gauge                           | VERIFIED  | 102: queries snmp_gauge{device_name="Simetra",resolved_name="Unknown"}, asserts count == 0 |
| 3  | Unmapped OIDs .999.2.x produce no snmp_gauge or snmp_info series                      | VERIFIED  | 103: queries by oid label for .999.2.1.0 and .999.2.2.0, both == 0     |
| 4  | Bad-community traps produce no snmp_gauge or snmp_info series                         | VERIFIED  | 104: auth_failed guard + snmp_gauge{device_name="unknown"} == 0         |
| 5  | snmp.trap.dropped stays 0 during normal E2E operation                                 | VERIFIED  | 105: activity-guarded, assert_delta_eq dropped_delta == 0               |
| 6  | Follower pods export no snmp_gauge or snmp_info series to Prometheus                  | VERIFIED  | 106: k8s_pod_name preflight + per-pod count == 0 identification         |
| 7  | Report includes Negative Proofs category covering SCENARIO_RESULTS 104-108            | VERIFIED  | report.sh line 21: "Negative Proofs|104|108" as 12th category entry     |

**Score:** 7/7 truths verified

### Required Artifacts

| Artifact                                              | Expected                              | Status    | Details                              |
|-------------------------------------------------------|---------------------------------------|-----------|--------------------------------------|
| `tests/e2e/scenarios/102-mnp01-heartbeat-not-in-snmp-info.sh` | MNP-01 heartbeat negative proof | VERIFIED | 21 lines, 1 record_pass, 1 record_fail, 1 SCENARIO_NAME, no for-loops |
| `tests/e2e/scenarios/103-mnp02-unmapped-oid-absent.sh`        | MNP-02 unmapped OID absence proof    | VERIFIED | 19 lines, 1 record_pass, 1 record_fail, 1 SCENARIO_NAME, no for-loops |
| `tests/e2e/scenarios/104-mnp03-bad-community-no-business-metric.sh` | MNP-03 bad-community metric absence | VERIFIED | 32 lines, 1 record_pass, 2 record_fail, 1 SCENARIO_NAME, no for-loops |
| `tests/e2e/scenarios/105-mnp04-trap-dropped-stays-zero.sh`    | MNP-04 trap.dropped stays zero proof | VERIFIED | 30 lines, assert_delta_eq delegates record_pass/record_fail, 1 record_fail for inactive pipeline |
| `tests/e2e/scenarios/106-mnp05-follower-no-snmp-gauge.sh`     | MNP-05 follower pod metric gating    | VERIFIED | 50 lines, 1 record_pass, 4 record_fail early exits with return/exit guards |
| `tests/e2e/lib/report.sh`                                     | Negative Proofs report category      | VERIFIED | "Negative Proofs|104|108" at line 21, 12th entry total |

### Key Link Verification

| From                                           | To                    | Via                                              | Status   | Details                                                           |
|------------------------------------------------|-----------------------|--------------------------------------------------|----------|-------------------------------------------------------------------|
| `102-mnp01-heartbeat-not-in-snmp-info.sh`      | Prometheus            | query_prometheus snmp_info{device_name="Simetra"} | WIRED   | 2 query_prometheus calls; INFO_COUNT and UNKNOWN_GAUGE_COUNT both used in condition |
| `106-mnp05-follower-no-snmp-gauge.sh`          | kubectl + Prometheus  | kubectl get pods then query by k8s_pod_name label | WIRED   | kubectl at line 17; 3 query_prometheus calls; k8s_pod_name label used for follower identification |
| `tests/e2e/lib/report.sh`                      | SCENARIO_RESULTS      | category index range 104-108                     | WIRED   | _REPORT_CATEGORIES array, "Negative Proofs|104|108" entry confirmed at line 21 |

### Requirements Coverage

| Requirement | Status    | Notes                                                                      |
|-------------|-----------|----------------------------------------------------------------------------|
| MNP-01      | SATISFIED | Scenario 102: snmp_info absence + no resolved_name=Unknown                |
| MNP-02      | SATISFIED | Scenario 103: OID-label queries for .999.2.1.0 and .999.2.2.0            |
| MNP-03      | SATISFIED | Scenario 104: auth_failed guard confirms bad-community trap arrived first  |
| MNP-04      | SATISFIED | Scenario 105: activity-guarded assert_delta_eq (non-vacuous)              |
| MNP-05      | SATISFIED | Scenario 106: k8s_pod_name preflight + follower identification loop        |

### Anti-Patterns Found

None. No TODO/FIXME/placeholder patterns found. No empty return values. No stub handlers.

Notable observations (non-blocking):

| File                                              | Line | Pattern         | Severity | Impact                                                                 |
|---------------------------------------------------|------|-----------------|----------|------------------------------------------------------------------------|
| `106-mnp05-follower-no-snmp-gauge.sh`             | 27   | `for` loop      | INFO     | Pod-iteration loop, not scenario loop. `break` after first follower found ensures exactly 1 SCENARIO_RESULTS entry. All 3 early-exit paths call `return 0 2>/dev/null \|\| exit 0` immediately after record_fail. |
| `105-mnp04-trap-dropped-stays-zero.sh`            | -    | 0 record_pass calls | INFO | Intentional: assert_delta_eq in common.sh delegates record_pass/record_fail. Verified: assert_delta_eq calls record_pass at common.sh:99 and record_fail at common.sh:101. Exactly 1 SCENARIO_RESULTS entry produced on each path. |

### Human Verification Required

None. All structural checks passed programmatically. The following are runtime-only aspects that cannot be verified from code inspection:

- Whether snmp_trap_auth_failed_total actually increments in the live cluster (MNP-03 will self-fail if not)
- Whether a follower pod exists in the cluster (MNP-05 will self-fail if not)
- Whether k8s_pod_name label is present in the live Prometheus (MNP-05 preflight handles this)

These are runtime conditions handled by the scenarios' own guard logic (record_fail paths), not structural gaps.

### Gaps Summary

No gaps. All 7 must-haves verified. All 5 scenario scripts exist, are substantive, and are wired to Prometheus (directly or via snapshot_counter/assert_delta_eq helpers). report.sh contains "Negative Proofs|104|108" as the 12th category. MNP-01 correctly does not assert snmp_gauge{device_name="Simetra"} == 0 (which would wrongly fail — heartbeat legitimately appears in snmp_gauge with resolved_name=Heartbeat). MNP-05 for-loop is pod-iteration only and produces exactly 1 SCENARIO_RESULTS entry via break + guarded early exits.

---

_Verified: 2026-03-22T19:08:51Z_
_Verifier: Claude (gsd-verifier)_
