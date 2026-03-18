---
phase: 56-tenant-validation-hardening
plan: 02
subsystem: tenant-validation
tags: [validation, deduplication, command-map, ip-resolution]
dependency-graph:
  requires: ["56-01"]
  provides: ["duplicate-tenant-detection", "duplicate-metric-detection", "duplicate-command-detection", "command-ip-resolution", "command-name-existence-check"]
  affects: []
tech-stack:
  added: []
  patterns: ["per-entry-skip-semantics", "hashset-deduplication", "ip-resolution-via-alldevices"]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
    - src/SnmpCollector/Program.cs
    - tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs
decisions:
  - id: 56-02-01
    description: "CommandName not in command map skips command (Error + continue), consistent with TEN-05 MetricName check"
  - id: 56-02-02
    description: "Duplicate metric key uses resolved IP (post-AllDevices loop), so hostname and IP variants don't falsely de-duplicate"
  - id: 56-02-03
    description: "Command IP resolution mirrors metric IP resolution pattern exactly (AllDevices loop + unresolved skip)"
  - id: 56-02-04
    description: "Duplicate command key uses resolved IP, same rationale as duplicate metric key"
metrics:
  duration: ~5 min
  completed: 2026-03-18
  test-results: "453 passed, 0 failed"
---

# Phase 56 Plan 02: Structural Additions (Duplicates + Command IP + CommandName) Summary

**One-liner:** Duplicate tenant/metric/command detection with skip-first-kept semantics, command IP resolution via AllDevices, and CommandName existence check via ICommandMapService.

## What Was Done

### Task 1: Structural additions to ValidateAndBuildTenants

- Added `ICommandMapService` parameter to `ValidateAndBuildTenants` method signature and wired from constructor field
- Updated both call sites (HandleConfigMapChangedAsync and Program.cs local-dev path)
- Added duplicate tenant Name detection before the main loop body (HashSet, case-insensitive; skip duplicate, keep first)
- Added duplicate metric detection after IP resolution (key = `{resolvedIp}:{Port}:{MetricName}`)
- Added duplicate command detection after IP resolution (key = `{resolvedIp}:{Port}:{CommandName}`)
- Replaced TEN-06 pass-through debug log with CommandName existence check: `commandMapService.ResolveCommandOid()` returns null -> Error + skip
- Added command IP resolution via AllDevices loop (mirrors metric pattern exactly)
- Added command hostname unresolved -> Error + skip (mirrors metric pattern)

### Task 2: Unit tests (8 new tests)

1. `DuplicateTenantName_SecondSkipped` -- two tenants with same Name, first kept
2. `DuplicateTenantName_FirstKeptWithCorrectMetrics` -- verifies first tenant's metrics survive
3. `NoDuplicates_AllTenantsLoad` -- regression guard, 3 unique tenants all load
4. `DuplicateMetricInTenant_SecondSkipped` -- same Ip+Port+MetricName, count reduced by 1
5. `DuplicateCommandInTenant_SecondSkipped` -- same Ip+Port+CommandName, count reduced by 1
6. `CommandIpResolved_MatchesDeviceResolvedIp` -- hostname -> resolved IP on command
7. `CommandNameNotInMap_CommandSkipped` -- ResolveCommandOid returns null, command skipped
8. `CommandIpNotResolved_CommandSkipped` -- hostname not in AllDevices, command skipped

Updated `UnresolvableCommandName_StoredAsIs` -> `CommandNameNotInMap_CommandSkipped` (behavior changed from pass-through to skip).

All existing tests updated with `ICommandMapService` parameter via `CreatePassthroughCommandMapService()` helper.

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | CommandName not in map -> Error + skip | Consistent with TEN-05 MetricName check; if command map loads after tenant config, commands are skipped until next reload |
| 2 | Duplicate metric key uses resolved IP | Post-resolution key prevents hostname+IP of same device from falsely surviving as distinct |
| 3 | Command IP resolution mirrors metric pattern | Identical AllDevices loop + unresolved skip for consistency |
| 4 | Duplicate command key uses resolved IP | Same rationale as metric key |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Program.cs call site also needed ICommandMapService parameter**

- **Found during:** Task 1 build verification
- **Issue:** Program.cs has a second call to ValidateAndBuildTenants for local-dev config loading
- **Fix:** Added `commandMapService` resolution and parameter to Program.cs call site
- **Files modified:** src/SnmpCollector/Program.cs
- **Commit:** b5c3142

**2. [Rule 1 - Bug] UnresolvableCommandName_StoredAsIs test now invalid**

- **Found during:** Task 2
- **Issue:** Old test asserted pass-through behavior that was replaced by skip behavior
- **Fix:** Replaced with CommandNameNotInMap_CommandSkipped test
- **Files modified:** tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs
- **Commit:** 96a1e8e

## Test Results

Full suite: 453 passed, 0 failed, 0 skipped.
