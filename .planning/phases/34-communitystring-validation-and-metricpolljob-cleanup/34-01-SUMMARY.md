---
phase: 34-communitystring-validation-and-metricpolljob-cleanup
plan: 01
subsystem: pipeline
tags: [csharp, device-registry, snmp, validation, oid-map, quartz]

# Dependency graph
requires:
  - phase: 31-human-name-device-config
    provides: DeviceOptions.CommunityString as primary device identifier; IOidMapService.ResolveToOid
  - phase: 32-command-map-infrastructure
    provides: CommandMapService pattern for independent ConfigMap watchers

provides:
  - DeviceRegistry with skip-based duplicate IP+Port handling (no throw)
  - DeviceRegistry with duplicate CommunityString Warning (DEV-10)
  - BuildPollGroups filtering zero-OID poll groups (DEV-08)
  - Operator config ordering guidance documented in AddSnmpPipeline (CS-07)
  - Updated DeviceRegistryTests: 22 tests covering all new behaviors

affects:
  - 34-02 (TenantVectorRegistry validation - same phase, next plan)
  - DynamicPollScheduler (benefits from zero-OID group filtering - no empty Quartz jobs registered)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Skip+Error log instead of throw for runtime duplicate detection"
    - "seenCommunityStrings Dictionary<string,string> tracking per reload loop"
    - "for-loop with index in BuildPollGroups for inline filtering"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/DeviceRegistry.cs
    - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs

key-decisions:
  - "Duplicate IP+Port: skip+Error semantics (not throw) - per-entry skip allows config reload to survive one bad entry"
  - "Duplicate CommunityString: Warning only, both devices load - CommunityString is not a uniqueness constraint"
  - "Zero-OID poll groups: filtered in BuildPollGroups inline loop (not in caller) - colocated with OID resolution logic"
  - "seenCommunityStrings uses TryAdd pattern symmetric across constructor and ReloadAsync"

patterns-established:
  - "Skip+Error pattern: LogError + continue (not throw) for runtime per-entry validation"
  - "Warning-only pattern: LogWarning + continue loading for non-critical duplication"
  - "Zero-OID group skip: filter inline during BuildPollGroups with continue; device itself always registers"

# Metrics
duration: 4min
completed: 2026-03-14
---

# Phase 34 Plan 01: CommunityString Validation & MetricPollJob Cleanup Summary

**DeviceRegistry skip+Error for duplicate IP+Port, Warning-only for duplicate CommunityString (DEV-10), zero-OID poll group filtering (DEV-08), and CS-07 operator config ordering docs**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-14T20:59:44Z
- **Completed:** 2026-03-14T21:03:44Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- DeviceRegistry no longer throws on duplicate IP+Port — logs Error and skips the second device, symmetric in constructor and ReloadAsync
- Duplicate CommunityString across devices with different IP+Port logs Warning but both devices load (DEV-10)
- BuildPollGroups now filters out poll groups with zero resolved OIDs (DEV-08); devices with all-unresolvable names still register for trap reception but have empty PollGroups
- XML doc `<remarks>` block added to `AddSnmpPipeline` documenting recommended oidmaps/commandmaps → devices → tenants apply order (CS-07)
- DeviceRegistryTests updated from 18 to 22 tests: renamed throw test, 4 new behavioral tests

## Task Commits

Each task was committed atomically:

1. **Task 1: DeviceRegistry -- duplicate IP+Port skip, CommunityString Warning, zero-OID filter** - `7de1a7e` (feat)
2. **Task 2: DeviceRegistryTests -- update throw test and add DEV-08/DEV-10 tests** - `656f9aa` (test)
3. **Task 3: Document operator config ordering guidance (CS-07)** - `c28f347` (docs)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - Duplicate IP+Port skip+Error, CommunityString Warning, BuildPollGroups zero-OID filter
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` - Updated/renamed 1 test, 4 new tests (22 total, all passing)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - CS-07 operator config ordering remarks on AddSnmpPipeline

## Decisions Made

- **Skip+Error over throw**: The plan specified skip+Error; both constructor and ReloadAsync needed symmetric treatment. Using `TryAdd` for `seenCommunityStrings` cleanly handles the case where the first device's CommunityString registration is idempotent.
- **BuildPollGroups refactored to for-loop**: The original LINQ `.Select()` chain was replaced with an explicit `for` loop to enable `continue` for zero-OID groups. This is cleaner than nullable return types or post-filter.
- **`<remarks>` placement**: Inserted after existing `<summary>` paragraphs within the `AddSnmpPipeline` doc comment, not as a separate doc block.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 34-02 (TenantVectorRegistry validation) can proceed immediately
- DeviceRegistry.TryGetByIpPort already available for TEN-07 IP+Port existence checks in TenantVectorRegistry
- IOidMapService.ContainsMetricName available for TEN-05 MetricName validation

---
*Phase: 34-communitystring-validation-and-metricpolljob-cleanup*
*Completed: 2026-03-14*
