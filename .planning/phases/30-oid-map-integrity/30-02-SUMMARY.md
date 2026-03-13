---
phase: 30-oid-map-integrity
plan: 02
subsystem: config-validation
tags: [json-document, oid-map, duplicate-detection, validation]

# Dependency graph
requires:
  - phase: 30-oid-map-integrity-01
    provides: "FrozenDictionary OidMapService with MergeWithHeartbeatSeed"
provides:
  - "JsonDocument-based duplicate OID key and metric name validation in OidMapWatcherService"
  - "Skip-both policy for ambiguous entries before UpdateMap"
  - "9 unit tests for ValidateAndParseOidMap"
affects: [30-oid-map-integrity-03, 31-device-human-name-resolution]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "JsonDocument property enumeration for duplicate detection before deserialization"
    - "3-pass validation: enumerate+detect OID dupes, detect name dupes, filter clean entries"
    - "internal static method pattern for testable validation logic"

key-files:
  created:
    - "tests/SnmpCollector.Tests/Services/OidMapWatcherValidationTests.cs"
  modified:
    - "src/SnmpCollector/Services/OidMapWatcherService.cs"

key-decisions:
  - "Removed JsonSerializer.Deserialize entirely -- JsonDocument.Parse replaces it"
  - "OrdinalIgnoreCase for OID key comparison matches FrozenDictionary runtime semantics"
  - "Ordinal (case-sensitive) for metric name comparison"
  - "Skip-both policy: neither occurrence of a duplicate survives"

patterns-established:
  - "Validation in watcher service, not in domain service"
  - "internal static method + InternalsVisibleTo for direct unit testing"

# Metrics
duration: 2min
completed: 2026-03-13
---

# Phase 30 Plan 02: Duplicate OID/Name Validation Summary

**JsonDocument-based 3-pass duplicate detection in OidMapWatcherService -- skip-both policy prevents ambiguous OID keys and metric names from reaching OidMapService**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-13T06:43:15Z
- **Completed:** 2026-03-13T06:45:20Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Replaced JsonSerializer.Deserialize with JsonDocument.Parse for explicit property enumeration
- 3-pass validation: detect duplicate OID keys (OrdinalIgnoreCase), detect duplicate metric names (Ordinal), build clean dictionary excluding both occurrences of any duplicate
- Structured warning logs per duplicate, ERROR log on empty result after validation
- 9 unit tests covering all validation scenarios pass (224 total tests pass)

## Task Commits

Each task was committed atomically:

1. **Task 1: Replace JSON deserialization with JsonDocument validation** - `9bfa83a` (feat)
2. **Task 2: Add unit tests for OID map validation logic** - `261edbf` (test)

## Files Created/Modified
- `src/SnmpCollector/Services/OidMapWatcherService.cs` - Added ValidateAndParseOidMap with 3-pass duplicate detection, removed JsonSerializer.Deserialize
- `tests/SnmpCollector.Tests/Services/OidMapWatcherValidationTests.cs` - 9 unit tests for validation logic

## Decisions Made
- Removed JsonOptions static field entirely (only consumer was the replaced Deserialize call)
- Used NullLogger in tests (option a) rather than mock logger -- dictionary contents verify correctness

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Duplicate validation complete, ready for Phase 30 Plan 03 (if exists) or Phase 31
- OidMapWatcherService now prevents phantom diff log entries from duplicate keys reaching UpdateMap

---
*Phase: 30-oid-map-integrity*
*Completed: 2026-03-13*
