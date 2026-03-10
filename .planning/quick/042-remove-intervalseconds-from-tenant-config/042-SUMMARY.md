# Quick Task 042: Remove IntervalSeconds from Tenant Vector Config Summary

**One-liner:** Removed IntervalSeconds from MetricSlotOptions config model; TenantVectorRegistry now derives it from DeviceRegistry poll groups via OidMapService OID resolution at Reload time.

## What Changed

### Task 1: Remove IntervalSeconds from config model, validator, and all config files
- **MetricSlotOptions.cs** -- Removed `IntervalSeconds` property and its XML doc comment
- **TenantVectorOptionsValidator.cs** -- Removed Rule 7 (IntervalSeconds > 0 validation), renumbered Rule 8 to Rule 7
- **tenantvector.json** -- Stripped IntervalSeconds from all metric entries (local dev config)
- **configmap.yaml** -- Stripped IntervalSeconds from all tenant vector metric entries and updated comment block
- **simetra-tenantvector.yaml** -- Stripped IntervalSeconds from all metric entries (dev deployment)
- **28-tenantvector-routing.sh** -- Stripped IntervalSeconds from all inline JSON (14 occurrences)
- Commit: `92a9c17`

### Task 2: Wire TenantVectorRegistry to derive IntervalSeconds and update all tests
- **TenantVectorRegistry.cs** -- Added IDeviceRegistry + IOidMapService constructor injection; added `DeriveIntervalSeconds` helper that walks device PollGroups, resolves each OID via OidMapService, and returns the matching poll group's IntervalSeconds
- **ServiceCollectionExtensions.cs** -- Updated DI registration to pass IDeviceRegistry and IOidMapService
- **TenantVectorRegistryTests.cs** -- Added NSubstitute mocks for new dependencies; removed interval tuple parameter from CreateOptions; removed IntervalSeconds assertions from single-tenant test
- **TenantVectorOptionsValidatorTests.cs** -- Removed IntervalSeconds from all MetricSlotOptions instances; deleted Validate_IntervalSecondsZero_Fails and Validate_IntervalSecondsNegative_Fails tests
- **TenantVectorFanOutBehaviorTests.cs** -- Updated all registry factory helpers with mock dependencies; removed IntervalSeconds from MetricSlotOptions
- **PipelineIntegrationTests.cs** -- Updated TenantVectorRegistry constructor calls with new dependencies
- Commit: `bd4ad96`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated ServiceCollectionExtensions.cs DI registration**
- **Found during:** Task 2
- **Issue:** TenantVectorRegistry constructor signature changed but DI registration in ServiceCollectionExtensions.cs was not listed in plan files
- **Fix:** Updated the `AddSingleton<TenantVectorRegistry>` lambda to pass IDeviceRegistry and IOidMapService from the service provider
- **Files modified:** `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs`
- **Commit:** `bd4ad96`

**2. [Rule 3 - Blocking] Updated PipelineIntegrationTests.cs constructor calls**
- **Found during:** Task 2
- **Issue:** PipelineIntegrationTests.cs had two direct constructor calls to TenantVectorRegistry that needed the new parameters
- **Fix:** Updated both constructor calls to pass mock IDeviceRegistry and IOidMapService
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs`
- **Commit:** `bd4ad96`

## Verification

- Clean build: `dotnet build` -- 0 errors, 0 warnings
- All tests: `dotnet test` -- 205 passed, 0 failed
- No IntervalSeconds in config model, validator, config files, or e2e scripts
- IntervalSeconds remains only in MetricSlotHolder (runtime property) and device-related code (MetricPollOptions/MetricPollInfo)

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| D042-01 | DeriveIntervalSeconds returns 0 when device or metric not found | Graceful fallback -- prevents crash on misconfigured tenant vector entries; staleness detection will simply not trigger |

## Metrics

- **Duration:** ~8.5 minutes
- **Completed:** 2026-03-11
- **Tests:** 205 passed (net change: -2 removed IntervalSeconds validator tests)
