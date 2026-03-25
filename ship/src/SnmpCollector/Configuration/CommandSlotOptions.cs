namespace SnmpCollector.Configuration;

/// <summary>
/// Configuration for a single command slot within a tenant.
/// Identifies a specific SNMP SET command target for future execution.
/// </summary>
public sealed class CommandSlotOptions
{
    /// <summary>
    /// IP address of the target device.
    /// </summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// SNMP port of the target device. Defaults to 161.
    /// </summary>
    public int Port { get; set; } = 161;

    /// <summary>
    /// Command name that maps to an OID via the command map.
    /// Resolved at execution time, not at load time.
    /// </summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>
    /// The value to SET, always stored as a string (e.g. "1", "10.0.0.1", "hostname").
    /// Required -- empty or null means the command entry is invalid.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Tells the system how to interpret Value for SNMP SET execution.
    /// Must be one of: "Integer32", "IpAddress", "OctetString".
    /// Validated at load time; invalid ValueType causes the entry to be skipped.
    /// </summary>
    public string ValueType { get; set; } = string.Empty;
}
