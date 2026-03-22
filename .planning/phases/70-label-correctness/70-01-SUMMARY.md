---
phase: 70-label-correctness
plan: 01
subsystem: testing
tags: [e2e, bash, prometheus, snmp_gauge, snmp_info, labels, source, snmp_type, resolved_name, device_name]

# Dependency graph
requires:
  - phase: 69-business-metric-value-correctness
    provides: MVC scenarios at indices 88-95; SCENARIO_RESULTS baseline
  - phase: 65-e2e-runner-fixes
    provides: stable E2E runner, report.sh category infrastructure
provides:
  - 7 label-correctness E2E scenario files (94, 95, 97-101)
  - Label Correctness category in report.sh (indices 96-103)
  - MLC-01: source=poll on snmp_gauge e2e_gauge_test
  - MLC-02: source=trap on snmp_gauge e2e_gauge_test (45s poll loop)
  - MLC-04: source=synthetic on snmp_gauge e2e_total_util
  - MLC-05: snmp_type correct for all 5 snmp_gauge types (1 entry unit assertion)
  - MLC-06: snmp_type correct for both snmp_info types (octetstring, ipaddress)
  - MLC-07: resolved_name cross-references oidmaps.json for OID 1.3.6.1.4.1.47477.999.1.1.0
  - MLC-08: device_name=E2E-SIM derived from community string Simetra.E2E-SIM
affects: [70-02-plan, run-all.sh, e2e reporting]

# Tech tracking
tech-stack:
  added: []
  patterns: [multi-type unit assertion (PASS_TYPES counter, single record_pass/fail)]

key-files:
  created:
    - tests/e2e/scenarios/94-mlc01-source-poll.sh
    - tests/e2e/scenarios/95-mlc02-source-trap.sh
    - tests/e2e/scenarios/97-mlc04-source-synthetic.sh
    - tests/e2e/scenarios/98-mlc05-snmptype-gauge.sh
    - tests/e2e/scenarios/99-mlc06-snmptype-info.sh
    - tests/e2e/scenarios/100-mlc07-resolved-name.sh
    - tests/e2e/scenarios/101-mlc08-device-name.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "MLC-05 checks all 5 snmp_type values as a unit (PASS_TYPES counter) to produce 1 SCENARIO_RESULTS entry"
  - "MLC-02 uses 45s poll loop because traps fire every 30s and data may not yet be present"
  - "MLC-04 queries by resolved_name=e2e_total_util without OID filter (synthetic uses oid=0.0 sentinel)"
  - "MLC-07 queries by OID label to cross-reference mapping rather than filtering by resolved_name directly"

patterns-established:
  - "Multi-type unit assertion: accumulate PASS_TYPES, emit single record_pass/record_fail at the end"
  - "Static source-label assertion: query with source= filter, read label, assert exact value"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 70 Plan 01: Label Correctness Scenarios Summary

**7 E2E label-correctness scenarios asserting source (poll/trap/synthetic), snmp_type (7 values), resolved_name (OID cross-reference), and device_name (community derivation) labels on snmp_gauge and snmp_info**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T18:33:10Z
- **Completed:** 2026-03-22T18:35:24Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments

- Added "Label Correctness|96|103" category to report.sh, completing the report structure for the full MLC suite
- Created 7 scenario files (94, 95, 97-101) covering all 4 label dimensions: source, snmp_type, resolved_name, device_name
- MLC-05 uses a PASS_TYPES counter pattern to assert all 5 numeric snmp_type values as a single unit with one SCENARIO_RESULTS entry

## Task Commits

Each task was committed atomically:

1. **Task 1: Add report category and create source-label scenarios (94, 95, 97)** - `a652c22` (feat)
2. **Task 2: Create snmp_type and resolved_name and device_name scenarios (98-101)** - `bcac513` (feat)

**Plan metadata:** _(pending docs commit)_

## Files Created/Modified

- `tests/e2e/lib/report.sh` - Added "Label Correctness|96|103" category entry
- `tests/e2e/scenarios/94-mlc01-source-poll.sh` - MLC-01: assert source=poll on e2e_gauge_test
- `tests/e2e/scenarios/95-mlc02-source-trap.sh` - MLC-02: assert source=trap (45s poll loop for trap timing)
- `tests/e2e/scenarios/97-mlc04-source-synthetic.sh` - MLC-04: assert source=synthetic on e2e_total_util
- `tests/e2e/scenarios/98-mlc05-snmptype-gauge.sh` - MLC-05: unit-assert all 5 numeric snmp_type values
- `tests/e2e/scenarios/99-mlc06-snmptype-info.sh` - MLC-06: assert octetstring and ipaddress in snmp_info
- `tests/e2e/scenarios/100-mlc07-resolved-name.sh` - MLC-07: cross-reference OID to resolved_name label
- `tests/e2e/scenarios/101-mlc08-device-name.sh` - MLC-08: assert device_name=E2E-SIM from community string

## Decisions Made

- MLC-05 checks all 5 snmp_type values via a PASS_TYPES accumulator (one `record_pass`/`record_fail` at the end) rather than per-type entries, keeping the SCENARIO_RESULTS index compact.
- MLC-02 uses a 45s poll loop (3s interval) as a safety net because traps fire every 30s — data should already be present from scenario 16, but the wait ensures robustness if the suite is run in isolation.
- MLC-04 queries `resolved_name="e2e_total_util"` without an OID filter because synthetic metrics always carry `oid="0.0"` (sentinel value, not a real OID).
- MLC-07 queries by the OID label rather than the resolved_name to explicitly verify the OID-to-name mapping in the live system.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- 7 of 8 MLC scenarios complete; plan 02 adds scenario 96 (MLC-03: source=command) to complete the Label Correctness suite
- All 7 files pass `bash -n` syntax check
- report.sh category covers indices 96-103 ready for all 8 entries

---
*Phase: 70-label-correctness*
*Completed: 2026-03-22*
