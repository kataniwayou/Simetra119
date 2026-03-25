namespace SnmpCollector.Configuration;

/// <summary>
/// SnmpCollector-specific logging configuration. Bound from "Logging" section.
/// Note: The LogLevel sub-section is handled by the built-in .NET logging system.
/// This class only captures the SnmpCollector-specific EnableConsole field.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    /// <summary>
    /// Whether to enable console logging output.
    /// </summary>
    public bool EnableConsole { get; set; }
}
