---
phase: 34-communitystring-validation-and-metricpolljob-cleanup
plan: 02
subsystem: pipeline
tags: [csharp, tenant-vector-registry, snmp, validation, oid-map, device-registry]

# Dependency graph
requires:
  - phase: 34-01
    provides: DeviceRegistry.TryGetByIpPort for TEN-07 IP+Port existence checks
  - phase: 33-tenant-options-commands-and-role
    provides: TenantOptions.Commands (CommandSlotOptions), MetricSlotOptions.Role field

provides:
  - TenantVectorRegistry with IOidMapService re-added (re-added for TEN-05 MetricName validation)
  - Per-entry metric validation: structural (Ip/Port/MetricName), Role, TEN-05 MetricName resolution, TEN-07 IP+Port existence
  - Per-entry command validation: structural (Ip/Port/CommandName), TEN-03 ValueType, Value, TEN-07 IP+Port existence
  - TEN-13 post-validation completeness gate: skips tenant missing Resolved, Evaluate, or commands
  - Per-entry skip semantics: one invalid entry does not block siblings
  - Updated DI wiring: IOidMapService passed to TenantVectorRegistry in ServiceCollectionExtensions
  - 36 TenantVectorRegistryTests (23 updated + 13 adjusted counts + 12 new validation tests)

affects:
  - 35-xxx (TenantVectorRegistry restructuring -- IOidMapService re-added here; Phase 35 will restructure further per STATE.md note)
  - TenantVectorWatcherService (calls Reload; benefits from per-entry skip over all-or-nothing)
  - DynamicPollScheduler (no direct impact; registry validation is orthogonal)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Per-entry guard clause chain: IsNullOrWhiteSpace -> range check -> role check -> OID map check -> DeviceRegistry check -> accept"
    - "TEN-13 completeness gate: post-loop check of evaluateCount/resolvedCount/commandCount before adding tenant to priorityBuckets"
    - "Passthrough mock pattern: IDeviceRegistry and IOidMapService accept-all substitutes in test helpers for TEN-13 compliance"
    - "MakeValidTenantOptions helper: auto-adds Resolved sibling and command entry for TEN-13 compliance in test helpers"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs

key-decisions:
  - "IOidMapService re-added to TenantVectorRegistry constructor (Phase 33 had removed it; Phase 35 will restructure further)"
  - "Per-entry skip semantics: continue in metric/command loops, not per-tenant abort on first bad entry"
  - "TEN-13 checks all three conditions (Resolved, Evaluate, commands) and reports all missing in one Error log"
  - "TenantCount = survivingTenantCount + 1 (heartbeat only): previously counted all tenants regardless of TEN-13"
  - "CreateOptions() in tests auto-adds Resolved metric and command per tenant for TEN-13 compliance"
  - "TenantVectorFanOutBehaviorTests: added using NSubstitute and passthrough helpers to support IOidMapService"

patterns-established:
  - "Validation loop pattern: guard clauses with continue + Error log at each validation step"
  - "TEN-13 gate: accumulate role/command counts during validation, check after both loops"
  - "Test helper passthrough pattern: CreatePassthroughDeviceRegistry/CreatePassthroughOidMapService return accept-all mocks"

# Metrics
duration: 7min
completed: 2026-03-14
---

# Phase 34 Plan 02: TenantVectorRegistry Validation Summary

**IOidMapService re-added to TenantVectorRegistry, per-entry metric and command validation (structural + Role + TEN-05/TEN-07/TEN-03), TEN-13 completeness gate, and 12 new validation tests covering all skip scenarios**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-14T21:06:02Z
- **Completed:** 2026-03-14T21:13:30Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- TenantVectorRegistry now validates every metric entry (empty Ip, port range, empty MetricName, invalid Role, TEN-05 MetricName not in OID map, TEN-07 IP+Port not in DeviceRegistry) with Error log + continue per invalid entry
- Command entries validated (empty Ip, port range, empty CommandName, TEN-03 invalid ValueType, empty Value, TEN-07 IP+Port not in DeviceRegistry)
- TEN-13 post-validation gate drops entire tenant when no Resolved metrics, no Evaluate metrics, or no valid commands remain after per-entry validation
- DI wiring updated: `ServiceCollectionExtensions` factory passes `IOidMapService` to `TenantVectorRegistry`
- All 270 tests pass (36 TenantVectorRegistryTests including 12 new validation tests)

## Task Commits

Each task was committed atomically:

1. **Task 1: TenantVectorRegistry -- re-add IOidMapService, per-entry validation, TEN-13 gate, DI wiring** - `a3cb0c2` (feat)
2. **Task 2: TenantVectorRegistryTests -- IOidMapService stub and validation tests** - `2d992cc` (test)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - IOidMapService field and constructor param, metric validation loop with 6 guard clauses, command validation loop with 6 guard clauses, TEN-13 completeness gate, TenantCount = survivingTenantCount + 1
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - DI factory now passes `sp.GetRequiredService<IOidMapService>()` to TenantVectorRegistry
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - CreateRegistry/CreateOptions helpers updated for IOidMapService and TEN-13 compliance; 12 new validation tests; slot count assertions updated
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` - Added `using NSubstitute;`, passthrough helpers, MakeValidTenantOptions, fixed all 4 constructor calls
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - Fixed 2 TenantVectorRegistry constructor calls to include IOidMapService

## Decisions Made

- **IOidMapService re-added**: The plan noted this explicitly -- Phase 33 removed it, Phase 35 will restructure further. Re-adding is the correct transitional step for TEN-05.
- **Per-entry skip, not per-tenant abort on first error**: Each validation step uses `continue` (not `return`/`break`), so one bad metric entry doesn't prevent valid siblings from loading.
- **TEN-13 collects all failures**: Uses a `missing` list to report all three missing conditions in one Error log message rather than three separate logs.
- **TenantCount changed**: Now counts surviving tenants (post TEN-13) + 1 heartbeat, rather than all tenants in options + 1. This reflects reality.
- **Test passthrough helpers**: `CreatePassthroughDeviceRegistry()` and `CreatePassthroughOidMapService()` accept everything so existing behavior tests don't need to set up OID map or device registry state.
- **FanOutBehaviorTests**: Added `using NSubstitute;` to avoid qualified call syntax that breaks extension method resolution (`.Returns()` is an extension method).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed constructor calls in TenantVectorFanOutBehaviorTests and PipelineIntegrationTests**
- **Found during:** Task 2 (first test run)
- **Issue:** 4 other test files used `new TenantVectorRegistry(deviceRegistry, logger)` -- the old 2-argument constructor. Adding IOidMapService made these fail to compile.
- **Fix:** Updated all 5 occurrences across `TenantVectorFanOutBehaviorTests.cs` (3 helper factories + 1 direct) and `PipelineIntegrationTests.cs` (2 DI registrations) to pass IOidMapService
- **Files modified:** `TenantVectorFanOutBehaviorTests.cs`, `PipelineIntegrationTests.cs`
- **Verification:** All 270 tests pass
- **Committed in:** `2d992cc` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (blocking)
**Impact on plan:** Required fix. IOidMapService constructor change necessarily propagates to all test files constructing TenantVectorRegistry directly. No scope creep.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 34 is now complete (both plans done)
- TenantVectorRegistry validates all tenant entries at load time with structured Error logs
- Per-entry skip semantics allow partial tenant load when some entries are invalid
- TEN-13 gate provides completeness assurance: no tenant with missing roles or commands loads silently
- STATE.md note: Phase 35 will restructure TenantVectorRegistry further (removing IDeviceRegistry dependency per v1.7 DNS resolution decision)

---
*Phase: 34-communitystring-validation-and-metricpolljob-cleanup*
*Completed: 2026-03-14*
