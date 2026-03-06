using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

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
    private readonly string _siteName;

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

    public SnmpMetricFactory(IMeterFactory meterFactory, IOptions<SiteOptions> siteOptions)
    {
        _meter = meterFactory.Create(TelemetryConstants.LeaderMeterName);
        _siteName = siteOptions.Value.Name;
    }

    /// <inheritdoc />
    public void RecordGauge(string metricName, string oid, string agent, string source, string snmpType, double value)
    {
        var gauge = GetOrCreateGauge("snmp_gauge");
        gauge.Record(value, new TagList
        {
            { "site_name", _siteName },
            { "metric_name", metricName },
            { "oid", oid },
            { "agent", agent },
            { "source", source },
            { "snmp_type", snmpType }
        });
    }

    /// <inheritdoc />
    public void RecordInfo(string metricName, string oid, string agent, string source, string snmpType, string value)
    {
        var truncated = value.Length > MaxInfoValueLength
            ? string.Concat(value.AsSpan(0, 125), "...")
            : value;

        var gauge = GetOrCreateGauge("snmp_info");
        gauge.Record(1.0, new TagList
        {
            { "site_name", _siteName },
            { "metric_name", metricName },
            { "oid", oid },
            { "agent", agent },
            { "source", source },
            { "snmp_type", snmpType },
            { "value", truncated }
        });
    }

    private Gauge<double> GetOrCreateGauge(string name)
        => (Gauge<double>)_instruments.GetOrAdd(name, n => _meter.CreateGauge<double>(n));

    public void Dispose() => _meter.Dispose();
}
