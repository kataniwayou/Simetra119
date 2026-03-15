---
phase: 43-heartbeat-cleanup
plan: 01
subsystem: pipeline
tags: [snmp, pipeline, tenant-vector-registry, fan-out, heartbeat, refactoring]

# Dependency graph
requires:
  - phase: 32-command-map-infrastructure
    provides: TenantVectorRegistry with command-map wiring (base registry code)
  - phase: 29-k8s-deployment-and-e2e-validation
    provides: TenantVectorFanOutBehavior device-registry routing (PIP-02)
provides:
  - TenantVectorRegistry.Reload with no heartbeat injection (purely config-driven tenants)
  - TenantVectorFanOutBehavior with single routing branch (device-registry only)
  - TenantCount reflecting only configured tenants (no +1 inflation)
affects:
  - 44-pipeline-liveness (depends on clean heartbeat path; "Simetra" naturally skipped)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Natural skip pattern: device not in DeviceRegistry -> TryGetDeviceByName returns false -> fan-out silently skipped"
    - "Config-driven-only registry: TenantCount and SlotCount reflect only explicitly configured tenants"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    - src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs
    - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs

key-decisions:
  - "Heartbeat device 'Simetra' not in DeviceRegistry -> TryGetDeviceByName returns false -> fan-out naturally skipped (no bypass needed)"
  - "HeartbeatJobOptions constants retained (still used by HeartbeatJob and CommunityStringHelper)"
  - "using SnmpCollector.Configuration removed from TenantVectorFanOutBehavior (no longer references any Configuration types)"

patterns-established:
  - "Assert.Single() preferred over Assert.Equal(1, .Count) for xUnit2013 compliance"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 43 Plan 01: Heartbeat Cleanup Summary

**Removed synthetic heartbeat tenant from TenantVectorRegistry and heartbeat bypass block from TenantVectorFanOutBehavior; TenantCount now reflects config-driven tenants only**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T15:53:40Z
- **Completed:** 2026-03-15T15:55:40Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Deleted the 15-line heartbeat holder injection block (lines 75-89) and `totalSlots++` from `TenantVectorRegistry.Reload`; `TenantCount = survivingTenantCount` (no `+1`)
- Deleted the 13-line heartbeat bypass `if`-block from `TenantVectorFanOutBehavior.Handle`; converted `else if` to `if` on the device-registry branch (single routing path)
- Deleted 4 section-9 heartbeat tests; fixed 7 count/index assertions in 5 remaining test methods (each drops by 1); all 332 tests pass with zero warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Remove hardcoded heartbeat from TenantVectorRegistry and TenantVectorFanOutBehavior** - `2aa6639` (refactor)
2. **Task 2: Delete heartbeat tests and fix count/index assertions** - `196442b` (refactor)

**Plan metadata:** (docs commit below)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` - Heartbeat injection block deleted; TenantCount fixed
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` - Heartbeat bypass deleted; unused Configuration using removed; `else if` -> `if`
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` - Section 9 deleted (4 tests); 7 assertions adjusted across 5 tests

## Decisions Made

- Removed `using SnmpCollector.Configuration;` from `TenantVectorFanOutBehavior` because after deleting the heartbeat bypass block, no Configuration types remained in that file — this avoids a compiler warning without impacting behavior.
- `HeartbeatJobOptions` constants (`HeartbeatDeviceName`, `HeartbeatOid`, `DefaultIntervalSeconds`) left untouched in `HeartbeatJobOptions.cs` — still used by `HeartbeatJob` and `CommunityStringHelper`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Converted Assert.Equal(1, .Count) to Assert.Single for xUnit2013 compliance**
- **Found during:** Task 2 (running registry tests)
- **Issue:** Two `Assert.Equal(1, registry.Groups.Count)` assertions generated xUnit2013 warnings ("Do not use Assert.Equal() to check for collection size. Use Assert.Single instead.") — these were new warnings introduced by the count adjustments
- **Fix:** Changed both to `var group = Assert.Single(registry.Groups)` which also removes the need for the separate `registry.Groups[0]` access
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
- **Verification:** Zero warnings in test build; all 332 tests pass
- **Committed in:** `196442b` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug/warning fix)
**Impact on plan:** Minor improvement to test quality; no scope creep.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 44 (Pipeline Liveness) can begin: heartbeat special-cases are fully removed; "Simetra" messages now follow the natural path (not in DeviceRegistry -> fan-out skipped)
- `ILivenessVectorService` untouched as required; `HeartbeatJobOptions` constants available for Phase 44 liveness window calculation
- All 332 tests green, build clean, zero warnings

---
*Phase: 43-heartbeat-cleanup*
*Completed: 2026-03-15*
