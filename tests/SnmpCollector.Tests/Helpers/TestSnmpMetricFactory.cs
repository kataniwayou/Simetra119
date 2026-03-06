using SnmpCollector.Telemetry;

namespace SnmpCollector.Tests.Helpers;

/// <summary>
/// In-memory implementation of <see cref="ISnmpMetricFactory"/> that records all method calls
/// for assertion in unit and integration tests. Thread-safe via lock on list operations is
/// intentionally omitted -- tests are single-threaded.
/// </summary>
public sealed class TestSnmpMetricFactory : ISnmpMetricFactory
{
    public List<(string MetricName, string Oid, string DeviceName, string Ip, string Source, string SnmpType, double Value)> GaugeRecords { get; } = new();
    public List<(string MetricName, string Oid, string DeviceName, string Ip, string Source, string SnmpType, string Value)> InfoRecords { get; } = new();

    public void RecordGauge(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double value)
        => GaugeRecords.Add((metricName, oid, deviceName, ip, source, snmpType, value));

    public void RecordInfo(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value)
        => InfoRecords.Add((metricName, oid, deviceName, ip, source, snmpType, value));
}
