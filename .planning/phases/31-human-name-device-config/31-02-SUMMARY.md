---
phase: 31-human-name-device-config
plan: 02
subsystem: pipeline
tags: [device-registry, oid-map, name-resolution, IOidMapService, NSubstitute, unit-tests]

requires:
  - phase: 31-01
    provides: PollOptions.MetricNames (renamed from Oids), IOidMapService.ResolveToOid interface
  - phase: 30-01
    provides: OidMapService._reverseMap volatile FrozenDictionary for reverse lookup
provides:
  - DeviceRegistry resolves MetricNames to OIDs via IOidMapService.ResolveToOid at device load time
  - BuildPollGroups() private helper shared by constructor and ReloadAsync (DRY resolution logic)
  - Structured warning logging for unresolvable metric names per device/poll/index
  - Resolution summary always logged per poll group for reload diff visibility
  - Unit tests covering all resolution scenarios (5 new test methods, 230 total passing)
affects: [31-03, MetricPollJob, DeviceWatcherService]

tech-stack:
  added: []
  patterns:
    - IOidMapService injected into DeviceRegistry (constructor injection, 3rd parameter)
    - BuildPollGroups() private helper extracts shared logic for constructor + ReloadAsync paths
    - Passthrough NSubstitute mock for IOidMapService in tests (preserves existing test behavior)
    - NSubstitute logger mock for verifying structured warning log calls

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs

key-decisions:
  - "IOidMapService injected as 2nd constructor parameter (before ILogger) -- DI resolves automatically via AddSingleton<IDeviceRegistry, DeviceRegistry>()"
  - "BuildPollGroups() private helper eliminates duplication between constructor and ReloadAsync"
  - "Device always registered even with zero resolved OIDs -- traps still need device lookup"
  - "Resolution summary always logged (not conditional) for reload diff visibility"
  - "Passthrough mock (name -> name) preserves all existing DeviceRegistryTests without modification"

patterns-established:
  - "IOidMapService.ResolveToOid called per MetricName in BuildPollGroups() -- null return means skip + warn"
  - "NSubstitute mock in test: oidMapService.ResolveToOid(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>()) for passthrough"

duration: 3m 16s
completed: 2026-03-13
---

# Phase 31 Plan 02: Name Resolution in DeviceRegistry Summary

**IOidMapService injected into DeviceRegistry; MetricNames[] resolved to OIDs at device load time via BuildPollGroups() helper shared by constructor and ReloadAsync, with structured warning logging for unresolvable names.**

## Performance

- **Duration:** 3m 16s
- **Started:** 2026-03-13T09:01:02Z
- **Completed:** 2026-03-13T09:04:18Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- DeviceRegistry constructor now accepts `IOidMapService` as 2nd parameter and resolves each MetricName to an OID via `ResolveToOid` before building `MetricPollInfo`
- `BuildPollGroups()` private helper eliminates duplication between constructor and `ReloadAsync` -- both paths now share identical resolution logic
- Unresolvable names log a structured warning (device name, metric name, poll index) and are excluded from the poll group; device always registered for trap reception
- 5 new unit tests cover all resolution scenarios; 230/230 tests pass (up from 225)

## Task Commits

1. **Task 1: Inject IOidMapService, add name resolution, update CreateRegistry helper** - `4b5fb4c` (feat)
2. **Task 2: Add unit tests for name resolution** - `95450a3` (test)

**Plan metadata:** (docs commit to follow)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - IOidMapService field + constructor param, BuildPollGroups() helper, resolution logging
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - Updated CreateRegistry helper (passthrough mock), 5 new resolution test methods, Assert.Single fix

## Decisions Made

- IOidMapService injected as 2nd constructor parameter (before ILogger) -- DI resolves automatically via `AddSingleton<IDeviceRegistry, DeviceRegistry>()`, no changes to ServiceCollectionExtensions needed
- `BuildPollGroups()` private helper extracts shared logic so constructor and ReloadAsync are both covered with a single implementation
- Device registered even with zero resolved OIDs -- needed for trap reception; this was already the case (no early return), and is now explicitly documented
- Passthrough mock (`name -> name`) for the default `CreateRegistry` helper preserves all 17 existing tests without change

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 31-03 (DeviceWatcherService: trigger re-resolution on OID map change, D-03 decision) can proceed:
- `DeviceRegistry.BuildPollGroups()` is private; if 31-03 needs to trigger re-resolution, it will call `ReloadAsync` (already has IOidMapService injected)
- `IOidMapService.ResolveToOid` integration path is verified end-to-end in unit tests
- All 230 tests pass, baseline is clean

---
*Phase: 31-human-name-device-config*
*Completed: 2026-03-13*
