namespace SnmpCollector.Telemetry;

public static class TelemetryConstants
{
    /// <summary>
    /// Pipeline metrics meter -- exported by ALL instances (pipeline + runtime health).
    /// Used by PipelineMetricService for snmp.event.*, snmp.poll.*, snmp.trap.* counters.
    /// </summary>
    public const string MeterName = "SnmpCollector";

    /// <summary>
    /// Business metrics meter -- exported ONLY by the leader instance.
    /// Used by SnmpMetricFactory for snmp_gauge and snmp_info instruments.
    /// </summary>
    public const string LeaderMeterName = "SnmpCollector.Leader";
}
