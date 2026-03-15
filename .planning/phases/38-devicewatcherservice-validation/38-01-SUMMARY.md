---
phase: 38-devicewatcherservice-validation
plan: 01
subsystem: pipeline
tags: [csharp, snmp, aggregation, validation, nsubstitute, xunit, buildpollgroups]

# Dependency graph
requires:
  - phase: 37-config-and-runtime-models
    provides: CombinedMetricDefinition, AggregationKind, PollOptions.AggregatedMetricName/Aggregator, MetricPollInfo.AggregatedMetrics init property
provides:
  - Combined metric validation block in BuildPollGroups (5 rules: co-presence, Aggregator enum parse, min 2 resolved OIDs, per-device duplicate name, OID map collision)
  - CombinedMetricDefinition populated on MetricPollInfo.AggregatedMetrics for valid configs
  - seenAggregatedNames HashSet for per-device duplicate detection
  - 10 unit tests covering all validation rules and happy path in DeviceWatcherValidationTests
affects:
  - phase-39-oid-resolution-bypass-guard
  - phase-40-metricpolljob-aggregate-dispatch

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Validate-then-build: run all validation rules in else-if chain; on any failure, Error log + null combinedMetric; always execute result.Add (never continue)"
    - "Per-device HashSet scoped to BuildPollGroups call for duplicate name tracking"
    - "Structured Error log with 4 named params: AggregatedMetricName, DeviceName, PollGroupIndex, Reason"
    - "resolvedOids.Count (not MetricNames.Count) for minimum-2 check — prevents phantom OID combined metrics"

key-files:
  created:
    - tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs (304 lines added)
  modified:
    - src/SnmpCollector/Services/DeviceWatcherService.cs (56 lines added to BuildPollGroups)

key-decisions:
  - "Minimum-2 check uses resolvedOids.Count not MetricNames.Count — prevents CombinedMetricDefinition with fewer SourceOids than configured names"
  - "OID map collision = Error + skip combined metric (not Warning) — real metric takes priority"
  - "Invalid combined metric skips definition only; poll group always loads for individual OID polling (no continue after validation failure)"
  - "seenAggregatedNames HashSet uses StringComparer.Ordinal — metric names are case-sensitive identifiers"

patterns-established:
  - "Rule: AggregatedMetricName + Aggregator are co-required (both or neither); partial config = Error"
  - "Rule order: co-presence -> enum parse -> min OIDs -> duplicate name -> OID map collision -> build"
  - "MetricPollInfo.AggregatedMetrics set via object initializer syntax (init-only property)"

# Metrics
duration: 3min
completed: 2026-03-15
---

# Phase 38 Plan 01: DeviceWatcherService Validation Summary

**Five-rule combined metric validation in BuildPollGroups: Aggregator enum parse, co-presence check, minimum 2 resolved OIDs, per-device duplicate name, OID map collision — 10 new unit tests, 312 total passing**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-15T09:15:03Z
- **Completed:** 2026-03-15T09:17:55Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Extended `BuildPollGroups` with a 5-rule combined metric validation block that rejects invalid config with structured Error logs (DeviceName, PollGroupIndex, AggregatedMetricName, Reason) while always preserving individual OID polling
- Valid combined metric config builds `CombinedMetricDefinition` and populates `MetricPollInfo.AggregatedMetrics` via the init property established in Phase 37
- Added 10 unit tests covering all validation rules and the happy path, using the established NSubstitute logger assertion pattern from existing `DeviceWatcherValidationTests`

## Task Commits

Each task was committed atomically:

1. **Task 1: Add combined metric validation to BuildPollGroups** - `5f6120d` (feat)
2. **Task 2: Add unit tests for combined metric validation rules** - `886cda4` (test)

**Plan metadata:** _(pending)_

## Files Created/Modified

- `src/SnmpCollector/Services/DeviceWatcherService.cs` - BuildPollGroups extended with seenAggregatedNames HashSet and 5-rule validation block before result.Add
- `tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` - 10 new combined metric tests appended (sections 13-22), SingleDeviceWithPoll helper added

## Decisions Made

- **resolvedOids.Count vs MetricNames.Count:** Used `resolvedOids.Count < 2` for the minimum-2 check. MetricNames may include names that fail OID resolution; using resolved count prevents a CombinedMetricDefinition with fewer SourceOids than the user configured — a silent semantic error at poll time.
- **OID map collision severity:** Error (not Warning) per CONTEXT.md — real OID map entries take unconditional priority over synthetic aggregated names.
- **Never `continue` after validation failure:** The `result.Add` always executes so invalid combined metric config does not drop the poll group's individual OID polling.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Style] Replaced Assert.Equal(0/1, ...) with Assert.Empty/Assert.Single**
- **Found during:** Task 2 (running tests)
- **Issue:** xUnit analyzer warnings (xUnit2013) on `Assert.Equal(0, collection.Count)` and `Assert.Equal(1, collection.Count)` — project treats analyzer warnings as policy
- **Fix:** Replaced all collection-size `Assert.Equal` calls with `Assert.Empty` and `Assert.Single` per xUnit idiom
- **Files modified:** tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs
- **Verification:** Zero warnings on rebuild; all 312 tests pass
- **Committed in:** `886cda4` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (style)
**Impact on plan:** Trivial style fix to eliminate analyzer warnings. No scope creep.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 39 (OID resolution bypass guard): `MetricPollInfo.AggregatedMetrics` is now populated for all valid combined metric configs. Phase 39 can read this to identify synthetic dispatch candidates.
- Phase 40 (MetricPollJob aggregate dispatch): `CombinedMetricDefinition` records on `MetricPollInfo` are fully validated and ready for computation.
- Pre-phase decision from STATE.md still applies: Phase 39 must add `SnmpSource.Synthetic` to the enum and a bypass guard in `OidResolutionBehavior` before Phase 40 can dispatch synthetic metrics.

---
*Phase: 38-devicewatcherservice-validation*
*Completed: 2026-03-15*
