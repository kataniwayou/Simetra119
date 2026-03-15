---
phase: 37-config-and-runtime-models
plan: 01
subsystem: pipeline
tags: [snmp, aggregation, combined-metrics, csharp, dotnet, records, enums]

# Dependency graph
requires:
  - phase: 32-command-map-infrastructure
    provides: watcher pattern and MetricPollInfo positional record used as extension point
provides:
  - AggregationKind enum (Sum, Subtract, AbsDiff, Mean) in SnmpCollector.Pipeline namespace
  - CombinedMetricDefinition sealed record (MetricName, Kind, SourceOids) in SnmpCollector.Pipeline namespace
  - PollOptions.AggregatedMetricName and PollOptions.Aggregator nullable string config properties
  - MetricPollInfo.AggregatedMetrics init property (IReadOnlyList<CombinedMetricDefinition>, default empty)
  - 13 unit tests covering all new types and backward compatibility
affects:
  - 37-02 (if planned): Phase 38 BuildPollGroups wiring
  - phase-38: DeviceWatcherService.BuildPollGroups reads AggregatedMetricName+Aggregator to build CombinedMetricDefinition and populate AggregatedMetrics
  - phase-39: SnmpPollJob reads AggregatedMetrics to dispatch synthetic metrics
  - phase-40: Aggregation computation uses AggregationKind enum

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Init-only property with default on positional record for backward-compatible extension (AggregatedMetrics = [])"
    - "Sealed record for immutable runtime definitions (CombinedMetricDefinition)"
    - "Case-insensitive Enum.TryParse<AggregationKind> for config string to enum mapping"

key-files:
  created:
    - src/SnmpCollector/Pipeline/AggregationKind.cs
    - src/SnmpCollector/Pipeline/CombinedMetricDefinition.cs
    - tests/SnmpCollector.Tests/Pipeline/CombinedMetricModelTests.cs
  modified:
    - src/SnmpCollector/Configuration/PollOptions.cs
    - src/SnmpCollector/Pipeline/MetricPollInfo.cs

key-decisions:
  - "AggregatedMetrics is an init-only property (not positional param) on MetricPollInfo so all 20+ existing construction sites require zero changes"
  - "No [JsonPropertyName] attributes on PollOptions — PropertyNameCaseInsensitive=true in existing deserializer handles it"
  - "CombinedMetricDefinition is a sealed record for value equality and immutability"

patterns-established:
  - "Init-only property with default [] for backward-compatible extension of positional records"
  - "Config string -> enum via Enum.TryParse ignoreCase:true (Aggregator -> AggregationKind)"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 37 Plan 01: Config and Runtime Models Summary

**AggregationKind enum, CombinedMetricDefinition sealed record, PollOptions aggregation config fields, and MetricPollInfo.AggregatedMetrics init property — purely additive types for v1.8 combined metrics**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T08:28:43Z
- **Completed:** 2026-03-15T08:30:33Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Created `AggregationKind` enum with Sum, Subtract, AbsDiff, Mean members; case-insensitive `Enum.TryParse` confirmed working
- Created `CombinedMetricDefinition` sealed record with positional parameters (MetricName, Kind, SourceOids); structural equality verified
- Extended `PollOptions` with `AggregatedMetricName` and `Aggregator` nullable string properties; JSON deserialization with `PropertyNameCaseInsensitive=true` confirmed working
- Extended `MetricPollInfo` with `AggregatedMetrics` as init-only property (default `[]`); all 286 pre-existing tests continue to pass unchanged
- Added 13 unit tests covering all four truths from the plan's must_haves

## Task Commits

Each task was committed atomically:

1. **Task 1: Add AggregationKind enum, CombinedMetricDefinition record, extend PollOptions and MetricPollInfo** - `81ec507` (feat)
2. **Task 2: Add unit tests for combined metric types and backward compatibility** - `683a89d` (test)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `src/SnmpCollector/Pipeline/AggregationKind.cs` - New enum: Sum, Subtract, AbsDiff, Mean
- `src/SnmpCollector/Pipeline/CombinedMetricDefinition.cs` - New sealed record: MetricName, Kind, SourceOids
- `src/SnmpCollector/Configuration/PollOptions.cs` - Added AggregatedMetricName and Aggregator nullable string properties
- `src/SnmpCollector/Pipeline/MetricPollInfo.cs` - Added AggregatedMetrics init-only property with default empty list
- `tests/SnmpCollector.Tests/Pipeline/CombinedMetricModelTests.cs` - 13 unit tests for all new types and backward compatibility

## Decisions Made

- **Init-only property, not positional parameter:** `AggregatedMetrics` is an init-only property on `MetricPollInfo` so all existing `new MetricPollInfo(index, oids, interval, timeout)` call sites compile without changes. Making it positional would have required modifying 20+ construction sites.
- **No `[JsonPropertyName]` on PollOptions:** The existing deserializer already uses `PropertyNameCaseInsensitive = true`, so adding attributes would be redundant noise.
- **`sealed record` for CombinedMetricDefinition:** Immutability and structural equality are correct semantics for a runtime definition object.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Applied xUnit analyzer best practices (Assert.Empty/Assert.Single)**

- **Found during:** Task 2 (unit test creation) — three xUnit2013 analyzer warnings surfaced during build
- **Issue:** Used `Assert.Equal(0, collection.Count)` and `Assert.Equal(1, collection.Count)` instead of `Assert.Empty` / `Assert.Single`
- **Fix:** Replaced with `Assert.Empty(info.AggregatedMetrics)` and `var single = Assert.Single(info.AggregatedMetrics)` per xUnit conventions
- **Files modified:** tests/SnmpCollector.Tests/Pipeline/CombinedMetricModelTests.cs
- **Verification:** No warnings in final build; all 13 tests pass
- **Committed in:** `683a89d` (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 missing critical — code quality)
**Impact on plan:** Minor style improvement. No scope creep. Tests are semantically identical.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All four types Phase 38 needs are available: `AggregationKind`, `CombinedMetricDefinition`, `PollOptions.AggregatedMetricName`, `PollOptions.Aggregator`, `MetricPollInfo.AggregatedMetrics`
- Phase 38 (DeviceWatcherService.BuildPollGroups) can proceed immediately
- Key link pattern confirmed: `Enum.TryParse<AggregationKind>(poll.Aggregator, ignoreCase: true, out var kind)` works for all four values
- No blockers.

---
*Phase: 37-config-and-runtime-models*
*Completed: 2026-03-15*
