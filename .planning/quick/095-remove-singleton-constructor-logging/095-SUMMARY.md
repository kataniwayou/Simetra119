---
phase: quick-095
plan: 01
subsystem: telemetry
tags: [singleton, DI, logging, deadlock-prevention]
dependency-graph:
  requires: []
  provides:
    - "DI-safe singleton constructors (no ILogger calls during construction)"
  affects: []
tech-stack:
  added: []
  patterns:
    - "Singleton constructors must only store field assignments -- no ILogger calls"
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/PreferredLeaderService.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
    - src/SnmpCollector/Pipeline/CommandMapService.cs
    - src/SnmpCollector/Pipeline/TrapChannel.cs
    - tests/SnmpCollector.Tests/Telemetry/MetricRoleGatedExporterTests.cs
decisions: []
metrics:
  duration: "~5 min"
  completed: "2026-03-26"
---

# Quick 095: Remove Singleton Constructor Logging

Eliminated ILogger method calls from four singleton constructors to prevent DI reentrant resolution deadlock risk under .NET DI.

## What Changed

**PreferredLeaderService** -- Removed `LogWarning` (PHYSICAL_HOSTNAME empty) and `LogInformation` (identity resolution result) from constructor. Both conditions are observable via the `IsPreferredPod` property. `UpdateStampFreshness` logging preserved.

**OidMapService** -- Removed `LogInformation` (entry count) from constructor. Observable via `EntryCount` property. `UpdateMap` hot-reload diff logging preserved.

**CommandMapService** -- Removed `LogInformation` (entry count) from constructor. Observable via `Count` property. `UpdateMap` hot-reload diff logging preserved.

**TrapChannel** -- Removed `LogInformation` (capacity) from constructor. Capacity is a config value visible in appsettings. `WaitForDrainAsync` drain logging preserved.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed MetricRoleGatedExporterTests compile error**
- **Found during:** Task 1 verification
- **Issue:** Pre-existing uncommitted change to `MetricRoleGatedExporter` changed constructor parameter from `ILeaderElection` to `Lazy<ILeaderElection>`, but test file was not updated, preventing the entire test project from compiling.
- **Fix:** Wrapped `StubLeaderElection` instances in `new Lazy<ILeaderElection>(() => ...)` at three call sites in the test file.
- **Files modified:** `tests/SnmpCollector.Tests/Telemetry/MetricRoleGatedExporterTests.cs`
- **Commit:** 9ebf5c4

## Verification

- `dotnet build` succeeds with 0 warnings, 0 errors
- All 524 tests pass (0 failures, 0 skipped)
- `grep` confirms zero Log calls inside any constructor body across all four files

## Commits

| # | Hash | Description |
|---|------|-------------|
| 1 | 9ebf5c4 | Remove constructor logging from PreferredLeaderService + fix test compile error |
| 2 | 68e0e3c | Remove constructor logging from OidMapService, CommandMapService, TrapChannel |
