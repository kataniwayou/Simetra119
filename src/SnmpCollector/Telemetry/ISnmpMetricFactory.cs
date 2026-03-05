namespace SnmpCollector.Telemetry;

/// <summary>
/// Provides access to SNMP business metric instruments (gauge and info).
/// Implementations cache instruments to avoid duplicate registrations.
/// </summary>
public interface ISnmpMetricFactory
{
    /// <summary>
    /// Records a numeric SNMP value on the <c>snmp_gauge</c> instrument.
    /// Used for Integer32, Gauge32, and TimeTicks OID types.
    /// </summary>
    void RecordGauge(string metricName, string oid, string agent, string source, double value);

    /// <summary>
    /// Records a string SNMP value on the <c>snmp_info</c> instrument as 1.0 with a value label.
    /// Used for OctetString, IPAddress, and ObjectIdentifier OID types.
    /// </summary>
    void RecordInfo(string metricName, string oid, string agent, string source, string value);

    /// <summary>
    /// Records a computed counter delta on the <c>snmp_counter</c> instrument.
    /// Used by the counter delta engine after computing the difference from the previous value.
    /// The delta parameter must be non-negative (counters never decrease in Prometheus).
    /// </summary>
    void RecordCounter(string metricName, string oid, string agent, string source, double delta);
}
