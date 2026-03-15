# Phase 38: DeviceWatcherService Validation - Context

**Gathered:** 2026-03-15
**Status:** Ready for planning

<domain>
## Phase Boundary

Validate combined metric config in `DeviceWatcherService.ValidateAndBuildDevicesAsync` (BuildPollGroups). Parse Aggregator, enforce co-presence, check name collisions, enforce minimum metrics count, detect duplicate names, and build `CombinedMetricDefinition` records on `MetricPollInfo`. Follows the watcher-validates-registry-stores pattern from v1.7.

</domain>

<decisions>
## Implementation Decisions

### Aggregator Parsing
- `Enum.TryParse<AggregationKind>(value, ignoreCase: true)` — maps lowercase JSON to PascalCase enum
- Invalid Aggregator value = Error log, skip combined metric definition (poll group still loads for individual metrics)

### Co-Presence Validation
- `AggregatedMetricName` and `Aggregator` must both be present or both absent
- Partial (one set, one null/empty) = Error log, skip combined metric definition

### Minimum Metrics Count
- Combined metric requires at least 2 entries in MetricNames[] — aggregating 1 value is meaningless
- MetricNames.Count < 2 with combined metric config = Error log, skip combined metric definition

### Duplicate AggregatedMetricName
- Two poll groups on the same device with the same AggregatedMetricName = Error log, skip the second one
- First-wins semantics — first poll group's combined metric loads, duplicate is rejected

### OID Map Name Collision
- AggregatedMetricName that matches an existing OID map entry = Error log, skip combined metric
- Real metric takes priority — combined metric with colliding name is rejected
- This is stricter than original CM-11 (changed from Warning to Error per user decision)

### Per-Entry Skip Pattern
- Invalid combined metric = skip that definition only; poll group still loads for individual OID polling
- Consistent with v1.7 per-entry skip pattern (never reject entire reload)

### Claude's Discretion
- Whether to track seen AggregatedMetricNames per-device in a HashSet or per-reload
- Exact placement of validation within BuildPollGroups method
- Test organization and naming

</decisions>

<specifics>
## Specific Ideas

No specific requirements beyond the confirmed validation rules.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 38-devicewatcherservice-validation*
*Context gathered: 2026-03-15*
