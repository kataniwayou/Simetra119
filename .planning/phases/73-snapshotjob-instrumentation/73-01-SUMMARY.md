---
phase: 73-snapshotjob-instrumentation
plan: 01
subsystem: telemetry
tags: [snmp, commandrequest, commandworkerservice, tenantmetricservice, nsubstitute, metrics, otel]

# Dependency graph
requires:
  - phase: 72-tenantmetricservice-meter-registration
    provides: ITenantMetricService interface and TenantMetricService implementation registered in DI
provides:
  - CommandRequest record extended with TenantId (string) and Priority (int) positional parameters
  - CommandWorkerService instrumented with ITenantMetricService for per-tenant failed-SET tracking
  - All 4 SET failure paths call IncrementCommandFailed with tenant context
affects:
  - 73-02 (SnapshotJob EvaluateTenant instrumentation - next plan in phase)
  - Any future consumers reading CommandRequest from the channel

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Additive metric calls: tenant metrics added alongside existing pipeline metrics, not replacing them"
    - "Tenant context carried in channel messages: CommandRequest holds TenantId/Priority for downstream tagging"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/CommandRequest.cs
    - src/SnmpCollector/Services/CommandWorkerService.cs
    - src/SnmpCollector/Jobs/SnapshotJob.cs
    - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs
    - tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs

key-decisions:
  - "ITenantMetricService injected between PipelineMetricService and IOptions<SnapshotJobOptions> in constructor parameter order"
  - "Tenant metric calls are additive — existing _pipelineMetrics calls preserved at all failure sites"
  - "MakeRequest helper uses default tenantId=test-tenant and priority=1 to keep test call sites unchanged"

patterns-established:
  - "Additive instrumentation pattern: new metric services added without removing existing ones"
  - "Channel message carries tenant context: avoids lookup at consumer; TenantId/Priority in CommandRequest"

# Metrics
duration: 3min
completed: 2026-03-23
---

# Phase 73 Plan 01: SnapshotJob Instrumentation - CommandRequest & CommandWorkerService Summary

**CommandRequest extended with TenantId/Priority; CommandWorkerService instrumented with ITenantMetricService calling IncrementCommandFailed at all 4 SET failure paths**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-23T12:29:46Z
- **Completed:** 2026-03-23T12:33:21Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Extended `CommandRequest` sealed record with `string TenantId` and `int Priority` as positional parameters 6 and 7, with XML doc update explaining tenant context purpose
- Updated all construction sites: `SnapshotJob.cs` passes `tenant.Id, tenant.Priority`; `SnapshotJobTests.cs` channel-fill stub updated; `CommandWorkerServiceTests.cs` `MakeRequest` helper extended with optional `tenantId`/`priority` params
- Injected `ITenantMetricService` into `CommandWorkerService` constructor (between `PipelineMetricService` and `IOptions<SnapshotJobOptions>`) with additive `IncrementCommandFailed` calls at all 4 SET failure sites
- Updated test fixture: added `_tenantMetrics = Substitute.For<ITenantMetricService>()` field and `tenantMetrics` parameter to `CreateService`; all 470 tests remain green

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend CommandRequest and update all construction sites** - `8c9bdbb` (feat)
2. **Task 2: Inject ITenantMetricService and call IncrementCommandFailed at all 4 SET failure sites** - `af2a35e` (feat)

**Plan metadata:** (pending docs commit)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/CommandRequest.cs` - Added TenantId (string) and Priority (int) as positional record parameters 6 and 7; updated XML doc
- `src/SnmpCollector/Services/CommandWorkerService.cs` - Added ITenantMetricService field + constructor param; 4 additive IncrementCommandFailed calls
- `src/SnmpCollector/Jobs/SnapshotJob.cs` - CommandRequest construction updated to pass tenant.Id, tenant.Priority
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` - Channel-fill stub updated to 7-arg CommandRequest
- `tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs` - Added NSubstitute import, _tenantMetrics field, updated MakeRequest helper and CreateService

## Decisions Made

- ITenantMetricService constructor parameter inserted between `PipelineMetricService pipelineMetrics` and `IOptions<SnapshotJobOptions>` to group metric services together
- Tenant metric calls are strictly additive — the existing `_pipelineMetrics.IncrementCommandFailed(...)` calls at each failure site are preserved unchanged
- `MakeRequest` defaults (`tenantId = "test-tenant"`, `priority = 1`) keep all existing test call sites unchanged with no required updates to test bodies

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

The `dotnet build` command on first invocation failed with `MSB3492: Could not read existing file SnmpCollector.AssemblyInfoInputs.cache`. This is a transient file-locking issue with the obj cache (the error message itself says "Overwriting it"). Deleted the stale cache file and subsequent build succeeded immediately.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 73-01 complete: structural plumbing in place
- CommandRequest carries tenant context through channel
- CommandWorkerService tracks per-tenant SET failures
- Ready for Plan 73-02: SnapshotJob EvaluateTenant instrumentation (tier counters, duration, tenant state recording)
- No blockers

---
*Phase: 73-snapshotjob-instrumentation*
*Completed: 2026-03-23*
