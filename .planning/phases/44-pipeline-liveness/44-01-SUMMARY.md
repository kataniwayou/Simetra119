---
phase: 44-pipeline-liveness
plan: 01
subsystem: pipeline
tags: [liveness, heartbeat, volatile, thread-safe, otel, mediatr, health-checks]

# Dependency graph
requires:
  - phase: 43-heartbeat-cleanup
    provides: heartbeat bypass removed from fan-out; HeartbeatDeviceName flows naturally through pipeline
  - phase: 08-graceful-shutdown-and-health-probes
    provides: ILivenessVectorService pattern used as model for IHeartbeatLivenessService
provides:
  - IHeartbeatLivenessService interface with Stamp() and DateTimeOffset? LastArrival
  - HeartbeatLivenessService: lock-free volatile long (UTC ticks) implementation
  - OtelMetricHandler stamps pipeline arrival after IncrementHandled when DeviceName == HeartbeatDeviceName
  - DI registration as singleton in AddSnmpPipeline
  - 2 new tests verifying stamp-on-heartbeat and no-stamp-on-non-heartbeat
affects:
  - 44-02-PLAN (LivenessHealthCheck extension reads IHeartbeatLivenessService.LastArrival)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Volatile.Read/Write on long field (not volatile keyword) for lock-free single-value timestamp stamp"
    - "Guard pattern: deviceName == HeartbeatJobOptions.HeartbeatDeviceName before side-effecting stamp call"
    - "Stamp after IncrementHandled — proves full handler logic ran, not just entry"

key-files:
  created:
    - src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs
    - src/SnmpCollector/Pipeline/HeartbeatLivenessService.cs
  modified:
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs

key-decisions:
  - "Volatile.Write/Read on long field (not volatile keyword on field) — avoids analyzer warning, explicit memory semantics"
  - "Stamp placed AFTER IncrementHandled, before break — proves full numeric-case handler ran"
  - "Stamp guarded by deviceName == HeartbeatJobOptions.HeartbeatDeviceName — only heartbeat device stamps"
  - "Stamp is in numeric case ONLY — heartbeat OID carries Counter32, not OctetString"
  - "HeartbeatJob.cs and ILivenessVectorService untouched — scheduler liveness and pipeline liveness are distinct concerns"

patterns-established:
  - "IHeartbeatLivenessService: minimal interface (Stamp + LastArrival) for single-value pipeline arrival tracking"
  - "HeartbeatLivenessService: long _lastArrivalTicks = 0 sentinel; Volatile.Write in Stamp(), Volatile.Read in getter"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 44 Plan 01: Pipeline Liveness - IHeartbeatLivenessService Summary

**Lock-free pipeline-arrival liveness stamp via volatile long (UTC ticks) in OtelMetricHandler, guarded by HeartbeatDeviceName, registered as singleton in AddSnmpPipeline**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T16:11:35Z
- **Completed:** 2026-03-15T16:13:35Z
- **Tasks:** 3
- **Files modified:** 5 (2 created, 3 modified)

## Accomplishments
- Created `IHeartbeatLivenessService` with `Stamp()` and `DateTimeOffset? LastArrival` contract
- Created `HeartbeatLivenessService` using `volatile long` (UTC ticks) with `Volatile.Write`/`Volatile.Read` — lock-free, no volatile keyword on field
- Injected into `OtelMetricHandler`; stamp fires after `IncrementHandled` in numeric case, guarded by `deviceName == HeartbeatJobOptions.HeartbeatDeviceName`
- Registered `services.AddSingleton<IHeartbeatLivenessService, HeartbeatLivenessService>()` in `AddSnmpPipeline`
- Updated test constructor (+ `new HeartbeatLivenessService()` argument); added 2 new tests; 334 total tests green

## Task Commits

Each task was committed atomically:

1. **Task 1: Create IHeartbeatLivenessService interface and HeartbeatLivenessService implementation** - `b3843ea` (feat)
2. **Task 2: Inject IHeartbeatLivenessService into OtelMetricHandler and stamp on heartbeat** - `76c8aae` (feat)
3. **Task 3: Update OtelMetricHandlerTests and add heartbeat liveness tests** - `da04986` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs` - Interface: `Stamp()` + `DateTimeOffset? LastArrival`
- `src/SnmpCollector/Pipeline/HeartbeatLivenessService.cs` - Implementation: volatile long ticks, Volatile.Read/Write
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - Added field, constructor param, stamp call in numeric case
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - AddSingleton in AddSnmpPipeline after ILivenessVectorService
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` - Constructor update + 2 new heartbeat liveness tests

## Decisions Made
- Used `Volatile.Write`/`Volatile.Read` methods rather than `volatile` keyword on field — per RESEARCH.md: `volatile` on a struct field is invalid C#; long is atomic on all supported platforms
- Stamp placed after `_pipelineMetrics.IncrementHandled(deviceName)` and before `break` — proves full handler logic ran (stamp point is the terminal success signal)
- Stamp guard uses `HeartbeatJobOptions.HeartbeatDeviceName` const — single source of truth for "Simetra"
- Stamp is in numeric case only — heartbeat OID carries `Counter32`, OctetString case is not the correct gate

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- `IHeartbeatLivenessService` singleton is wired and stamped; ready for plan 02 (`LivenessHealthCheck` extension)
- Plan 02 injects `IHeartbeatLivenessService` + `IOptions<HeartbeatJobOptions>` into `LivenessHealthCheck` to add pipeline-arrival staleness check
- `HeartbeatJob.cs` and `ILivenessVectorService` untouched — no regressions to scheduler liveness path

---
*Phase: 44-pipeline-liveness*
*Completed: 2026-03-15*
