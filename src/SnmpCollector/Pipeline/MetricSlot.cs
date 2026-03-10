namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable value cell for a single metric observation.
/// Swapped atomically via Volatile.Write in MetricSlotHolder.
/// </summary>
public sealed record MetricSlot(double Value, string? StringValue, DateTimeOffset UpdatedAt);
