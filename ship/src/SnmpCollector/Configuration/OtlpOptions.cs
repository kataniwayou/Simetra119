using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

/// <summary>
/// OpenTelemetry Protocol (OTLP) exporter configuration. Bound from "Otlp" section.
/// </summary>
public sealed class OtlpOptions
{
    public const string SectionName = "Otlp";

    /// <summary>
    /// OTLP collector endpoint URL (e.g., "http://localhost:4317").
    /// </summary>
    [Required]
    public required string Endpoint { get; set; }

    /// <summary>
    /// Service name reported in OTLP telemetry.
    /// </summary>
    [Required]
    public required string ServiceName { get; set; } = "snmp-collector";
}
