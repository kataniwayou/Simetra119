---
phase: 35-tenantvectorregistry-refactor-and-validator-activation
plan: 01
subsystem: pipeline
tags: [device-registry, watcher-service, dns-resolution, community-string, oid-resolution, frozen-dictionary]

# Dependency graph
requires:
  - phase: 34-communitystring-validation-metricpolljob-cleanup
    provides: DeviceRegistry duplicate IP+Port, CS validation, BuildPollGroups, Phase 34 patterns
  - phase: 32-command-map-infrastructure
    provides: CommandMapWatcherService.ValidateAndParseCommandMap static method pattern
provides:
  - DeviceWatcherService.ValidateAndBuildDevicesAsync (internal static async) - all validation in watcher
  - DeviceRegistry pure store: constructor takes ILogger only, ReloadAsync accepts List<DeviceInfo>
  - IDeviceRegistry.ReloadAsync(List<DeviceInfo>) updated interface
  - CommunityStringHelper made public (accessible from Services namespace)
  - DevicesOptionsValidator simplified to no-op
  - DeviceWatcherValidationTests (12 tests) covering all validation paths
  - Updated DeviceRegistryTests (16 tests) covering pure store behavior
affects:
  - phase 35-02 (TenantVectorRegistry refactor - depends on IDeviceRegistry being clean)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Watcher validates, registry stores: ValidateAndBuildDevicesAsync internal static async, same as CommandMapWatcher pattern"
    - "Pure store registry: DeviceRegistry constructor takes only ILogger, starts with FrozenDictionary.Empty"
    - "ReloadAsync(List<DeviceInfo>): registry accepts pre-validated data, no DNS/OID/validation logic"
    - "DI factory registration: new DeviceRegistry(sp.GetRequiredService<ILogger<DeviceRegistry>>())"

key-files:
  created:
    - tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs
  modified:
    - src/SnmpCollector/Pipeline/CommunityStringHelper.cs
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
    - src/SnmpCollector/Services/DeviceWatcherService.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    - src/SnmpCollector/Program.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
    - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs

key-decisions:
  - "CommunityStringHelper made public (not moved): simplest path for Services namespace access without namespace churn"
  - "DeviceRegistry constructor takes only ILogger (not List<DeviceInfo>): starts empty, loaded via ReloadAsync"
  - "DevicesOptionsValidator is a full no-op (returns Success always): per-entry validation lives entirely in watcher"
  - "BuildPollGroups moved to DeviceWatcherService as private static helper: removes IOidMapService from registry"
  - "DNS resolution: Dns.GetHostAddressesAsync with AddressFamily.InterNetwork filter, same as prior registry code"

patterns-established:
  - "ValidateAndBuildDevicesAsync: internal static async Task<List<DeviceInfo>>, explicit IOidMapService + ILogger params"
  - "Program.cs local dev path: resolve IOidMapService + ILogger from DI, call static method, then ReloadAsync"

# Metrics
duration: 6min
completed: 2026-03-15
---

# Phase 35 Plan 01: DeviceWatcher validates, DeviceRegistry stores - Summary

**DeviceRegistry refactored to pure FrozenDictionary store; all validation (CS extraction, async DNS, OID resolution, dup detection) moved to DeviceWatcherService.ValidateAndBuildDevicesAsync static method**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-14T22:25:16Z
- **Completed:** 2026-03-14T22:31:09Z
- **Tasks:** 2
- **Files modified:** 10

## Accomplishments
- DeviceRegistry constructor simplified to `DeviceRegistry(ILogger<DeviceRegistry> logger)` — starts empty
- IDeviceRegistry.ReloadAsync signature changed from `List<DeviceOptions>` to `List<DeviceInfo>`
- All validation logic moved to `DeviceWatcherService.ValidateAndBuildDevicesAsync` (internal static async)
- CommunityStringHelper promoted from `internal` to `public` for cross-namespace access
- DevicesOptionsValidator simplified to no-op (all per-entry validation now in watcher)
- Program.cs local dev path updated to call ValidateAndBuildDevicesAsync before ReloadAsync
- 12 new DeviceWatcherValidationTests covering all validation paths
- DeviceRegistryTests rewritten to use List<DeviceInfo> directly (16 pure store tests)
- All 279 tests pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Create ValidateAndBuildDevicesAsync + refactor DeviceRegistry to pure store** - `2b39024` (refactor)
2. **Task 2: Update DeviceRegistryTests + create DeviceWatcherValidationTests** - `2b5f4a9` (test)

**Plan metadata:** `(docs commit follows)`

## Files Created/Modified
- `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` - Changed internal → public (class + methods)
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - Pure store: only ILogger ctor, ReloadAsync(List<DeviceInfo>), no validation
- `src/SnmpCollector/Pipeline/IDeviceRegistry.cs` - ReloadAsync signature: List<DeviceOptions> → List<DeviceInfo>
- `src/SnmpCollector/Services/DeviceWatcherService.cs` - Added IOidMapService param + ValidateAndBuildDevicesAsync + BuildPollGroups static methods
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - DeviceRegistry DI uses explicit factory (ILogger only)
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` - Simplified to no-op (returns Success)
- `src/SnmpCollector/Program.cs` - Local dev path calls ValidateAndBuildDevicesAsync then ReloadAsync
- `tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` - NEW: 12 tests for ValidateAndBuildDevicesAsync
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - Rewritten: List<DeviceInfo> inputs, 16 pure store tests
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` - StubDeviceRegistry.ReloadAsync signature fix
- `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` - StubDeviceRegistry.ReloadAsync signature fix

## Decisions Made
- CommunityStringHelper made public (not moved to shared namespace): minimal change, no namespace churn
- DeviceRegistry constructor takes only ILogger, starts with FrozenDictionary.Empty — no initial device loading
- DevicesOptionsValidator becomes a full no-op: per-entry validation belongs exclusively in the watcher
- BuildPollGroups moved from DeviceRegistry to DeviceWatcherService as private static helper — removes IOidMapService from registry entirely
- DNS resolution kept as Dns.GetHostAddressesAsync with AddressFamily.InterNetwork filter (exact same logic as prior)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed StubDeviceRegistry in TenantVectorFanOutBehaviorTests and MetricPollJobTests**
- **Found during:** Task 2 (test compilation)
- **Issue:** Two test files had inner `StubDeviceRegistry` classes implementing `IDeviceRegistry.ReloadAsync(List<DeviceOptions>)` — this no longer matches the updated interface
- **Fix:** Changed both stub implementations to `ReloadAsync(List<DeviceInfo>)` to match the new interface signature
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs, tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
- **Verification:** Build succeeded; all 279 tests pass
- **Committed in:** `2b5f4a9` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Necessary cascade fix from interface signature change. No scope creep.

## Issues Encountered
None beyond the expected StubDeviceRegistry stub cascade.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- DeviceRegistry is a pure store; ready for Phase 35-02 (TenantVectorRegistry refactor)
- IDeviceRegistry.ReloadAsync(List<DeviceInfo>) is the clean interface for 35-02 to depend on
- DeviceWatcherService.ValidateAndBuildDevicesAsync establishes the static validation method pattern for TenantVectorWatcherService to follow

---
*Phase: 35-tenantvectorregistry-refactor-and-validator-activation*
*Completed: 2026-03-15*
