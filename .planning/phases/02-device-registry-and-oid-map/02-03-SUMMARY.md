---
phase: 02-device-registry-and-oid-map
plan: 03
subsystem: testing
tags: [dotnet, xunit, tdd, OidMapService, DeviceRegistry, IOptionsMonitor, hot-reload, unit-tests]

# Dependency graph
requires:
  - phase: 02-device-registry-and-oid-map/02-02
    provides: OidMapService (volatile FrozenDictionary, IOptionsMonitor hot-reload), DeviceRegistry (FrozenDictionary by IP and name, community string fallback)
provides:
  - xUnit test project (tests/SnmpCollector.Tests) targeting net9.0 with ProjectReference to SnmpCollector
  - TestOptionsMonitor<T> helper for simulating IOptionsMonitor OnChange callbacks in-process
  - OidMapServiceTests: 6 tests covering known OID, unknown OID, empty map, EntryCount, hot-reload add, hot-reload remove
  - DeviceRegistryTests: 10 tests covering IP lookup, IPv6-mapped IPv4, unknown IP, name lookup (exact + case-insensitive), unknown name, AllDevices count, community fallback, community override, JobKey
affects:
  - Phase 3 (MediatR pipeline): behavioral contracts locked -- OidMapService.Resolve and DeviceRegistry.TryGetDevice/TryGetDeviceByName verified
  - Future test phases: TestOptionsMonitor helper available for any IOptionsMonitor-dependent service

# Tech tracking
tech-stack:
  added:
    - xunit 2.9.3 -- xUnit test framework
    - xunit.runner.visualstudio 2.8.2 -- VS Test adapter
    - Microsoft.NET.Test.Sdk 17.12.0 -- test runner infrastructure
    - Microsoft.Extensions.Logging.Abstractions 9.0.0 -- NullLogger<T> for test construction
  patterns:
    - "TDD with pre-existing implementation: RED commit compiles but tests cover all behaviors; GREEN pass confirms implementation correctness"
    - "TestOptionsMonitor<T>: in-memory IOptionsMonitor that triggers OnChange directly, no IHost required"
    - "Direct instantiation pattern: new OidMapService(monitor, NullLogger) and new DeviceRegistry(Options.Create(...), Options.Create(...))"
    - "Release configuration for dotnet test -- avoids Debug exe file-lock (SnmpCollector.exe running in background)"

key-files:
  created:
    - tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj
    - tests/SnmpCollector.Tests/Helpers/TestOptionsMonitor.cs
    - tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
  modified: []

key-decisions:
  - "xunit 2.x (not 3.x) -- 2.9.3 is latest stable 2.x; 3.x changes assertion model and isn't needed here"
  - "Release configuration for dotnet test -- Debug build fails due to SnmpCollector.exe file-lock (pre-existing issue from 02-02)"
  - "Explicit 'using Xunit;' required -- xUnit attributes not available via ImplicitUsings (not in SDK defaults)"

patterns-established:
  - "Test construction pattern: TestOptionsMonitor<T> wraps options + NullLogger for direct service instantiation"
  - "Hot-reload test pattern: monitor.Change(newOptions) triggers OnChange synchronously in test context"
  - "IPv6-mapped test: IPAddress.Parse('::ffff:10.0.10.1') covers trap receiver path normalization"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 2 Plan 3: OID Resolution and Device Registry Unit Tests Summary

**xUnit test project with TestOptionsMonitor<T> helper and 16 passing tests locking in OidMapService hot-reload and DeviceRegistry IPv4/name/community-string contracts without a running host**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-05T00:19:00Z
- **Completed:** 2026-03-05T00:20:54Z
- **Tasks:** 1 (TDD: RED commit + GREEN verification)
- **Files modified:** 4 (all created)

## Accomplishments

- Created xUnit test project (`tests/SnmpCollector.Tests`) targeting net9.0 with all required NuGet references and ProjectReference to SnmpCollector
- Created `TestOptionsMonitor<T>` helper that allows tests to trigger `IOptionsMonitor.OnChange` callbacks synchronously without a running host
- 16 tests pass across two test classes: 6 OidMapService tests and 10 DeviceRegistry tests
- Phase 2 Success Criterion #2 satisfied: OID resolution and device lookup are verified in unit tests without a running host

## Task Commits

Each task was committed atomically:

1. **Task 1: Create test project, TestOptionsMonitor helper, OidMapServiceTests, DeviceRegistryTests** - `895ebd2` (test)

**Plan metadata:** (committed after SUMMARY creation)

_Note: TDD task produced RED commit (test files compile, tests written) + GREEN verification (all 16 pass). No separate feat commit needed -- implementation already existed from 02-02._

## Files Created/Modified

- `tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` - xUnit test project: net9.0, xunit 2.9.3, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Microsoft.Extensions.Logging.Abstractions, ProjectReference to SnmpCollector
- `tests/SnmpCollector.Tests/Helpers/TestOptionsMonitor.cs` - `TestOptionsMonitor<T>`: in-memory `IOptionsMonitor<T>` with `Change(T)` method to fire OnChange callbacks synchronously
- `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs` - 6 tests: `Resolve_KnownOid_ReturnsMetricName`, `Resolve_UnknownOid_ReturnsUnknown`, `Resolve_EmptyMap_AlwaysReturnsUnknown`, `EntryCount_MatchesDictionarySize`, `Resolve_AfterReload_NewOidResolves`, `Resolve_AfterReload_RemovedOidReturnsUnknown`
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - 10 tests: IP lookup, IPv6-mapped IPv4, unknown IP, exact name, case-insensitive name, unknown name, AllDevices count, community fallback, community override, JobKey

## Decisions Made

- `xunit` version 2.9.3 (not 3.x) -- latest stable 2.x; v3 changes the assertion API and adds complexity not needed for these unit tests
- `dotnet test -c Release` required -- `dotnet build -c Debug` fails with MSB3027 file-lock when SnmpCollector.exe is running (pre-existing behavior documented in 02-02); Release configuration avoids the lock
- Explicit `using Xunit;` required in test files -- `ImplicitUsings` only adds BCL namespaces, not xUnit attributes

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added explicit `using Xunit;` to test files**

- **Found during:** Task 1, first build attempt
- **Issue:** `[Fact]` attribute resolved as `CS0246 FactAttribute not found` -- `ImplicitUsings` does not include xUnit namespaces, only standard BCL ones
- **Fix:** Added `using Xunit;` to `OidMapServiceTests.cs` and `DeviceRegistryTests.cs`
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs`, `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs`
- **Verification:** `dotnet build -c Release` succeeds with 0 errors
- **Committed in:** `895ebd2` (test commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Single missing using directive; no scope change.

## Issues Encountered

**Build file-lock (pre-existing):** `dotnet build -c Debug` fails with MSB3027 when `SnmpCollector.exe` is running (same as 02-01, 02-02). Used `-c Release` for all build and test commands. Zero `error CS` compile errors confirmed.

## User Setup Required

None -- test project has no external dependencies beyond NuGet packages restored automatically.

## Next Phase Readiness

- `IOidMapService` and `IDeviceRegistry` behavioral contracts locked by unit tests
- `OidMapService.Resolve()` hot-reload behavior verified via `TestOptionsMonitor<T>`
- `DeviceRegistry` IPv6-mapped IPv4 normalization verified (`::ffff:10.0.10.1` -> `10.0.10.1`)
- `MetricPollInfo.JobKey()` pattern verified: `"metric-poll-{deviceName}-{pollIndex}"`
- `TestOptionsMonitor<T>` reusable for any future IOptionsMonitor-dependent service test
- No blockers

---
*Phase: 02-device-registry-and-oid-map*
*Completed: 2026-03-05*
