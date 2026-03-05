using SnmpCollector.Telemetry;

namespace SnmpCollector.Tests.Helpers;

/// <summary>
/// In-memory implementation of <see cref="ISnmpMetricFactory"/> that records all method calls
/// for assertion in unit and integration tests. Thread-safe via lock on list operations is
/// intentionally omitted -- tests are single-threaded.
/// </summary>
public sealed class TestSnmpMetricFactory : ISnmpMetricFactory
{
    public List<(string MetricName, string Oid, string Agent, string Source, double Value)> GaugeRecords { get; } = new();
    public List<(string MetricName, string Oid, string Agent, string Source, string Value)> InfoRecords { get; } = new();

    public void RecordGauge(string metricName, string oid, string agent, string source, double value)
        => GaugeRecords.Add((metricName, oid, agent, source, value));

    public void RecordInfo(string metricName, string oid, string agent, string source, string value)
        => InfoRecords.Add((metricName, oid, agent, source, value));
}
