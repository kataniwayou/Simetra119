---
phase: 06-poll-scheduling
plan: 01
subsystem: pipeline
tags: [snmp, otel, concurrent-dictionary, unreachability-tracking, metrics]

# Dependency graph
requires:
  - phase: 05-trap-ingestion
    provides: PipelineMetricService singleton with 9 counters; IDeviceRegistry with AllDevices
provides:
  - IDeviceUnreachabilityTracker interface (RecordFailure, RecordSuccess, GetFailureCount, IsUnreachable)
  - DeviceUnreachabilityTracker sealed class with threshold=3 and ConcurrentDictionary-backed per-device state
  - snmp.poll.unreachable OTel counter on PipelineMetricService
  - snmp.poll.recovered OTel counter on PipelineMetricService
affects:
  - 06-02-MetricPollJob (consumes IDeviceUnreachabilityTracker and IncrementPollUnreachable/IncrementPollRecovered)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Inner-class pattern for ConcurrentDictionary state: sealed inner DeviceState class avoids struct-update atomicity issues"
    - "volatile int + Interlocked.Increment for lock-free per-device failure counting"
    - "Transition-only return: RecordFailure/RecordSuccess return true only on state change, false on steady-state"

key-files:
  created:
    - src/SnmpCollector/Pipeline/IDeviceUnreachabilityTracker.cs
    - src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs
  modified:
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs

key-decisions:
  - "Threshold hardcoded at 3 (not configurable) per locked CONTEXT.md decision"
  - "OrdinalIgnoreCase comparer for ConcurrentDictionary — device names are user-configured and may vary in case"
  - "Singleton tracker (not per-job field) — Quartz DI creates new job instance per execution, so per-job state is lost"

patterns-established:
  - "Pattern 5: class-based inner state in ConcurrentDictionary for atomic mutable per-key state"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 6 Plan 01: Foundation Types for Poll Scheduling Summary

**ConcurrentDictionary-backed per-device consecutive failure tracker with threshold=3 transition detection, plus snmp.poll.unreachable and snmp.poll.recovered OTel counters on PipelineMetricService**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-05T14:44:21Z
- **Completed:** 2026-03-05T14:46:21Z
- **Tasks:** 2
- **Files modified:** 3 (2 created, 1 modified)

## Accomplishments
- IDeviceUnreachabilityTracker interface with transition-detecting RecordFailure/RecordSuccess semantics
- DeviceUnreachabilityTracker singleton using inner DeviceState class for atomic ConcurrentDictionary updates
- PipelineMetricService extended from 9 to 11 counters with snmp.poll.unreachable and snmp.poll.recovered
- All 86 existing tests continue to pass

## Task Commits

Each task was committed atomically:

1. **Task 1: IDeviceUnreachabilityTracker interface and DeviceUnreachabilityTracker implementation** - `433d480` (feat)
2. **Task 2: Add snmp.poll.unreachable and snmp.poll.recovered counters to PipelineMetricService** - `a470ee4` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/IDeviceUnreachabilityTracker.cs` - Interface with 4 methods: RecordFailure (transition-on-threshold), RecordSuccess (transition-on-recovery), GetFailureCount, IsUnreachable
- `src/SnmpCollector/Pipeline/DeviceUnreachabilityTracker.cs` - Sealed singleton; ConcurrentDictionary<string, DeviceState> with OrdinalIgnoreCase; inner DeviceState with volatile int + Interlocked for lock-free counting; threshold=3 hardcoded
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` - Added _pollUnreachable and _pollRecovered Counter<long> fields, constructor initialization, IncrementPollUnreachable() and IncrementPollRecovered() public methods; class doc updated to "11 pipeline counter instruments"

## Decisions Made
- Threshold hardcoded at 3 per locked CONTEXT.md decision — no configuration plumbing needed
- OrdinalIgnoreCase on the ConcurrentDictionary — device names are user-configured strings that may vary in case between configuration and Quartz JobDataMap usage
- Singleton tracker pattern confirmed over per-job instance fields — Quartz DI constructs a new job instance per execution, so any per-job state is destroyed between runs

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- IDeviceUnreachabilityTracker and DeviceUnreachabilityTracker are ready for injection into MetricPollJob (Plan 02)
- IncrementPollUnreachable() and IncrementPollRecovered() are ready to be called from MetricPollJob failure/recovery logic
- Plan 02 can proceed immediately with no blockers

---
*Phase: 06-poll-scheduling*
*Completed: 2026-03-05*
