---
phase: 30-oid-map-integrity
plan: 01
subsystem: pipeline
tags: [frozen-dictionary, reverse-index, oid-map, hot-reload]

# Dependency graph
requires:
  - phase: 11-oid-map-design-and-obp-population
    provides: OidMapService with FrozenDictionary forward map and MergeWithHeartbeatSeed
provides:
  - IOidMapService.ResolveToOid(string metricName) for metric-name-to-OID reverse lookup
  - Volatile FrozenDictionary reverse map rebuilt on every hot-reload
affects:
  - 31 (device human-name resolution needs ResolveToOid)
  - 30-02 (OID map validation may use reverse map)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Reverse FrozenDictionary built from forward map post-heartbeat-seed"
    - "StringComparer.Ordinal for reverse map keys (metric names are case-sensitive)"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Pipeline/IOidMapService.cs
    - src/SnmpCollector/Pipeline/OidMapService.cs
    - tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
    - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs

key-decisions:
  - "StringComparer.Ordinal for reverse map (metric names are case-sensitive snake_case)"
  - "No locking for reverse map writes -- matches existing volatile swap pattern for _map and _metricNames"

patterns-established:
  - "BuildReverseMap called after MergeWithHeartbeatSeed in both constructor and UpdateMap"

# Metrics
duration: 2min
completed: 2026-03-13
---

# Phase 30 Plan 01: Reverse Index & ResolveToOid Summary

**Volatile FrozenDictionary reverse index (metric name -> OID) on OidMapService with ResolveToOid interface method**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-13T06:42:14Z
- **Completed:** 2026-03-13T06:44:08Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Added ResolveToOid(string metricName) to IOidMapService interface
- Implemented volatile FrozenDictionary reverse map rebuilt in constructor and UpdateMap
- Heartbeat seed included in reverse index (resolves "Heartbeat" to HeartbeatOid)
- 5 new unit tests covering known/unknown names, heartbeat seed, and hot-reload scenarios

## Task Commits

Each task was committed atomically:

1. **Task 1: Add reverse index and ResolveToOid to OidMapService + IOidMapService** - `8e7555d` (feat)
2. **Task 2: Add unit tests for ResolveToOid and reverse index** - `ee8a5ee` (test)

## Files Created/Modified
- `src/SnmpCollector/Pipeline/IOidMapService.cs` - Added ResolveToOid method declaration
- `src/SnmpCollector/Pipeline/OidMapService.cs` - Added _reverseMap field, BuildReverseMap method, ResolveToOid implementation
- `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs` - 5 new ResolveToOid tests
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` - StubOidMapService updated for new interface method

## Decisions Made
- Used StringComparer.Ordinal for reverse map keys (metric names are case-sensitive snake_case in this codebase)
- No locking introduced -- matches existing volatile swap pattern for _map and _metricNames

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed StubOidMapService in OidResolutionBehaviorTests**
- **Found during:** Task 2 (test compilation)
- **Issue:** StubOidMapService did not implement newly added IOidMapService.ResolveToOid, causing CS0535
- **Fix:** Added `public string? ResolveToOid(string metricName) => null;` to the stub
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
- **Verification:** All tests compile and pass
- **Committed in:** ee8a5ee (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Minimal -- standard interface implementation update required by the new method.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- ResolveToOid is ready for Phase 31 device human-name resolution
- Reverse map includes heartbeat seed, so "Heartbeat" resolves correctly
- All 11 OidMapServiceTests pass (6 existing + 5 new)

---
*Phase: 30-oid-map-integrity*
*Completed: 2026-03-13*
