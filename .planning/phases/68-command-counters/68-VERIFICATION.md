---
phase: 68-command-counters
verified: 2026-03-22T17:30:00Z
status: passed
score: 8/8 must-haves verified
re_verification:
  previous_status: passed
  previous_score: 7/7
  gaps_closed:
    - CCV-03 assertion corrected: scenario 84 now asserts dispatched > 0 AND suppressed > 0 simultaneously (68-03)
    - CCV-04 trigger rewritten: scenario 85 now uses SET timeout to unreachable IP instead of unmapped CommandName (68-04)
    - New fixture tenant-cfg10-ccv-timeout.yaml created with valid CommandName e2e_set_bypass and command IP 10.255.255.254
    - Old fixture tenant-cfg09-ccv-failed.yaml deleted
  gaps_remaining: []
  regressions: []
---

# Phase 68: Command Counters Verification Report

**Phase Goal:** The SNMP SET command lifecycle counters correctly reflect dispatch, suppression, and failure at tier=4.
**Verified:** 2026-03-22T17:30:00Z
**Status:** passed
**Re-verification:** Yes -- after gap closure plans 68-03 and 68-04

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | snmp.command.dispatched increments when SnapshotJob enqueues a SET command at tier=4 (CCV-01, scenario 83) | VERIFIED | `83-ccv01-command-dispatched.sh` (98 lines) polls `snmp_command_dispatched_total{device_name="e2e-pss-tenant"}`, asserts delta >= 1 |
| 2 | A second tier=4 evaluation within the suppression window increments BOTH dispatched AND suppressed simultaneously (CCV-02B/CCV-03, scenario 84) | VERIFIED | `84-ccv02-03-command-suppressed.sh` Window 2 lines 122-128: asserts `DELTA_SENT_W2 -gt 0` AND `DELTA_SUPP_W2 -gt 0`; dispatched and suppressed fire together, not mutually exclusively |
| 3 | snmp.command.suppressed increments within suppression window (CCV-02B, scenario 84) | VERIFIED | `84-ccv02-03-command-suppressed.sh` line 112: `assert_delta_gt "$DELTA_SUPP_W2" 0` |
| 4 | snmp.command.failed increments when CommandWorkerService SET times out against unreachable device (CCV-04B, scenario 85) | VERIFIED | `85-ccv04-command-failed.sh` asserts `DELTA_FAILED -ge 1` using `device_name="FAKE-UNREACHABLE"` label; timeout path uses device.Name not IP:port |
| 5 | snmp.command.dispatched also increments for CCV-04 (dispatch precedes timeout failure) (CCV-04A, scenario 85) | VERIFIED | `85-ccv04-command-failed.sh` asserts `DELTA_SENT -ge 1` using `device_name="e2e-ccv-timeout"` label |
| 6 | New fixture uses valid CommandName e2e_set_bypass with unreachable command IP 10.255.255.254 | VERIFIED | `tenant-cfg10-ccv-timeout.yaml` has `CommandName: "e2e_set_bypass"` and `Ip: "10.255.255.254"`; e2e_set_bypass confirmed in `simetra-oid-command-map.yaml` line 21 |
| 7 | Old fixture with unmapped CommandName is deleted | VERIFIED | `tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml` absent from filesystem; grep count of old references in scenario 85 = 0 |
| 8 | Report category Command Counter Verification 82-88 in report.sh | VERIFIED | `tests/e2e/lib/report.sh` line 18: `"Command Counter Verification|82|88"` |

**Score:** 8/8 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `tests/e2e/scenarios/83-ccv01-command-dispatched.sh` | CCV-01 dispatched counter verification | VERIFIED | 98 lines; no shebang (sourced); no stubs; polls `snmp_command_dispatched_total{device_name="e2e-pss-tenant"}`; asserts delta >= 1 |
| `tests/e2e/scenarios/84-ccv02-03-command-suppressed.sh` | CCV-02/03 suppressed + dispatched simultaneously | VERIFIED | 143 lines; no shebang; no stubs; Window 2 asserts both `DELTA_SUPP_W2 -gt 0` and `DELTA_SENT_W2 -gt 0` at lines 122-128; gap 68-03 closed |
| `tests/e2e/scenarios/85-ccv04-command-failed.sh` | CCV-04 command.failed via SET timeout | VERIFIED | 185 lines; no shebang; no stubs; adds FAKE-UNREACHABLE device to DeviceRegistry (lines 38-58); waits 15s for DeviceWatcher reload; applies tenant-cfg10-ccv-timeout.yaml (line 69); asserts both dispatched and failed increment; restores both simetra-devices and simetra-tenants ConfigMaps in cleanup |
| `tests/e2e/fixtures/tenant-cfg10-ccv-timeout.yaml` | Tenant with valid CommandName + unreachable IP 10.255.255.254:161 | VERIFIED | 48 lines; tenant `e2e-ccv-timeout`; `SuppressionWindowSeconds: 5`; `CommandName: "e2e_set_bypass"` valid in deployed oid-command-map; `Ip: "10.255.255.254"` unreachable |
| `tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml` | MUST NOT EXIST (deleted in 68-04) | VERIFIED | File absent from filesystem; unmapped CommandName approach correctly abandoned |
| `tests/e2e/lib/report.sh` | Command Counter Verification category at 82-88 | VERIFIED | Line 18: `"Command Counter Verification|82|88"` in `_REPORT_CATEGORIES` array |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `83-ccv01-command-dispatched.sh` | `tenant-cfg06-pss-single.yaml` | `kubectl apply` at line 25 | WIRED | File referenced and applied |
| `84-ccv02-03-command-suppressed.sh` | `tenant-cfg06-pss-suppression.yaml` | `kubectl apply` at line 30 | WIRED | File referenced and applied |
| `85-ccv04-command-failed.sh` | `tenant-cfg10-ccv-timeout.yaml` | `kubectl apply` at line 69 | WIRED | Applied after FAKE-UNREACHABLE device registration; fixture confirmed at 48 lines |
| `85-ccv04-command-failed.sh` | `simetra-devices` ConfigMap | jq-built ConfigMap at lines 38-58 | WIRED | Reads current devices.json via jsonpath, appends FAKE-UNREACHABLE entry with jq, applies via temp file; 15s wait for DeviceWatcher reload |
| CommandWorkerService timeout path | `snmp_command_failed_total{device_name="FAKE-UNREACHABLE"}` | OperationCanceledException fires `IncrementCommandFailed(device.Name)` | WIRED | `e2e_set_bypass` is valid so command reaches SET layer; 10.255.255.254 unreachable so SET times out after 0.8s; device.Name=FAKE-UNREACHABLE from CommunityString Simetra.FAKE-UNREACHABLE |
| `84-ccv02-03-command-suppressed.sh` CCV-03 assertion | dispatched AND suppressed both > 0 | Lines 122-128: `DELTA_SENT_W2 -gt 0 AND DELTA_SUPP_W2 -gt 0` | WIRED | Positive proof both counters fire simultaneously; gap 68-03 closed |
| All 3 scenarios | `run-all.sh` | `[0-9]*.sh` glob at line 87 | WIRED | Glob includes 83/84/85 automatically in numeric order |

### Requirements Coverage

| Requirement | Status | Notes |
|-------------|--------|-------|
| CCV-01: snmp.command.dispatched increments at tier=4 dispatch | SATISFIED | Scenario 83, sub-assertion index 82 |
| CCV-02A: snmp.command.dispatched increments on first tier=4 (Window 1) | SATISFIED | Scenario 84 sub-assertion 84a, index 83 |
| CCV-02B: snmp.command.suppressed increments within suppression window | SATISFIED | Scenario 84 sub-assertion 84b, index 84 |
| CCV-03: snmp.command.dispatched AND snmp.command.suppressed both fire during suppression window | SATISFIED | Scenario 84 sub-assertion 84c, index 85; positive assertion (both > 0); gap 68-03 closed |
| CCV-04A: snmp.command.dispatched increments before failure (dispatch precedes timeout) | SATISFIED | Scenario 85 sub-assertion 85a, index 86 |
| CCV-04B: snmp.command.failed increments on SET timeout to unreachable device | SATISFIED | Scenario 85 sub-assertion 85b, index 87; timeout path not unmapped CommandName; gap 68-04 closed |

### Anti-Patterns Found

None. All three scenario files are free of TODO/FIXME, placeholder text, empty handlers, and stub patterns. All follow the established save/apply/prime/grace/baseline/trigger/poll/assert/cleanup pattern. Scenario 85 contains no references to the old fixture name (`tenant-cfg09`) or the old approach (`e2e_set_unknown`): grep count confirms 0 matches for both patterns.

### Human Verification Required

The following items require a live cluster to verify:

#### 1. CCV-01 Counter Actually Increments at Runtime

**Test:** Run scenario 83 against the live E2E cluster.
**Expected:** `snmp_command_dispatched_total{device_name="e2e-pss-tenant"}` increments >= 1 within 30s of setting OID 5.1 to 0.
**Why human:** Prometheus polling requires a running cluster with the SnmpCollector pod and working OTel export pipeline.

#### 2. CCV-02B / CCV-03 Simultaneous Counter Firing

**Test:** Run scenario 84. Verify that Window 2 (after 15s stabilization sleep) still falls within the 30s suppression window when the second SnapshotJob cycle fires.
**Expected:** Both `snmp_command_suppressed_total` and `snmp_command_dispatched_total` increment > 0 during Window 2, confirming SnapshotJob TryWrite (dispatched++) then TrySuppress (suppressed++) execution sequence.
**Why human:** Timing correctness (15s stabilization + ~1s poll interval vs. 30s suppression window) must be validated in the actual scheduler environment.

#### 3. CCV-04 Failure Path via SET Timeout

**Test:** Run scenario 85 against the live E2E cluster.
**Expected:** `snmp_command_dispatched_total{device_name="e2e-ccv-timeout"}` increments >= 1, then `snmp_command_failed_total{device_name="FAKE-UNREACHABLE"}` increments >= 1 within 15s of the dispatch being observed.
**Why human:** Requires confirming (a) DeviceWatcher registers FAKE-UNREACHABLE within 15s of ConfigMap update; (b) TenantVectorWatcher loads the Commands entry because TryGetByIpPort succeeds; (c) CommandWorkerService SET to 10.255.255.254:161 actually times out in 0.8s on the live cluster network; (d) OTel exports the failed counter within the 15s wait.

### Gaps Summary

No gaps. All 8 must-haves are verified at existence, substantive, and wiring levels. Gap closure plans 68-03 and 68-04 are confirmed complete:

**68-03 closed:** CCV-03 assertion in scenario 84 (lines 122-128) now asserts `DELTA_SENT_W2 -gt 0` AND `DELTA_SUPP_W2 -gt 0` simultaneously. The previous negative assertion (`DELTA_SENT_W2 -eq 0`) was incorrect because SnapshotJob increments dispatched before checking suppression -- suppression blocks worker execution, not queue enqueue.

**68-04 closed:** Scenario 85 now triggers command.failed via a real SET timeout (0.8s) to an unreachable IP (10.255.255.254), replacing the unmapped CommandName approach. The new fixture `tenant-cfg10-ccv-timeout.yaml` uses `CommandName: "e2e_set_bypass"` (confirmed in the deployed K8s oid-command-map) so TenantVectorWatcherService does not skip the tenant at load time. Scenario 85 pre-registers FAKE-UNREACHABLE in the DeviceRegistry before applying the tenant fixture so `TryGetByIpPort(10.255.255.254, 161)` succeeds. The old fixture `tenant-cfg09-ccv-failed.yaml` is confirmed deleted from the filesystem.

---

_Verified: 2026-03-22T17:30:00Z_
_Verifier: Claude (gsd-verifier)_
