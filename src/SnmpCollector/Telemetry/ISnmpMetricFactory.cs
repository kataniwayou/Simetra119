namespace SnmpCollector.Telemetry;

/// <summary>
/// Provides access to SNMP business metric instruments (gauge and info).
/// Implementations cache instruments to avoid duplicate registrations.
/// </summary>
public interface ISnmpMetricFactory
{
    /// <summary>
    /// Records a numeric SNMP value on the <c>snmp_gauge</c> instrument.
    /// Used for Integer32, Gauge32, TimeTicks, Counter32, and Counter64 OID types.
    /// </summary>
    void RecordGauge(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double value);

    /// <summary>
    /// Records a string SNMP value on the <c>snmp_info</c> instrument as 1.0 with a value label.
    /// Used for OctetString, IPAddress, and ObjectIdentifier OID types.
    /// </summary>
    void RecordInfo(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value);

    /// <summary>
    /// Records poll duration on the <c>snmp_gauge_duration</c> histogram.
    /// Same 6 labels as RecordGauge (resolved_name, oid, device_name, ip, source, snmp_type).
    /// </summary>
    void RecordGaugeDuration(string metricName, string oid, string deviceName, string ip, string source, string snmpType, double durationMs);

    /// <summary>
    /// Records poll duration on the <c>snmp_info_duration</c> histogram.
    /// Same 7 labels as RecordInfo (resolved_name, oid, device_name, ip, source, snmp_type, value).
    /// </summary>
    void RecordInfoDuration(string metricName, string oid, string deviceName, string ip, string source, string snmpType, string value, double durationMs);
}
