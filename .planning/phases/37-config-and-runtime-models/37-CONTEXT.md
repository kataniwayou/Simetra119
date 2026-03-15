# Phase 37: Config and Runtime Models - Context

**Gathered:** 2026-03-15
**Status:** Ready for planning

<domain>
## Phase Boundary

Add new C# types for aggregate metrics: `AggregateMetricName` + `Aggregator` on PollOptions, `AggregationKind` enum, `CombinedMetricDefinition` runtime record, extend MetricPollInfo with CombinedMetrics collection. Purely additive — no behavior changes, no validation logic.

</domain>

<decisions>
## Implementation Decisions

### Config JSON Shape
- PollOptions gains two optional string properties: `AggregateMetricName` and `Aggregator`
- Both null/empty = disabled (current behavior, no regression)
- Aggregator values: `"sum"`, `"subtract"`, `"absDiff"`, `"mean"` (lowercase in JSON)
- Config example:
  ```json
  {
    "IntervalSeconds": 10,
    "MetricNames": ["npb_port_rx_octets_P1", "npb_port_rx_octets_P2", "npb_port_rx_octets_P3"],
    "AggregateMetricName": "npb_total_rx_octets",
    "Aggregator": "sum"
  }
  ```

### Runtime Types
- `AggregationKind` enum: `Sum`, `Subtract`, `AbsDiff`, `Mean` (PascalCase C# convention)
- `CombinedMetricDefinition` runtime record: MetricName (string), Kind (AggregationKind), SourceOids (IReadOnlyList<string>)
- `MetricPollInfo` gains `CombinedMetrics` (IReadOnlyList<CombinedMetricDefinition>) with default empty list — backward-compatible

### Naming Convention
- Config property: `AggregateMetricName` + `Aggregator` (the naming pair)
- Runtime record: `CombinedMetricDefinition` (describes the aggregate to compute at poll time)
- Enum: `AggregationKind` (the type of aggregation)

### Claude's Discretion
- Whether CombinedMetricDefinition is a record or sealed class
- Exact property types and nullability on PollOptions
- Whether to add a CombinedMetricOptions wrapper class or keep properties flat on PollOptions

</decisions>

<specifics>
## Specific Ideas

No specific requirements beyond the confirmed JSON shape and type names.

</specifics>

<deferred>
## Deferred Ideas

- Ratio aggregation — excluded from v1.8, may exist as future AggregationKind member
- Cross-poll-group aggregation — out of scope

</deferred>

---

*Phase: 37-config-and-runtime-models*
*Context gathered: 2026-03-15*
