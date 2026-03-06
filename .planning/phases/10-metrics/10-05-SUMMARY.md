---
phase: "10-metrics"
plan: "05"
subsystem: "test-suite"
tags: ["tests", "community-string", "label-taxonomy", "ITrapChannel"]
depends_on:
  requires: ["10-04"]
  provides: ["full-test-coverage-phase-10"]
  affects: []
tech_stack:
  added: []
  patterns: ["community-string-convention-tests", "ITrapChannel-test-doubles"]
key_files:
  created: []
  modified:
    - "tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs"
    - "tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs"
    - "tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs"
    - "tests/SnmpCollector.Tests/Pipeline/Behaviors/ValidationBehaviorTests.cs"
    - "tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs"
    - "tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs"
    - "tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs"
    - "tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs"
    - "tests/SnmpCollector.Tests/Services/ChannelConsumerServiceTests.cs"
    - "tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs"
    - "tests/SnmpCollector.Tests/Lifecycle/GracefulShutdownServiceTests.cs"
  deleted:
    - "tests/SnmpCollector.Tests/Pipeline/DeviceChannelManagerTests.cs"
decisions: []
metrics:
  duration: "~18 min"
  completed: "2026-03-06"
---

# Phase 10 Plan 05: Test Suite Update Summary

All test files updated to match Phase 10 production changes. 115 tests pass with zero failures.

## What Changed

### Task 1: Test Helpers and Pipeline/Telemetry Tests

1. **TestSnmpMetricFactory** -- Updated interface from `(metricName, oid, agent, source, snmpType, value)` to `(metricName, oid, deviceName, ip, source, snmpType, value)`. Tuple field names now reflect the split: `DeviceName` + `Ip` instead of `Agent`.

2. **OtelMetricHandlerTests** -- Removed `SiteOptions` dependency from constructor. Assertions updated from `record.Agent` to `record.DeviceName` + `record.Ip`. Fallback test renamed from `FallsBackToAgentIpWhenDeviceNameNull` to `FallsBackToUnknownWhenDeviceNameNull` (handler uses "unknown" for null DeviceName). Truncation test updated to pass 7 parameters.

3. **PipelineIntegrationTests** -- Removed `SnmpListenerOptions` registration. Removed `CommunityString` from `DeviceOptions`. Removed `IDeviceRegistry` manual registration (AddSnmpPipeline handles it). Label assertions updated: `record.Agent` -> `record.DeviceName` + `record.Ip`. ThrowingSnmpMetricFactory updated for 7-parameter signature.

4. **ValidationBehaviorTests** -- Removed `IDeviceRegistry` dependency entirely. Removed `StubDeviceRegistry` inner class. `CreateBehavior()` now takes only logger + metrics (matching production `ValidationBehavior` which no longer accepts `IDeviceRegistry`). Device registry lookup tests replaced with `MissingDeviceName` rejection test.

5. **DeviceRegistryTests** -- Removed `SnmpListenerOptions` from `CreateRegistry()`. Removed `CommunityString` from `DeviceOptions` test data. Deleted `CommunityString_FallsBackToGlobal` and `CommunityString_UsesOverride` tests (community string is now derived at usage time via `CommunityStringHelper.DeriveFromDeviceName`).

6. **SnmpMetricFactoryTests** -- Removed `IOptions<SiteOptions>` from constructor. Labels updated: `site_name` -> `host_name` (environment-derived), `agent` -> `device_name` + `ip`. Gauge now has 7 labels (was 6), Info now has 8 labels (was 7).

7. **PipelineMetricServiceTests** -- Removed `IOptions<SiteOptions>` from constructor. Label assertions updated: `site_name` -> `host_name`. All trap counter tests verify `host_name` tag (environment-derived).

### Task 2: Service/Channel/Job Tests

1. **DeviceChannelManagerTests.cs** -- Deleted entirely. `DeviceChannelManager` was removed in Phase 10 (replaced by single shared `TrapChannel`).

2. **SnmpTrapListenerServiceTests** -- Complete rewrite. Removed `IDeviceRegistry` and `IDeviceChannelManager` dependencies. Constructor now takes `ITrapChannel` (matching production). New test suite verifies Simetra.{DeviceName} community string convention: valid community writes to channel, `public` drops with auth_failed, empty community drops, `Simetra.` (no device name) drops. Uses `CapturingTrapChannel` and `NoOpTrapChannel` test doubles.

3. **ChannelConsumerServiceTests** -- Replaced `IDeviceChannelManager` with `ITrapChannel`. `PrimedChannelManager` replaced with `PrimedTrapChannel`. `CreateService()` updated to match production constructor signature. Removed `SiteOptions` from `PipelineMetricService` construction.

4. **MetricPollJobTests** -- Removed `CommunityString` from `DeviceInfo` construction (3 params instead of 4). Removed `SiteOptions` from `PipelineMetricService` construction.

5. **GracefulShutdownServiceTests** -- Replaced `IDeviceChannelManager` with `ITrapChannel`. `StubChannelManager` replaced with `StubTrapChannel`. `CompleteAll()` -> `Complete()`. `WaitForDrainAsync()` signature unchanged.

## Verification Results

- `dotnet test` -- 115 tests pass, 0 failures
- `grep IDeviceChannelManager tests/` -- 0 matches
- `grep site_name tests/` -- 0 matches
- `grep .CommunityString tests/` -- 0 matches (property access)
- `grep "agent" tests/` -- 0 matches (as label name)

## Deviations from Plan

None -- plan executed exactly as written.

## Next Phase Readiness

Phase 10 is complete. All 5 plans executed:
- 10-01: Community string convention + CommunityStringHelper
- 10-02: Label taxonomy redesign (host_name, device_name, ip)
- 10-03: Single shared TrapChannel (ITrapChannel/TrapChannel)
- 10-04: Poll path alignment, validation, DI wiring
- 10-05: Test suite update (this plan)

Production code builds with 0 errors. All 115 tests pass.
