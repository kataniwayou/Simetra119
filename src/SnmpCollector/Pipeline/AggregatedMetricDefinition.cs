namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable definition of one aggregate metric to compute at poll time.
/// Produced by DeviceWatcherService.BuildPollGroups from PollOptions when
/// AggregatedMetricName and Aggregator are both non-empty.
/// </summary>
/// <param name="MetricName">Name of the aggregate metric to emit.</param>
/// <param name="Kind">Aggregation function to apply to SourceOids values.</param>
/// <param name="SourceOids">Resolved OID strings whose values are aggregated.</param>
public sealed record AggregatedMetricDefinition(
    string MetricName,
    AggregationKind Kind,
    IReadOnlyList<string> SourceOids);
