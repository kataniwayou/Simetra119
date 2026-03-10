using Lextm.SharpSnmpLib;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Volatile wrapper around a MetricSlot reference.
/// WriteValue/ReadSlot encapsulate Volatile.Write/Read so callers never touch the volatile field directly.
/// ReadSlot returns null before any write has occurred (no value yet).
/// </summary>
public sealed class MetricSlotHolder
{
    private MetricSlot? _slot;

    public string Ip { get; }
    public int Port { get; }
    public string MetricName { get; }
    public int IntervalSeconds { get; }

    public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds)
    {
        Ip = ip;
        Port = port;
        MetricName = metricName;
        IntervalSeconds = intervalSeconds;
    }

    /// <summary>
    /// Creates a new MetricSlot from the provided values (timestamped UtcNow)
    /// and publishes it atomically via Volatile.Write.
    /// </summary>
    public void WriteValue(double value, string? stringValue, SnmpType typeCode)
    {
        var newSlot = new MetricSlot(value, stringValue, typeCode, DateTimeOffset.UtcNow);
        Volatile.Write(ref _slot, newSlot);
    }

    /// <summary>
    /// Returns the most recently written MetricSlot, or null if no value has been written yet.
    /// </summary>
    public MetricSlot? ReadSlot() => Volatile.Read(ref _slot);
}
