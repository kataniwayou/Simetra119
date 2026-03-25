namespace SnmpCollector.Configuration;

/// <summary>
/// Wrapper for the Devices configuration array. Bound from "Devices" section.
/// The JSON "Devices" key is a top-level array, so binding uses a custom Configure
/// delegate to bind the array directly into the Devices list property.
/// An empty list is valid (no devices to poll).
/// </summary>
public sealed class DevicesOptions
{
    public const string SectionName = "Devices";

    /// <summary>
    /// List of monitored device configurations.
    /// An empty list is valid (no devices to poll).
    /// </summary>
    public List<DeviceOptions> Devices { get; set; } = [];
}
