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
    void RecordGauge(string metricName, string oid, string agent, string source, string snmpType, double value);

    /// <summary>
    /// Records a string SNMP value on the <c>snmp_info</c> instrument as 1.0 with a value label.
    /// Used for OctetString, IPAddress, and ObjectIdentifier OID types.
    /// </summary>
    void RecordInfo(string metricName, string oid, string agent, string source, string snmpType, string value);
}
