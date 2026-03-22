---
phase: 69-business-metric-value-correctness
plan: 01
subsystem: testing
tags: [e2e, snmp, prometheus, snmp_gauge, snmp_info, bash, scenario]

# Dependency graph
requires:
  - phase: 68-command-counters
    provides: CCV scenarios 83-85 establishing the SCENARIO_RESULTS index baseline for report.sh
provides:
  - 7 static E2E scenario files (86-92) asserting exact SNMP values in Prometheus
  - Corrected CCV report category range (|82|87|, was |82|88|)
  - New report category "Business Metric Value Correctness|88|95"
affects:
  - 69-02 (MVC-08 value-change scenario will land at index 95 in the reserved range)
  - future report.sh changes (must preserve |88|95 range for MVC scenarios)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Static exact-value assertion: query_prometheus, extract VALUE_INT via cut -d. -f1, compare with [ = ]"
    - "snmp_info string assertion: extract .metric.value label AND confirm .value[1] = 1"
    - "One record_pass/record_fail per scenario file (exactly 1 SCENARIO_RESULTS entry)"

key-files:
  created:
    - tests/e2e/scenarios/86-mvc01-gauge32-exact.sh
    - tests/e2e/scenarios/87-mvc02-integer32-exact.sh
    - tests/e2e/scenarios/88-mvc03-counter32-exact.sh
    - tests/e2e/scenarios/89-mvc04-counter64-exact.sh
    - tests/e2e/scenarios/90-mvc05-timeticks-exact.sh
    - tests/e2e/scenarios/91-mvc06-info-octetstring-exact.sh
    - tests/e2e/scenarios/92-mvc07-info-ipaddress-exact.sh
  modified:
    - tests/e2e/lib/report.sh

key-decisions:
  - "CCV range corrected from |82|88 to |82|87 -- CCV produces exactly 6 SCENARIO_RESULTS entries (indices 82-87)"
  - "MVC range set to |88|95 (8 slots) to reserve space for MVC-08 in phase 69-02"
  - "MVC-07 IpAddress assertion uses exact string '10.0.0.1' -- actual format logged in EVIDENCE so failures reveal actual value"
  - "Counter32/Counter64 asserted as raw gauge values (no rate/delta) -- confirmed by OtelMetricHandler calling RecordGauge for all 5 numeric types"

patterns-established:
  - "Pattern: snmp_gauge exact-value -- query by resolved_name, cut -d. -f1 for integer part, compare with [ = ]"
  - "Pattern: snmp_info exact-value -- assert .metric.value string AND .value[1] = 1 in single if-check"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 69 Plan 01: Business Metric Value Correctness Summary

**7 static E2E scenarios (MVC-01 through MVC-07) asserting exact Prometheus values for all mapped .999.1.x SNMP types, plus corrected CCV report range and new MVC report category**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T18:03:15Z
- **Completed:** 2026-03-22T18:04:50Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments

- Created 5 snmp_gauge scenarios (MVC-01 through MVC-05) asserting exact values for Gauge32=42, Integer32=100, Counter32=5000, Counter64=1000000, TimeTicks=360000
- Created 2 snmp_info scenarios (MVC-06, MVC-07) asserting exact string value labels E2E-TEST-VALUE and 10.0.0.1
- Fixed report.sh CCV category off-by-one (|82|88 -> |82|87) and added "Business Metric Value Correctness|88|95"

## Task Commits

Each task was committed atomically:

1. **Task 1: Create 5 snmp_gauge exact-value scenarios (MVC-01 through MVC-05)** - `2ff9b31` (feat)
2. **Task 2: Create 2 snmp_info scenarios (MVC-06, MVC-07) and update report categories** - `f25728a` (feat)

## Files Created/Modified

- `tests/e2e/scenarios/86-mvc01-gauge32-exact.sh` - MVC-01: snmp_gauge Gauge32 exact value assertion (expected 42)
- `tests/e2e/scenarios/87-mvc02-integer32-exact.sh` - MVC-02: snmp_gauge Integer32 exact value assertion (expected 100)
- `tests/e2e/scenarios/88-mvc03-counter32-exact.sh` - MVC-03: snmp_gauge Counter32 exact value assertion (expected 5000)
- `tests/e2e/scenarios/89-mvc04-counter64-exact.sh` - MVC-04: snmp_gauge Counter64 exact value assertion (expected 1000000)
- `tests/e2e/scenarios/90-mvc05-timeticks-exact.sh` - MVC-05: snmp_gauge TimeTicks exact value assertion (expected 360000)
- `tests/e2e/scenarios/91-mvc06-info-octetstring-exact.sh` - MVC-06: snmp_info OctetString value label assertion (expected E2E-TEST-VALUE)
- `tests/e2e/scenarios/92-mvc07-info-ipaddress-exact.sh` - MVC-07: snmp_info IpAddress value label assertion (expected 10.0.0.1)
- `tests/e2e/lib/report.sh` - Corrected CCV range to |82|87|, added Business Metric Value Correctness|88|95

## Decisions Made

- CCV report range was `|82|88` but CCV actually produces exactly 6 SCENARIO_RESULTS entries (index 82=CCV-01 via assert_delta_ge, indices 83-85=CCV-02/03 3 entries, indices 86-87=CCV-04 2 entries). Corrected to `|82|87`.
- MVC range `|88|95` reserves 8 slots to accommodate MVC-08 (phase 69-02) which will add 1 more result at index 95.
- IpAddress format confidence is MEDIUM (research note). MVC-07 asserts exact `"10.0.0.1"` and includes actual INFO_VALUE in EVIDENCE string so any format difference is immediately visible in test output.
- Counter32 and Counter64 are recorded as raw gauge values by OtelMetricHandler (RecordGauge, no rate conversion). Assertions compare raw values.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 7 MVC static-assertion scenarios are in place at indices 88-94
- Slot 95 reserved for MVC-08 (value-change scenario, phase 69-02)
- report.sh category ranges are now correct with no overlaps: CCV=82-87, MVC=88-95
- Phase 69-02 can proceed immediately with MVC-08 (sim_set_oid poll-with-deadline pattern)

---
*Phase: 69-business-metric-value-correctness*
*Completed: 2026-03-22*
