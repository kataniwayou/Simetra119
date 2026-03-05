---
phase: 06-poll-scheduling
plan: 03
subsystem: scheduling
tags: [quartz, snmp, thread-pool, job-registration, hosted-service]

# Dependency graph
requires:
  - phase: 06-02
    provides: MetricPollJob Quartz IJob implementation with SNMP GET and ISender dispatch
  - phase: 06-01
    provides: IDeviceUnreachabilityTracker singleton with consecutive failure tracking
  - phase: 02-02
    provides: DeviceRegistry with AllDevices and PollGroups per DeviceInfo

provides:
  - AddSnmpScheduling registers IDeviceUnreachabilityTracker as singleton
  - UseDefaultThreadPool maxConcurrency = 1 + sum(device.MetricPolls.Count) for all devices
  - One MetricPollJob per device/poll-group with JobDataMap (deviceName, pollIndex, intervalSeconds)
  - PollSchedulerStartupService logs "Registered {N} poll jobs across {M} devices, thread pool size: {T}"

affects:
  - 06-04 (final phase verification and integration tests)
  - 07-leader-election (MetricRoleGatedExporter uses same AddQuartz block)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pre-DI-build config binding: new DevicesOptions(); config.GetSection(...).Bind(devicesOptions.Devices) — same pattern as AddSnmpConfiguration"
    - "for-loop indexing (not foreach) inside AddQuartz lambdas to avoid C# variable capture bug"
    - "Thread pool auto-scale: jobCount = 1 + sum(MetricPolls.Count) then UseDefaultThreadPool(maxConcurrency: jobCount)"

key-files:
  created:
    - src/SnmpCollector/Services/PollSchedulerStartupService.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "Thread pool maxConcurrency = 1 (CorrelationJob) + sum of all device poll group counts — 1:1 thread-per-job, no starvation"
  - "DevicesOptions bound eagerly (pre-DI) to devicesOptions.Devices — DI container not built when AddQuartz lambda runs"
  - "for loops with index variable (not foreach) inside AddQuartz lambdas — prevents C# lambda closure capture bug on loop variables"
  - "PollSchedulerStartupService committed atomically with ServiceCollectionExtensions — build requires both files present simultaneously"

patterns-established:
  - "IHostedService startup log pattern: StartAsync reads IDeviceRegistry.AllDevices, sums PollGroups.Count, logs at Information"
  - "Quartz job loop pattern: for(di) outer / for(pi) inner, var device = Devices[di] as local copy, JobKey uses device.Name + pi"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 6 Plan 03: Quartz MetricPollJob Registration Summary

**Quartz thread pool auto-scaled to total job count with one MetricPollJob per device/poll-group pair and startup log emitting poll job count, device count, and thread pool size**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-05T14:53:15Z
- **Completed:** 2026-03-05T14:55:15Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- AddSnmpScheduling now registers IDeviceUnreachabilityTracker as singleton before CorrelationService
- UseDefaultThreadPool(maxConcurrency: jobCount) replaces Quartz default of 10 — thread pool scales to exact job count
- MetricPollJob registration loop adds one Quartz job per device/poll-group with correct JobDataMap keys (deviceName, pollIndex, intervalSeconds)
- PollSchedulerStartupService logs the locked CONTEXT.md format: "Registered {N} poll jobs across {M} devices, thread pool size: {T}"

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite AddSnmpScheduling with thread pool sizing and MetricPollJob registration** - `1b0edaf` (feat)
2. **Task 2: Create PollSchedulerStartupService for startup log** - `1b0edaf` (feat, same commit — build requires both files simultaneously)

_Note: Tasks 1 and 2 were committed together because Task 1 registers `AddHostedService<PollSchedulerStartupService>()` and the build fails without the class file present. Both changes are logically part of the same atomic unit._

## Files Created/Modified
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - AddSnmpScheduling rewritten with IDeviceUnreachabilityTracker singleton, eager DevicesOptions binding, thread pool sizing, MetricPollJob per-device loop, PollSchedulerStartupService registration
- `src/SnmpCollector/Services/PollSchedulerStartupService.cs` - New IHostedService: reads IDeviceRegistry.AllDevices, sums PollGroups.Count, logs Information startup summary

## Decisions Made
- **Thread pool formula:** `jobCount = 1 + sum(device.MetricPolls.Count)` — 1:1 thread-per-job matches Simetra reference; no starvation possible
- **Pre-DI config binding:** `new DevicesOptions(); config.GetSection("Devices").Bind(devicesOptions.Devices)` — DI container not yet built when AddQuartz configuration lambda runs; same pattern used in AddSnmpConfiguration
- **for loops in AddQuartz lambdas:** Index variables `di` and `pi` are value-type locals captured correctly; `foreach` with lambda would capture the loop variable reference, causing all jobs to use the last device/poll values
- **Single commit for both tasks:** Build requires PollSchedulerStartupService to exist when ServiceCollectionExtensions compiles; staged atomically

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- MetricPollJob is now wired into Quartz: polls will fire on configured intervals as soon as the scheduler starts
- Thread pool sized to prevent job starvation under any device/poll-group configuration
- PollSchedulerStartupService provides capacity validation on startup
- Phase 06-04 can proceed with integration verification and any remaining phase 6 work

---
*Phase: 06-poll-scheduling*
*Completed: 2026-03-05*
