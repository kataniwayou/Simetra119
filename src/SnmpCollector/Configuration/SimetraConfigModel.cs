namespace SnmpCollector.Configuration;

/// <summary>
/// Unified configuration POCO deserialized from the simetra-config ConfigMap.
/// Contains both OID map entries and device definitions in a single JSON document,
/// enabling atomic reload of the entire configuration via K8s API watch.
/// </summary>
public sealed class SimetraConfigModel
{
    /// <summary>
    /// OID-to-metric-name mapping. Keys are exact OID strings;
    /// values are camelCase metric names (e.g., "1.3.6.1.2.1.25.3.3.1.2" -> "hrProcessorLoad").
    /// </summary>
    public Dictionary<string, string> OidMap { get; set; } = new();

    /// <summary>
    /// List of monitored device configurations.
    /// An empty list is valid (no devices to poll).
    /// </summary>
    public List<DeviceOptions> Devices { get; set; } = new();
}
