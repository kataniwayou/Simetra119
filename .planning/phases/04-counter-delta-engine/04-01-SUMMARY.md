---
phase: 04-counter-delta-engine
plan: 01
subsystem: telemetry
tags: [snmp, otel, counter, metrics, interface, pipeline]

# Dependency graph
requires:
  - phase: 03-mediatr-pipeline-and-instruments
    provides: ISnmpMetricFactory with RecordGauge/RecordInfo, SnmpOidReceived IRequest<Unit>, TestSnmpMetricFactory
provides:
  - SysUpTimeCentiseconds property on SnmpOidReceived for sysUpTime flow through pipeline
  - RecordCounter method on ISnmpMetricFactory interface
  - RecordCounter implementation in SnmpMetricFactory using Counter<double>.Add with 5-label TagList
  - CounterRecords list in TestSnmpMetricFactory for test assertion
  - RecordCounter stubs in ThrowingSnmpMetricFactory and CapturingSnmpMetricFactory
affects:
  - 04-02 (CounterDeltaEngine uses RecordCounter and SysUpTimeCentiseconds)
  - 04-03 (handler wiring uses RecordCounter via ISnmpMetricFactory)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "RecordCounter mirrors RecordGauge signature; last param named delta (not value) to signal computed difference"
    - "Counter<double>.Add with 5-label TagList: site_name, metric_name, oid, agent, source"
    - "SysUpTimeCentiseconds uses set accessor (not init) following enrichment property pattern"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/SnmpOidReceived.cs
    - src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs
    - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
    - tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs

key-decisions:
  - "SysUpTimeCentiseconds is uint? nullable - null means unavailable; delta engine conservatively treats current < previous as reboot when null"
  - "RecordCounter last param named delta not value - signals it is a computed difference, not raw SNMP reading"
  - "snmp_counter instrument name used for Counter<double> (mirrors snmp_gauge/snmp_info naming pattern)"
  - "GetOrCreateCounter comment updated from 'future Phase 4' to 'Phase 4 delta engine' - it is now used"

patterns-established:
  - "ISnmpMetricFactory extension pattern: add method to interface, implement in SnmpMetricFactory, stub in TestSnmpMetricFactory/ThrowingSnmpMetricFactory/CapturingSnmpMetricFactory"
  - "All ISnmpMetricFactory implementations updated in same plan to keep interfaces consistent"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 4 Plan 01: Interface Extensions for Counter Delta Engine Summary

**RecordCounter added to ISnmpMetricFactory with Counter<double>.Add implementation and SysUpTimeCentiseconds added to SnmpOidReceived, enabling Phase 4 delta engine and handler wiring in Plans 02-03**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-05T03:06:59Z
- **Completed:** 2026-03-05T03:09:04Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Added `uint? SysUpTimeCentiseconds` to `SnmpOidReceived` for sysUpTime enrichment in the counter poll path
- Added `RecordCounter` to `ISnmpMetricFactory` and implemented in `SnmpMetricFactory` using `Counter<double>.Add` with 5-label TagList
- Updated `TestSnmpMetricFactory` with `CounterRecords` list, plus stubs in `ThrowingSnmpMetricFactory` and `CapturingSnmpMetricFactory`
- All 52 existing tests pass with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Add SysUpTimeCentiseconds to SnmpOidReceived and RecordCounter to ISnmpMetricFactory** - `247f2b3` (feat)
2. **Task 2: Implement RecordCounter in SnmpMetricFactory and TestSnmpMetricFactory** - `a1d7904` (feat)

**Plan metadata:** (docs commit below)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` - Added `uint? SysUpTimeCentiseconds { get; set; }` property
- `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` - Added `RecordCounter` method declaration
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` - Implemented `RecordCounter` using `Counter<double>.Add` with 5-label TagList; updated `GetOrCreateCounter` comment
- `tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs` - Added `CounterRecords` list and `RecordCounter` stub
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - Added `RecordCounter` to `ThrowingSnmpMetricFactory`
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` - Added `RecordCounter` to `CapturingSnmpMetricFactory`

## Decisions Made
- `SysUpTimeCentiseconds` is `uint?` nullable - null is the default valid state; delta engine conservatively treats `current < previous` as reboot when null
- `RecordCounter` last parameter named `delta` (not `value`) to signal it is a computed difference, not a raw SNMP reading
- `snmp_counter` instrument name used for `Counter<double>` - follows `snmp_gauge`/`snmp_info` naming convention
- `GetOrCreateCounter` comment updated from "future Phase 4" to "Phase 4 delta engine" - it is now actively used

## Deviations from Plan

None - plan executed exactly as written. The plan correctly anticipated that `SnmpMetricFactory` (in the main project) also needed `RecordCounter` as part of Task 2, and all four ISnmpMetricFactory implementations were updated together.

## Issues Encountered
None - all changes were purely additive and backward-compatible.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Plan 02 (CounterDeltaEngine): `ISnmpMetricFactory.RecordCounter` and `SnmpOidReceived.SysUpTimeCentiseconds` are ready; delta engine can be built against these interfaces
- Plan 03 (handler wiring): `TestSnmpMetricFactory.CounterRecords` is ready for test assertions on counter dispatch
- All 52 existing tests pass; no regressions introduced

---
*Phase: 04-counter-delta-engine*
*Completed: 2026-03-05*
