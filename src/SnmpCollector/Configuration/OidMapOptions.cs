namespace SnmpCollector.Configuration;

/// <summary>
/// OID-to-metric-name mapping options. Bound from "OidMap" section.
/// Maps exact OID strings to human-readable metric names (e.g., "1.3.6.1.2.1.25.3.3.1.2" -> "hrProcessorLoad").
/// OIDs not present in the map resolve to resolved_name="Unknown" at runtime.
/// Supports hot-reload via IOptionsMonitor without restart.
/// </summary>
public sealed class OidMapOptions
{
    public const string SectionName = "OidMap";

    /// <summary>
    /// Dictionary mapping OID string to metric name.
    /// Keys are exact OID strings; values are camelCase metric names (e.g., "hrProcessorLoad").
    /// </summary>
    public Dictionary<string, string> Entries { get; set; } = [];
}
