---
phase: 25
plan: 01
subsystem: configuration
tags: [poco, validation, ioptions, tenant-vector, oid-map]
dependency_graph:
  requires: []
  provides:
    - TenantVectorOptions POCO hierarchy (TenantVectorOptions, TenantOptions, MetricSlotOptions)
    - IOidMapService.ContainsMetricName for metric name lookup
    - TenantVectorOptionsValidator with IOidMapService injection
    - tenantvector.json dev config
    - DI registration with ValidateOnStart
  affects:
    - 25-02 (TenantVectorRegistry consumes TenantVectorOptions)
    - 25-03 (MetricSlot routing uses MetricSlotOptions shape)
tech_stack:
  added: []
  patterns:
    - IValidateOptions<T> with collect-all-errors pattern
    - FrozenSet<string> for O(1) metric name containment check
    - Volatile swap of _metricNames alongside _map in OidMapService
key_files:
  created:
    - src/SnmpCollector/Configuration/TenantVectorOptions.cs
    - src/SnmpCollector/Configuration/TenantOptions.cs
    - src/SnmpCollector/Configuration/MetricSlotOptions.cs
    - src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
    - src/SnmpCollector/config/tenantvector.json
    - tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
  modified:
    - src/SnmpCollector/Pipeline/IOidMapService.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
decisions:
  - id: D25-01
    description: "FrozenSet<string> for metric name lookup rather than LINQ Any() on FrozenDictionary values"
    rationale: "O(1) containment check; swapped atomically alongside _map for consistency"
metrics:
  duration: "~4 minutes"
  completed: "2026-03-10"
---

# Phase 25 Plan 01: Config Models and Validation Summary

TenantVector POCO hierarchy with IValidateOptions validator injecting IOidMapService for MetricName verification, DI wiring with ValidateOnStart, and 23 unit tests covering all validation rules.

## What Was Done

### Task 1: POCO Hierarchy and IOidMapService Extension
- Created `TenantVectorOptions` (SectionName = "TenantVector", List<TenantOptions>)
- Created `TenantOptions` (Id, Priority, List<MetricSlotOptions>)
- Created `MetricSlotOptions` (Ip, Port=161, MetricName, IntervalSeconds)
- Added `ContainsMetricName(string)` to IOidMapService interface
- Implemented in OidMapService with `volatile FrozenSet<string> _metricNames`
- Constructor and UpdateMap both atomically swap _metricNames alongside _map

### Task 2: Validator, DI Registration, Config File
- Created `TenantVectorOptionsValidator` implementing `IValidateOptions<TenantVectorOptions>`
- Constructor-injects IOidMapService and ILogger
- Validates: tenant ID required, duplicate IDs (case-insensitive), IP format, port range, MetricName required, MetricName in OID map, IntervalSeconds > 0, per-tenant duplicate metrics
- Skips MetricName OID map check when EntryCount == 0 (logs warning once)
- Collects all errors before returning (never throws on first error)
- Error messages use `Tenants[i].Metrics[j].Property` path format
- Registered in AddSnmpConfiguration with `.Bind().ValidateOnStart()`
- Program.cs loads tenantvector.json from config directory before Build()
- Created tenantvector.json with fiber-monitor and traffic-baseline example tenants

### Task 3: Comprehensive Unit Tests
- 23 test cases using xunit + NSubstitute
- 6 positive cases: valid config, multiple tenants, empty metrics, cross-tenant overlap, empty tenants list, negative priority
- 13 negative cases: empty/whitespace ID, duplicate IDs, invalid/empty IP, port out of range, empty MetricName, MetricName not in OID map, IntervalSeconds <= 0, duplicate metrics within tenant, multiple errors collected
- 2 OID map empty cases: skips MetricName check, still validates other rules
- 1 error message format case: verifies path context in failure messages

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed StubOidMapService compilation error**
- **Found during:** Task 3
- **Issue:** `OidResolutionBehaviorTests.StubOidMapService` did not implement the new `ContainsMetricName` method added to `IOidMapService`
- **Fix:** Added `ContainsMetricName` implementation to the stub
- **Files modified:** `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs`
- **Commit:** 24f4d5b

## Verification

- Build: `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- 0 errors, 0 warnings
- Validator tests: 23/23 passed
- Full suite: 159/161 passed (2 pre-existing failures in OidMapAutoScanTests unrelated to this plan)
- tenantvector.json: valid JSON, parses correctly

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | f137b4b | POCO hierarchy + IOidMapService.ContainsMetricName |
| 2 | 2de804b | Validator, DI registration, tenantvector.json |
| 3 | 24f4d5b | 23 unit tests + StubOidMapService fix |

## Next Phase Readiness

Phase 25 Plan 02 can proceed. TenantVectorOptions is bound and validated at startup. The POCO hierarchy provides the data model for TenantVectorRegistry to consume.
