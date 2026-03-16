using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Creates and caches SNMP business metric instruments using a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Instruments are created lazily on first use via GetOrAdd, guaranteeing single registration.
/// Instruments are registered on the leader-gated meter (<see cref="TelemetryConstants.LeaderMeterName"/>)
/// so that <c>MetricRoleGatedExporter</c> can suppress them on follower instances, ensuring
/// snmp_gauge and snmp_info are exported only by the leader pod.
/// </summary>
public sealed class SnmpMetricFactory : ISnmpMetricFactory, IDisposable
{
    private readonly Meter _meter;

    /// <summary>
    /// Thread-safe cache mapping instrument name to the instrument instance.
    /// Values are typed as <c>object</c> because <see cref="Gauge{T}"/> shares no common base with other instrument types.
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _instruments = new();

    /// <summary>
    /// Maximum length of the string value label on <c>snmp_info</c>.
    /// Values exceeding this are truncated and suffixed with "..." to stay within OTel label cardinality bounds.
    /// </summary>
    private const int MaxInfoValueLength = 128;

    public SnmpMetricFactory(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(TelemetryConstants.LeaderMeterName);
    }

    /// <inheritdoc />
    public void RecordGauge(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double value)
    {
        var gauge = GetOrCreateGauge("snmp_gauge");
        gauge.Record(value, new TagList
        {
            { "resolved_name", metricName },
            { "oid", oid },
            { "device_name", deviceName },
            { "ip", ip },
            { "source", source },
            { "snmp_type", snmpType }
        });
    }

    /// <inheritdoc />
    public void RecordInfo(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value)
    {
        var truncated = value.Length > MaxInfoValueLength
            ? string.Concat(value.AsSpan(0, 125), "...")
            : value;

        var gauge = GetOrCreateGauge("snmp_info");
        gauge.Record(1.0, new TagList
        {
            { "resolved_name", metricName },
            { "oid", oid },
            { "device_name", deviceName },
            { "ip", ip },
            { "source", source },
            { "snmp_type", snmpType },
            { "value", truncated }
        });
    }

    /// <inheritdoc />
    public void RecordGaugeDuration(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double durationMs)
    {
        var histogram = GetOrCreateHistogram("snmp_gauge_duration");
        histogram.Record(durationMs, new TagList
        {
            { "resolved_name", metricName },
            { "oid", oid },
            { "device_name", deviceName },
            { "ip", ip },
            { "source", source },
            { "snmp_type", snmpType }
        });
    }

    /// <inheritdoc />
    public void RecordInfoDuration(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value, double durationMs)
    {
        var truncated = value.Length > MaxInfoValueLength
            ? string.Concat(value.AsSpan(0, 125), "...")
            : value;

        var histogram = GetOrCreateHistogram("snmp_info_duration");
        histogram.Record(durationMs, new TagList
        {
            { "resolved_name", metricName },
            { "oid", oid },
            { "device_name", deviceName },
            { "ip", ip },
            { "source", source },
            { "snmp_type", snmpType },
            { "value", truncated }
        });
    }

    private Gauge<double> GetOrCreateGauge(string name)
        => (Gauge<double>)_instruments.GetOrAdd(name, n => _meter.CreateGauge<double>(n));

    private Histogram<double> GetOrCreateHistogram(string name)
        => (Histogram<double>)_instruments.GetOrAdd(name, n => _meter.CreateHistogram<double>(n, unit: "ms"));

    public void Dispose() => _meter.Dispose();
}
