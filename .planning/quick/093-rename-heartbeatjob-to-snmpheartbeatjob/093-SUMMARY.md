---
phase: quick-093
plan: 01
subsystem: jobs
tags: [quartz, csharp, rename, heartbeat, snmp]

# Dependency graph
requires:
  - phase: quick-088
    provides: SNMP heartbeat job and pipeline liveness infrastructure
provides:
  - SnmpHeartbeatJob class replacing HeartbeatJob across all of src/ and tests/
  - SnmpHeartbeatJobOptions with SectionName = "SnmpHeartbeatJob"
  - Job key "snmp-heartbeat" replacing "heartbeat" in Quartz registry
affects: [any future phase referencing the SNMP heartbeat job, liveness checks, or job interval registry]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Job key naming: prefix SNMP-specific jobs with 'snmp-' to avoid ambiguity with other heartbeat concepts"
    - "Options SectionName matches class prefix: SnmpHeartbeatJob -> SnmpHeartbeatJob (consistent casing)"

key-files:
  created:
    - src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs
    - src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs
    - tests/SnmpCollector.Tests/Jobs/SnmpHeartbeatJobTests.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Services/PollSchedulerStartupService.cs
    - src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs
    - src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
    - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    - src/SnmpCollector/appsettings.json
    - src/SnmpCollector/appsettings.Development.json
    - tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    - tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs

key-decisions:
  - "IHeartbeatLivenessService name preserved — it represents heartbeat liveness, not the Quartz job"
  - "PreferredHeartbeatJob and PreferredHeartbeatJobOptions not renamed — separate concept (leader election)"
  - "Job key changed from 'heartbeat' to 'snmp-heartbeat' to match class semantics in Quartz registry and liveness vector"

patterns-established:
  - "SNMP-domain classes prefixed with Snmp to differentiate from K8s/liveness heartbeat concepts"

# Metrics
duration: 12min
completed: 2026-03-26
---

# Quick Task 093: Rename HeartbeatJob to SnmpHeartbeatJob Summary

**Renamed HeartbeatJob/HeartbeatJobOptions to SnmpHeartbeatJob/SnmpHeartbeatJobOptions across 13 source files; config section and Quartz job key updated; all 524 tests pass**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-03-26T00:00:00Z
- **Completed:** 2026-03-26T00:12:00Z
- **Tasks:** 2
- **Files modified:** 13

## Accomplishments
- Renamed HeartbeatJob.cs to SnmpHeartbeatJob.cs and HeartbeatJobOptions.cs to SnmpHeartbeatJobOptions.cs via `git mv`
- Updated all references in src/ (7 files) — class names, logger types, IOptions injection, DI registration, appsettings keys
- Renamed HeartbeatJobTests.cs to SnmpHeartbeatJobTests.cs via `git mv` and updated all 5 test files
- Config section renamed from "HeartbeatJob" to "SnmpHeartbeatJob" in both appsettings files
- Quartz job key renamed from "heartbeat" to "snmp-heartbeat" (trigger, registry entry)
- All 524 tests pass; build succeeds with 0 errors and 0 warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Rename files and classes in src/** - `52c9e3d` (feat)
2. **Task 2: Rename files and references in tests/** - `cf77510` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified
- `src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs` - Renamed from HeartbeatJob.cs; class and logger type updated
- `src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs` - Renamed from HeartbeatJobOptions.cs; SectionName = "SnmpHeartbeatJob"
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - DI registration, Quartz job key, comments
- `src/SnmpCollector/Services/PollSchedulerStartupService.cs` - Comment update only
- `src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs` - IOptions injection type, comment
- `src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs` - Doc comment update only
- `src/SnmpCollector/Pipeline/OidMapService.cs` - HeartbeatSeed method reference
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - HeartbeatDeviceName comparison
- `src/SnmpCollector/appsettings.json` - Section key renamed
- `src/SnmpCollector/appsettings.Development.json` - Section key renamed
- `tests/SnmpCollector.Tests/Jobs/SnmpHeartbeatJobTests.cs` - Renamed from HeartbeatJobTests.cs; all references updated
- `tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs` - Options type updated
- `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` - Static const references updated
- `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs` - Static const reference updated
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` - Static const references updated

## Decisions Made
- `IHeartbeatLivenessService` was NOT renamed — it tracks "heartbeat liveness" (pipeline arrival), which is a distinct concept from the SNMP job. Only the doc comment was updated to reference `SnmpHeartbeatJobOptions`.
- `PreferredHeartbeatJob` and `PreferredHeartbeatJobOptions` were NOT renamed — they belong to the K8s leader election subsystem, not the SNMP polling heartbeat.
- Job key changed from `"heartbeat"` to `"snmp-heartbeat"` to be consistent with the class rename and distinguish it in the Quartz registry and liveness vector.

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- SnmpHeartbeatJob is fully renamed; all consumers updated
- Liveness vector key "snmp-heartbeat" must match in any K8s deployment manifests or dashboards that reference the job key by string (if any exist)
- No blockers

---
*Phase: quick-093*
*Completed: 2026-03-26*
