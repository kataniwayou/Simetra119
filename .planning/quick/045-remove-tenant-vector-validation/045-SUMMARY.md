---
phase: quick
plan: 045
subsystem: configuration
tags: [validation, tenant-vector, no-op]
dependency-graph:
  requires: [25-01]
  provides: [unconditional-tenant-creation]
  affects: []
tech-stack:
  added: []
  patterns: [no-op-validator]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
    - src/SnmpCollector/Pipeline/IOidMapService.cs
    - tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
decisions:
  - id: Q045-01
    description: "Keep DI registration unchanged — parameterless validator still activates via concrete-first pattern"
metrics:
  duration: "3 minutes"
  completed: 2026-03-11
---

# Quick Task 045: Remove Tenant Vector Validation Summary

**One-liner:** No-op TenantVectorOptionsValidator that always returns Success, eliminating false-positive validation failures from forward-referenced metric names or unloaded OID maps.

## What Was Done

### Task 1: Gut validator and clean up DI and IOidMapService doc
- Removed all validation rules from TenantVectorOptionsValidator.Validate() — now returns ValidateOptionsResult.Success unconditionally
- Removed IOidMapService and ILogger constructor dependencies (class is now parameterless)
- Removed unused `using` directives (System.Net, SnmpCollector.Pipeline, Microsoft.Extensions.Logging)
- Updated IOidMapService.ContainsMetricName XML doc to remove validator reference
- DI registration in ServiceCollectionExtensions.cs unchanged (concrete-first pattern still works)
- **Commit:** ba4d735

### Task 2: Update test suite for unconditional acceptance
- Deleted 11 negative/OID-map/error-format tests
- Updated 6 positive tests to use parameterless `new TenantVectorOptionsValidator()` (no mocks)
- Added 3 new unconditional acceptance tests (empty IP, invalid port, empty metric name)
- Removed NSubstitute, IOidMapService, ILogger mock dependencies from test file
- 9 tests pass; full suite 194/194 green
- **Commit:** ac07691

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| Q045-01 | Keep DI registration unchanged | Parameterless validator still activates via AddSingleton concrete-first pattern; no wiring changes needed |

## Deviations from Plan

None — plan executed exactly as written.

## Verification

| Check | Result |
|-------|--------|
| `dotnet build src/SnmpCollector/SnmpCollector.csproj` | 0 errors, 0 warnings |
| `dotnet test --filter TenantVectorOptionsValidator` | 9/9 passed |
| `dotnet test` (full suite) | 194/194 passed |
