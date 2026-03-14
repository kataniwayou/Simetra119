---
phase: 35-tenantvectorregistry-refactor-and-validator-activation
plan: 02
subsystem: pipeline
tags: [tenant-vector, watcher-service, validation, oid-resolution, device-registry, static-method]

# Dependency graph
requires:
  - phase: 35-01
    provides: DeviceRegistry pure store, IDeviceRegistry.ReloadAsync(List<DeviceInfo>), CommunityStringHelper public
  - phase: 32-command-map-infrastructure
    provides: CommandMapWatcherService.ValidateAndParseCommandMap static method pattern
provides:
  - TenantVectorWatcherService.ValidateAndBuildTenants (internal static) — all validation in watcher
  - TenantVectorRegistry pure store: constructor takes ILogger only, no IDeviceRegistry/IOidMapService
  - ResolveIp() deleted from TenantVectorRegistry (CLN-01)
  - Reload() trusts pre-validated, IP-resolved input (no validation loops)
  - TEN-06: CommandName stored as-is with Debug log (no command map check)
  - TEN-13 completeness gate moved to watcher
  - TenantVectorWatcherValidationTests (19 tests) covering all validation paths
  - Updated TenantVectorRegistryTests (25 tests) covering pure store behavior
affects:
  - Phase 36 (next phase — tenant vector config rename or command execution)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Watcher validates, registry stores: ValidateAndBuildTenants internal static, same as CommandMapWatcher/DeviceWatcher pattern"
    - "Pure store registry: TenantVectorRegistry constructor takes only ILogger"
    - "IP resolution in watcher: iterates IDeviceRegistry.AllDevices before passing to registry"
    - "TEN-06 deferred resolution: CommandName stored as-is, no command map check at load time"

key-files:
  created:
    - tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs
  modified:
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
    - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs

key-decisions:
  - "ValidateAndBuildTenants is synchronous (all lookups in-memory): no async needed, mirrors CommandMapWatcher pattern"
  - "TEN-06: CommandName not checked against command map at load time — deferred to execution time"
  - "IP resolution mutates MetricSlotOptions.Ip in-place before adding to cleanMetrics list (POCO with setter)"
  - "Program.cs: removed TenantVectorOptionsValidator call in local dev path; ValidateAndBuildTenants does all validation"

patterns-established:
  - "ValidateAndBuildTenants: internal static TenantVectorOptions, explicit IOidMapService + IDeviceRegistry + ILogger params"
  - "Program.cs local dev path: resolve IOidMapService + IDeviceRegistry + ILogger from DI, call static method, then Reload"

# Metrics
duration: 12min
completed: 2026-03-15
---

# Phase 35 Plan 02: TenantVectorWatcher validates, TenantVectorRegistry stores - Summary

**TenantVectorRegistry refactored to pure store (ILogger-only constructor); all 6-metric + 6-command validation, IP resolution, and TEN-13 gate moved to TenantVectorWatcherService.ValidateAndBuildTenants static method**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-14T22:34:11Z
- **Completed:** 2026-03-14T22:46:17Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments
- TenantVectorRegistry constructor simplified to `TenantVectorRegistry(ILogger<TenantVectorRegistry> logger)` — removes IDeviceRegistry and IOidMapService (CLN-02, TEN-04)
- `ResolveIp()` private method deleted from registry (CLN-01)
- `Reload()` is now a pure data-loading loop — trusts all input is pre-validated with resolved IPs
- All validation logic moved to `TenantVectorWatcherService.ValidateAndBuildTenants` (internal static)
- TenantVectorWatcherService constructor gains `IOidMapService` and `IDeviceRegistry` injections
- `HandleConfigMapChangedAsync` calls `ValidateAndBuildTenants` before `Reload`
- TEN-06: CommandName stored as-is with Debug log (no command map lookup at load time)
- TEN-13 completeness gate now in watcher (not registry)
- Program.cs local dev path updated to call `ValidateAndBuildTenants` before `Reload`
- ServiceCollectionExtensions.cs DI factory simplified to ILogger-only
- 19 new TenantVectorWatcherValidationTests covering all validation + TEN-13 + IP resolution + TEN-06
- TenantVectorRegistryTests rewritten for pure store behavior (25 tests)
- All 286 tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ValidateAndBuildTenants + refactor TenantVectorRegistry to pure store** - `fdba78c` (refactor)
2. **Task 2: Update TenantVectorRegistryTests + create TenantVectorWatcherValidationTests** - `a849a7c` (test)

**Plan metadata:** `(docs commit follows)`

## Files Created/Modified
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - Pure store: ILogger-only ctor, no validation in Reload, ResolveIp() removed
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` - Added IOidMapService + IDeviceRegistry + ValidateAndBuildTenants static method
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - TenantVectorRegistry DI factory uses ILogger only
- `src/SnmpCollector/Program.cs` - Local dev path calls ValidateAndBuildTenants before Reload (removed TenantVectorOptionsValidator call)
- `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` - NEW: 19 tests for ValidateAndBuildTenants
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - Rewritten: ILogger-only ctor, pure store tests, removed all validation tests
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` - Updated: 3-arg ctor → ILogger-only ctor (cascade fix)
- `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` - Updated: 3-arg ctor → ILogger-only ctor (cascade fix)

## Decisions Made
- `ValidateAndBuildTenants` is synchronous: all lookups (OID map, device registry) are in-memory, no async needed
- TEN-06: CommandName not checked against command map at load time — deferred to execution time (SET execution is out of scope)
- IP resolution mutates `MetricSlotOptions.Ip` in-place (POCO has setter) before adding to clean list — avoids creating new objects
- `Program.cs` local dev path now calls `ValidateAndBuildTenants` directly (removing the old `TenantVectorOptionsValidator.Validate` call)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed TenantVectorFanOutBehaviorTests 3-argument constructor cascade**
- **Found during:** Task 2 (test compilation)
- **Issue:** `TenantVectorFanOutBehaviorTests.cs` had 4 places creating `TenantVectorRegistry(IDeviceRegistry, IOidMapService, ILogger)` — old 3-arg ctor no longer exists
- **Fix:** Changed all 4 usages to `new TenantVectorRegistry(NullLogger<TenantVectorRegistry>.Instance)`
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
- **Verification:** Build succeeded; 286 tests pass
- **Committed in:** `a849a7c` (Task 2 commit)

**2. [Rule 3 - Blocking] Fixed PipelineIntegrationTests 3-argument constructor cascade**
- **Found during:** Task 2 (test compilation)
- **Issue:** `PipelineIntegrationTests.cs` had 2 DI factory registrations using the old 3-arg constructor
- **Fix:** Changed both to ILogger-only factory
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
- **Verification:** Build succeeded; 286 tests pass
- **Committed in:** `a849a7c` (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both cascade fixes expected and necessary from the constructor signature change. No scope creep.

## Issues Encountered
None beyond the expected cascade fixes from the TenantVectorRegistry constructor signature change.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- TenantVectorRegistry is a pure store; constructor takes ILogger only
- TenantVectorWatcherService owns all cross-service validation (IOidMapService, IDeviceRegistry)
- ValidateAndBuildTenants follows the established watcher-validates/registry-stores pattern
- Phase 35 complete — ready for Phase 36 (tenant vector rename, command execution, or other v1.7 work)

---
*Phase: 35-tenantvectorregistry-refactor-and-validator-activation*
*Completed: 2026-03-15*
