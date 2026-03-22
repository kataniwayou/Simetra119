---
phase: 70-label-correctness
verified: 2026-03-22T18:40:36Z
status: gaps_found
score: 6/8 must-haves verified
gaps:
  - truth: resolved_name label matches oidmaps.json mapping for a representative OID
    status: failed
    reason: >-
      100-mlc07-resolved-name.sh sorts at suite execution position 11 because bash
      glob lexicographic ordering places 100- before 11- (character 0 less than 1).
      SCENARIO_RESULTS entry lands at runtime index approx 10, outside Label Correctness|96|103.
    artifacts:
      - path: tests/e2e/scenarios/100-mlc07-resolved-name.sh
        issue: Filename prefix 100- sorts before 11- lexicographically; executes at position 11 not 100
    missing:
      - Switch run-all.sh glob to use sort -V so 100- sorts after 99-
  - truth: device_name label equals E2E-SIM for community-derived device
    status: failed
    reason: >-
      101-mlc08-device-name.sh sorts at suite position 12 for the same lexicographic reason.
      Entry lands at runtime index approx 11, outside Label Correctness|96|103.
    artifacts:
      - path: tests/e2e/scenarios/101-mlc08-device-name.sh
        issue: Filename prefix 101- sorts before 11- lexicographically; executes at position 12 not 101
    missing:
      - Same root fix as 100-mlc07
      - At runtime entries from these files appear in Business Metrics|10|22 not Label Correctness
---

# Phase 70: Label Correctness Verification Report

**Phase Goal:** Every metric exported to Prometheus carries the correct source, snmp_type, resolved_name, and device_name labels.
**Verified:** 2026-03-22T18:40:36Z
**Status:** gaps_found
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | snmp_gauge e2e_gauge_test carries source=poll | VERIFIED | 94-mlc01-source-poll.sh: 18 lines, syntax OK, 1 SCENARIO_RESULTS entry |
| 2 | snmp_gauge e2e_gauge_test carries source=trap | VERIFIED | 95-mlc02-source-trap.sh: 27 lines, syntax OK, 45s poll loop |
| 3 | snmp_gauge e2e_command_response carries source=command | VERIFIED | 96-mlc03-source-command.sh: 130 lines, full fixture lifecycle, 1 entry |
| 4 | snmp_gauge e2e_total_util carries source=synthetic | VERIFIED | 97-mlc04-source-synthetic.sh: 19 lines, syntax OK |
| 5 | snmp_type matches for all 5 numeric types | VERIFIED | 98-mlc05-snmptype-gauge.sh: 88 lines, PASS_TYPES accumulator, 1 entry |
| 6 | snmp_type matches for both string types | VERIFIED | 99-mlc06-snmptype-info.sh: 43 lines, PASS_TYPES accumulator, 1 entry |
| 7 | resolved_name matches oidmaps.json for OID 1.3.6.1.4.1.47477.999.1.1.0 | FAILED | 100-mlc07-resolved-name.sh exists and valid bash but sorts at suite position 11; entry at index approx 10, not in Label Correctness|96|103 |
| 8 | device_name equals E2E-SIM for community-derived device | FAILED | 101-mlc08-device-name.sh exists and valid bash but sorts at suite position 12; entry at index approx 11, not in Label Correctness|96|103 |

**Score:** 6/8 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| tests/e2e/scenarios/94-mlc01-source-poll.sh | MLC-01 source=poll assertion | VERIFIED | 18 lines, syntax OK |
| tests/e2e/scenarios/95-mlc02-source-trap.sh | MLC-02 source=trap assertion | VERIFIED | 27 lines, syntax OK, poll loop |
| tests/e2e/scenarios/96-mlc03-source-command.sh | MLC-03 source=command with tier=4 | VERIFIED | 130 lines, syntax OK, full fixture lifecycle wired |
| tests/e2e/scenarios/97-mlc04-source-synthetic.sh | MLC-04 source=synthetic | VERIFIED | 19 lines, syntax OK |
| tests/e2e/scenarios/98-mlc05-snmptype-gauge.sh | MLC-05 snmp_type 5 numeric | VERIFIED | 88 lines, PASS_TYPES unit assertion |
| tests/e2e/scenarios/99-mlc06-snmptype-info.sh | MLC-06 snmp_type 2 string | VERIFIED | 43 lines, PASS_TYPES unit assertion |
| tests/e2e/scenarios/100-mlc07-resolved-name.sh | MLC-07 resolved_name cross-ref | ORPHANED | 20 lines, syntax OK, content correct; sort position 11 puts entry at index approx 10, outside Label Correctness category |
| tests/e2e/scenarios/101-mlc08-device-name.sh | MLC-08 device_name | ORPHANED | 18 lines, syntax OK, content correct; sort position 12 puts entry at index approx 11, outside Label Correctness category |
| tests/e2e/lib/report.sh | Label Correctness|96|103 category | PARTIAL | Entry at line 20; only 6 of 8 MLC entries land in range at runtime |
| tests/e2e/fixtures/tenant-cfg06-pss-single.yaml | Tier=4 dispatch fixture | VERIFIED | File exists |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| 96-mlc03-source-command.sh | fixtures/tenant-cfg06-pss-single.yaml | kubectl apply | WIRED | Line 26 present |
| 96-mlc03-source-command.sh | snmp_gauge source=command | query_prometheus poll loop | WIRED | Lines 84-97 present |
| 96-mlc03-source-command.sh | cleanup | restore_configmap + reset_oid_overrides | WIRED | Lines 122+126 both unconditionally present |
| 100-mlc07-resolved-name.sh | Label Correctness category | SCENARIO_RESULTS index | NOT_WIRED | Sort position 11, entry at index approx 10, category is 96-103 |
| 101-mlc08-device-name.sh | Label Correctness category | SCENARIO_RESULTS index | NOT_WIRED | Sort position 12, entry at index approx 11, category is 96-103 |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|----------------|
| MLC-01: source=poll | SATISFIED | scenario 94 verified |
| MLC-02: source=trap | SATISFIED | scenario 95 verified |
| MLC-03: source=command | SATISFIED | scenario 96 verified |
| MLC-04: source=synthetic | SATISFIED | scenario 97 verified |
| MLC-05: snmp_type 5 numeric | SATISFIED | scenario 98 verified |
| MLC-06: snmp_type 2 string | SATISFIED | scenario 99 verified |
| MLC-07: resolved_name cross-reference | BLOCKED | scenario 100 executes at wrong suite position |
| MLC-08: device_name | BLOCKED | scenario 101 executes at wrong suite position |

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| tests/e2e/scenarios/100-mlc07-resolved-name.sh | Filename prefix 100- sorts before 11- in bash glob lexicographic order | Blocker | MLC-07 entry appears in Business Metrics category at runtime |
| tests/e2e/scenarios/101-mlc08-device-name.sh | Filename prefix 101- sorts before 11- in bash glob lexicographic order | Blocker | MLC-08 entry appears in Business Metrics category at runtime |

### Gaps Summary

Two scenario files have a filename sort-order defect. The run-all.sh glob pattern
for scenario in "$SCRIPT_DIR"/scenarios/[0-9]*.sh expands using bash lexicographic sort.
Under lexicographic ordering 100 < 11 < 12 because the second character 0 < 1.
The confirmed execution order at the suite boundary is:

  sort position 10: 10-trap-dropped.sh
  sort position 11: 100-mlc07-resolved-name.sh   (intended: approximately position 100)
  sort position 12: 101-mlc08-device-name.sh      (intended: approximately position 101)
  sort position 13: 11-gauge-labels-e2e-sim.sh

SCENARIO_RESULTS entries from 100- and 101- land at indices approximately 10 and 11.
The Label Correctness|96|103 category starts at index 96. At runtime, MLC-07 and
MLC-08 entries appear in the Business Metrics|10|22 report section.

The scenario content in both files is correct: valid bash syntax, substantive
Prometheus queries, correct label assertions, exactly 1 SCENARIO_RESULTS entry each.
The defect is purely in filename ordering affecting suite execution position.

Minimal fix: change run-all.sh loop to use sort -V (version/numeric sort).
sort -V correctly places 100- and 101- after 99-.

---

_Verified: 2026-03-22T18:40:36Z_
_Verifier: Claude (gsd-verifier)_
