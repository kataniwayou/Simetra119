---
phase: 06-poll-scheduling
plan: 02
subsystem: pipeline
tags: [snmp, quartz, mediatr, sharpsnmplib, unreachability-tracking, polling, otel]

# Dependency graph
requires:
  - phase: 06-01-poll-scheduling
    provides: IDeviceUnreachabilityTracker with transition-detecting RecordFailure/RecordSuccess; PipelineMetricService with IncrementPollUnreachable/IncrementPollRecovered
  - phase: 03-mediatr-pipeline-and-instruments
    provides: SnmpOidReceived IRequest<Unit>; ISender.Send dispatch path; full behavior pipeline (Logging, Exception, Validation, OidResolution)
  - phase: 02-device-registry-and-oid-map
    provides: IDeviceRegistry.TryGetDeviceByName; DeviceInfo with PollGroups; MetricPollInfo with Oids/IntervalSeconds
provides:
  - MetricPollJob sealed IJob with [DisallowConcurrentExecution], sysUpTime prepend, 80% interval timeout, ISender.Send per varbind, and unreachability transition handling
affects:
  - 06-03 (Quartz scheduler registration — will register MetricPollJob instances per device/poll-group)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Linked CancellationTokenSource with CancelAfter at 80% of interval for SNMP GET timeout (not fixed timeout)"
    - "Three-way OperationCanceledException catch: when-guard for timeout vs bare catch for host shutdown (re-throw)"
    - "sysUpTime extracted from first matching varbind; its own SnmpOidReceived carries SysUpTimeCentiseconds=null (value not yet extracted when dispatching first iteration)"
    - "IncrementPollExecuted in finally block — fires after every poll attempt, success or failure (SC#4)"
    - "Transition-only counter/log: RecordFailure/RecordSuccess return value gates OTel counter increment and Warning log"

key-files:
  created:
    - src/SnmpCollector/Jobs/MetricPollJob.cs
  modified: []

key-decisions:
  - "ISender.Send used (not IPublisher.Publish) — SnmpOidReceived is IRequest<Unit>; IPublisher.Publish bypasses IPipelineBehavior entirely (locked from 03-06)"
  - "Device not found returns immediately before try block — config errors must NOT increment snmp.poll.executed"
  - "sysUpTime varbind itself dispatched with SysUpTimeCentiseconds=null — value extracted after loop processes it; subsequent OIDs carry the extracted value"
  - "noSuchObject/noSuchInstance/EndOfMibView varbinds skipped with Debug (not Warning) — expected for optional OIDs, not operator-alertable events"
  - "Bare OperationCanceledException (host shutdown) re-thrown — Quartz handles graceful shutdown; swallowing it causes job to report success on shutdown"

patterns-established:
  - "Pattern 6: three-way OperationCanceledException catch with when-guard for timeout vs host shutdown distinction"
  - "Pattern 7: sysUpTime-prepend pattern for SNMP GET with fallthrough dispatch (varbind dispatched even if it provided the uptime value)"

# Metrics
duration: 3min
completed: 2026-03-05
---

# Phase 6 Plan 02: MetricPollJob Summary

**Quartz IJob that prepends sysUpTime to every SNMP GET, dispatches each varbind via ISender.Send with 80% interval timeout, three-way cancel handling, and transition-only unreachability logging**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-03-05T14:48:59Z
- **Completed:** 2026-03-05T14:51:59Z
- **Tasks:** 1
- **Files modified:** 1 (1 created)

## Accomplishments
- MetricPollJob sealed IJob with [DisallowConcurrentExecution] prevents Quartz pile-up on slow devices
- sysUpTime (1.3.6.1.2.1.1.3.0) prepended to every SNMP GET request for atomic uptime context alongside counter OIDs
- Linked CTS with CancelAfter at 80% of intervalSeconds enforces SC#2 timeout
- Each varbind dispatched individually via ISender.Send into full MediatR behavior pipeline
- noSuchObject/noSuchInstance/EndOfMibView varbinds silently skipped with Debug log
- Three-way catch: timeout (when-guard), host shutdown (re-throw), network/SNMP error
- IncrementPollExecuted in finally — every completed attempt counted regardless of success/failure
- RecordFailure/RecordSuccess transition-only: OTel counter + Warning log fire only on state change
- Build succeeds with 0 warnings; all 86 existing tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Create MetricPollJob with SNMP GET, MediatR dispatch, and failure handling** - `90fc831` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/Jobs/MetricPollJob.cs` - Sealed IJob: [DisallowConcurrentExecution], SysUpTimeOid constant, constructor with 5 DI dependencies (IDeviceRegistry, IDeviceUnreachabilityTracker, ISender, PipelineMetricService, ILogger), Execute method with device lookup, variable list construction, linked CTS timeout, Messenger.GetAsync, DispatchResponseAsync, three-way catch, finally IncrementPollExecuted; private DispatchResponseAsync (sysUpTime extraction + varbind dispatch via ISender.Send); private RecordFailure (transition-only OTel + log)

## Decisions Made
- ISender.Send confirmed (not IPublisher.Publish) — SnmpOidReceived is IRequest<Unit>; locked from plan 03-06
- Device lookup failure returns before try block — ensures snmp.poll.executed never increments for config errors, only for actual poll attempts
- sysUpTime own SnmpOidReceived carries SysUpTimeCentiseconds=null — value is extracted from the TimeTicks Data and stored in local variable; since it's the first iteration, the local is null when dispatching the sysUpTime varbind itself; subsequent OIDs carry the extracted value. Consistent with RESEARCH.md Open Question #2 resolution.
- noSuchObject/noSuchInstance/EndOfMibView logged at Debug (not Warning) — expected for devices that don't expose all OIDs; operator-alertable level would cause noise
- Bare OperationCanceledException (no when-guard) re-throws for host shutdown — Quartz needs to see the cancellation to handle graceful shutdown correctly; swallowing it would make the job report success during shutdown

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- MetricPollJob is complete and ready for Quartz scheduler registration in Plan 03
- Plan 03 will register one job instance per device/poll-group combination using DeviceInfo.PollGroups
- Thread pool sizing formula (jobs * 1.5 or jobs + 2) to be decided in Plan 03

---
*Phase: 06-poll-scheduling*
*Completed: 2026-03-05*
