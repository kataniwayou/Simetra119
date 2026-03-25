using System.Collections.Immutable;
using Lextm.SharpSnmpLib;
using SnmpCollector.Configuration;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Volatile wrapper around an ImmutableArray of MetricSlot samples (cyclic time series).
/// WriteValue/ReadSlot encapsulate atomic field swaps so callers never touch the field directly.
/// ReadSlot returns null before any real write has occurred.
/// ReadSeries returns the full ImmutableArray of samples (empty before first write).
/// TypeCode and Source are promoted to holder-level mutable properties.
/// IsReady determines whether the holder should participate in threshold evaluation and
/// staleness detection. A fresh holder is not ready until ReadinessGrace has elapsed,
/// unless data has already been written (CopyFrom or WriteValue), in which case it is
/// immediately ready.
/// </summary>
public sealed class MetricSlotHolder
{
    /// <summary>
    /// Boxed ImmutableArray wrapper to enable Volatile.Read/Write (requires reference type).
    /// </summary>
    private sealed class SeriesBox
    {
        public static readonly SeriesBox Empty = new(ImmutableArray<MetricSlot>.Empty);

        public ImmutableArray<MetricSlot> Series { get; }

        public SeriesBox(ImmutableArray<MetricSlot> series) => Series = series;
    }

    private SeriesBox _box = SeriesBox.Empty;

    public string Ip { get; }
    public int Port { get; }
    public string MetricName { get; }
    public int IntervalSeconds { get; }
    public string Role { get; }
    public int TimeSeriesSize { get; }
    public double GraceMultiplier { get; }
    public SnmpType TypeCode { get; private set; }
    public SnmpSource Source { get; private set; }
    public ThresholdOptions? Threshold { get; }

    /// <summary>
    /// The UTC time at which this holder was constructed. Used to compute readiness.
    /// </summary>
    public DateTimeOffset ConstructedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The duration a fresh holder must wait before being considered ready for evaluation.
    /// Computed as TimeSeriesSize * IntervalSeconds * GraceMultiplier.
    /// </summary>
    public TimeSpan ReadinessGrace =>
        TimeSpan.FromSeconds(TimeSeriesSize * IntervalSeconds * GraceMultiplier);

    /// <summary>
    /// True when the holder should participate in threshold evaluation and staleness detection.
    /// Returns true immediately when data is present (WriteValue or CopyFrom already called).
    /// Returns true after ReadinessGrace has elapsed since construction.
    /// </summary>
    public bool IsReady =>
        ReadSeries().Length > 0 || DateTimeOffset.UtcNow - ConstructedAt > ReadinessGrace;

    public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds,
        string role, int timeSeriesSize = 1, double graceMultiplier = 2.0,
        ThresholdOptions? threshold = null)
    {
        Ip = ip;
        Port = port;
        MetricName = metricName;
        IntervalSeconds = intervalSeconds;
        Role = role;
        TimeSeriesSize = timeSeriesSize;
        GraceMultiplier = graceMultiplier;
        Threshold = threshold;
    }

    /// <summary>
    /// Creates a new MetricSlot from the provided values (timestamped UtcNow)
    /// and appends it to the cyclic series via Volatile.Write.
    /// TypeCode and Source are set on the holder.
    /// </summary>
    public void WriteValue(double value, string? stringValue, SnmpType typeCode, SnmpSource source)
    {
        TypeCode = typeCode;
        Source = source;

        var sample = new MetricSlot(value, stringValue, DateTimeOffset.UtcNow);
        var current = Volatile.Read(ref _box);
        var series = current.Series;
        var updated = series.Length >= TimeSeriesSize
            ? series.RemoveAt(0).Add(sample)
            : series.Add(sample);
        Volatile.Write(ref _box, new SeriesBox(updated));
    }

    /// <summary>
    /// Returns the most recently written MetricSlot, or null if no real write has occurred.
    /// </summary>
    public MetricSlot? ReadSlot()
    {
        var s = Volatile.Read(ref _box).Series;
        return s.Length > 0 ? s[^1] : null;
    }

    /// <summary>
    /// Returns the full time series as an ImmutableArray snapshot.
    /// </summary>
    public ImmutableArray<MetricSlot> ReadSeries() => Volatile.Read(ref _box).Series;

    /// <summary>
    /// Bulk-loads series data from an old holder during registry reload.
    /// Copies the full series (truncated to this holder's TimeSeriesSize) plus TypeCode and Source.
    /// </summary>
    public void CopyFrom(MetricSlotHolder old)
    {
        TypeCode = old.TypeCode;
        Source = old.Source;
        var oldSeries = old.ReadSeries();
        // Take last TimeSeriesSize samples if old series is larger
        var trimmed = oldSeries.Length > TimeSeriesSize
            ? oldSeries.RemoveRange(0, oldSeries.Length - TimeSeriesSize)
            : oldSeries;
        Volatile.Write(ref _box, new SeriesBox(trimmed));
    }
}
