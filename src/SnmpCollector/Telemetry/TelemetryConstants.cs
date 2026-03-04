namespace SnmpCollector.Telemetry;

public static class TelemetryConstants
{
    /// <summary>
    /// Primary meter for all SNMP collector metrics.
    /// Phase 7 will split into leader-gated and instance meters if needed.
    /// </summary>
    public const string MeterName = "SnmpCollector";
}
