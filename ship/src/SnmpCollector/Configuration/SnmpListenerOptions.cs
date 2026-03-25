using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// SNMP trap listener configuration. Bound from "SnmpListener" section.
/// </summary>
public sealed class SnmpListenerOptions
{
    public const string SectionName = "SnmpListener";

    /// <summary>
    /// IP address to bind the SNMP listener to.
    /// </summary>
    [Required]
    public required string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// UDP port for SNMP trap reception.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 162;

    /// <summary>
    /// SNMP protocol version. Only "v2c" is supported.
    /// </summary>
    [Required]
    [RegularExpression("^v2c$", ErrorMessage = "Only v2c is supported")]
    public required string Version { get; set; } = "v2c";
}
