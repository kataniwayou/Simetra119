---
phase: 05-trap-ingestion
plan: 04
subsystem: testing
tags: [di-registration, unit-tests, xunit, meterlistener, channels, snmp-traps, backpressure]

# Dependency graph
requires:
  - phase: 05-01
    provides: ChannelsOptions, VarbindEnvelope, IDeviceChannelManager, DeviceChannelManager, 3 trap counters on PipelineMetricService
  - phase: 05-02
    provides: SnmpTrapListenerService with ProcessDatagram auth and routing logic
  - phase: 05-03
    provides: ChannelConsumerService with ReadAllAsync drain and ISender.Send dispatch
provides:
  - DI registration of IDeviceChannelManager singleton, SnmpTrapListenerService, and ChannelConsumerService as hosted services in AddSnmpPipeline
  - ChannelsOptions binding in AddSnmpConfiguration with ValidateDataAnnotations and ValidateOnStart
  - 22 unit tests covering all Phase 5 success criteria (86 total passing)
  - InternalsVisibleTo enabling test access to ProcessDatagram
  - NonParallelCollection preventing MeterListener cross-test interference
affects:
  - Phase 6 (polling) — AddSnmpPipeline DI wiring complete; Phase 6 adds poll jobs
  - Phase 7 (leader election) — AddSnmpPipeline and AddSnmpConfiguration both extensible
  - Phase 8 (heartbeat) — pattern established for registered hosted services

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "[Collection(NonParallelCollection.Name)] for test classes using MeterListener — prevents parallel cross-test meter contamination"
    - "ProcessDatagram made internal + InternalsVisibleTo for testability without public API exposure"
    - "xUnit [CollectionDefinition(DisableParallelization=true)] for global side-effect isolation"
    - "PrimedChannelManager/CapturingChannelManager stubs for channel-based BackgroundService testing"
    - "CapturingWriter (ChannelWriter subclass) for synchronous write capture without real channels"

key-files:
  created:
    - src/SnmpCollector/Properties/AssemblyInfo.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceChannelManagerTests.cs
    - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
    - tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs
    - tests/SnmpCollector.Tests/Services/ChannelConsumerServiceTests.cs
    - tests/SnmpCollector.Tests/Helpers/NonParallelCollection.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Services/SnmpTrapListenerService.cs
    - tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs

key-decisions:
  - "ProcessDatagram changed private -> internal with InternalsVisibleTo rather than extracting to public interface — keeps architectural boundary intact while enabling unit testing"
  - "NonParallelCollection[DisableParallelization=true] used for all MeterListener-using test classes — MeterListener is a global .NET runtime listener; concurrent tests with same meter name cause cross-contamination of measurement lists"
  - "CapturingChannelManager uses custom ChannelWriter subclass (TryWrite captures to list) rather than wrapping Channel<T> — gives exact write capture without any buffering or async complexity"
  - "ChannelConsumerService tests use WaitForAsync polling helper (10ms intervals, 5s timeout) rather than Task.WhenAll or fixed delays — BackgroundService.ExecuteAsync runs on a different Task"

patterns-established:
  - "Isolated ServiceProvider per test class (AddMetrics() + fresh BuildServiceProvider()) for OTel meter isolation"
  - "MeterListener with [Collection(NonParallelCollection.Name)] for counter measurement verification"
  - "PrimedChannelManager: pre-load envelopes before Complete() so ReadAllAsync drains synchronously in test"

# Metrics
duration: 14min
completed: 2026-03-05
---

# Phase 5 Plan 4: DI Wiring and Unit Tests Summary

**All Phase 5 services wired into DI (IDeviceChannelManager singleton, listener+consumer hosted services in registration order) with 22 new unit tests covering drop/auth/routing/DropOldest/ISender.Send/counter semantics; 86 tests passing.**

## Performance

- **Duration:** 14 min
- **Started:** 2026-03-05T05:17:48Z
- **Completed:** 2026-03-05T05:31:37Z
- **Tasks:** 2
- **Files modified:** 9 (3 source, 6 test)

## Accomplishments

- ChannelsOptions binding in `AddSnmpConfiguration` with `ValidateDataAnnotations` and `ValidateOnStart` for fail-fast capacity validation
- IDeviceChannelManager singleton + SnmpTrapListenerService/ChannelConsumerService hosted services registered in correct order (listener before consumer = start order = registration order)
- 22 new unit tests across 4 test files covering all Phase 5 architectural constraints and success criteria
- `ProcessDatagram` made `internal` with `InternalsVisibleTo` — testable without exposing to production callers
- `NonParallelCollection` helper prevents `MeterListener` measurement cross-contamination in parallel xUnit test runs

## Task Commits

Each task was committed atomically:

1. **Task 1: Register Phase 5 services in DI** - `273c5b7` (feat)
2. **Task 2: Write unit tests for Phase 5 components** - `fe94e92` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - ChannelsOptions binding, IDeviceChannelManager/listener/consumer registrations
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` - ProcessDatagram changed private -> internal
- `src/SnmpCollector/Properties/AssemblyInfo.cs` - [assembly: InternalsVisibleTo("SnmpCollector.Tests")]
- `tests/SnmpCollector.Tests/Pipeline/DeviceChannelManagerTests.cs` - 8 tests: channel creation, TryWrite/ReadAllAsync end-to-end, DropOldest, CompleteAll, KeyNotFoundException
- `tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs` - 4 tests: auth_failed, unknown_device, dropped (with device_name tag), received counters
- `tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs` - 5 tests: unknown device drop, auth failure, varbind routing, envelope fields, malformed packet
- `tests/SnmpCollector.Tests/Services/ChannelConsumerServiceTests.cs` - 5 tests: ISender.Send dispatch, Source=Trap, DeviceName propagation, snmp.trap.received counter, exception resilience
- `tests/SnmpCollector.Tests/Helpers/NonParallelCollection.cs` - xUnit collection definition for sequential meter tests
- `tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs` - Added [Collection] attribute for MeterListener isolation

## Decisions Made

- **ProcessDatagram visibility:** Changed to `internal` (not extracted to interface) — keeps SnmpTrapListenerService boundary intact; tests use InternalsVisibleTo
- **NonParallelCollection:** MeterListener is a global .NET runtime listener that captures ALL meters with matching name. Parallel test classes with the same TelemetryConstants.MeterName caused cross-test measurement contamination. DisableParallelization=true is the correct xUnit solution.
- **CapturingChannelManager stub:** Implements custom ChannelWriter subclass (TryWrite appends to List) — chosen over wrapping a real Channel because it gives exact synchronous capture with no buffering delay
- **WaitForAsync polling:** ChannelConsumerService tests use 10ms-interval polling (5s timeout) to wait for the BackgroundService's async consumer loop rather than fixed Task.Delay — more reliable with no unnecessary wait time

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] TrapV2Message constructor signature corrected**
- **Found during:** Task 2 (test authoring)
- **Issue:** Plan documentation implied a 5-argument TrapV2Message constructor; SharpSnmpLib 12.5.7 uses a 6-argument constructor (requestId, version, community, enterprise, time, variables)
- **Fix:** Used correct 6-argument constructor after dynamic reflection inspection
- **Files modified:** SnmpTrapListenerServiceTests.cs
- **Verification:** TrapV2Message.ToBytes() produces parseable bytes; test trap routing succeeds

**2. [Rule 2 - Missing Critical] NonParallelCollection added to prevent MeterListener interference**
- **Found during:** Task 2 (first test run showing 3 failures from cross-test meter contamination)
- **Issue:** xUnit runs test classes in parallel; MeterListener is global; concurrent tests with same meter name contaminated each other's measurement lists
- **Fix:** Created NonParallelCollection with DisableParallelization=true; applied [Collection] attribute to all 4 MeterListener-using test classes
- **Files modified:** NonParallelCollection.cs, PipelineMetricServiceTests.cs, SnmpTrapListenerServiceTests.cs, ChannelConsumerServiceTests.cs, SnmpMetricFactoryTests.cs
- **Verification:** All 86 tests pass consistently across multiple runs

---

**Total deviations:** 2 auto-fixed (1 API signature correction, 1 test infrastructure bug)
**Impact on plan:** Both necessary for correctness. No scope creep.

## Issues Encountered

- `CapturingChannelManager` constructor initially contained a leftover debug line `_channel.Writer.TryWrite(default!)` — removed; no functional impact since the capturing uses a custom `CapturingWriter` subclass, not the backing channel's writer
- `UdpReceiveResult` is in `System.Net.Sockets` namespace (not `System.Net`) — corrected during build

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 5 (Trap Ingestion) is now **complete**. All 4 plans delivered:
- 05-01: Foundation types (ChannelsOptions, VarbindEnvelope, IDeviceChannelManager, DeviceChannelManager, 3 trap counters)
- 05-02: SnmpTrapListenerService (UDP receive, auth, channel write)
- 05-03: ChannelConsumerService (ReadAllAsync drain, ISender.Send, Source=Trap)
- 05-04: DI wiring + 22 unit tests (this plan)

**Ready for Phase 6 (Polling):**
- `AddSnmpPipeline` provides full DI registration for Phase 6 to add poll jobs
- `IDeviceRegistry.AllDevices` available for Quartz job registration
- `ISender.Send` pipeline tested end-to-end (86 tests)

**Concern (carried from Phase 5-03):** Phase 7 MetricRoleGatedExporter uses reflection on OTel internals — verify during Phase 7 planning.

---
*Phase: 05-trap-ingestion*
*Completed: 2026-03-05*
