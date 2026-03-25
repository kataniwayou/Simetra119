using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Options for <see cref="SnmpConsoleFormatter"/>. Extends <see cref="ConsoleFormatterOptions"/>
/// with an <see cref="IServiceProvider"/> reference used to lazily resolve DI services at write time.
/// </summary>
public sealed class SnmpConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>
    /// The service provider used to resolve <see cref="ICorrelationService"/>
    /// and <see cref="ILeaderElection"/> on first write.
    /// Populated by <see cref="PostConfigureSnmpFormatterOptions"/>.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }
}

/// <summary>
/// Custom plain-text console formatter that prefixes every log line with hostname, role, and
/// correlationId context. Produces output in the format:
/// <code>
/// {timestamp} [{level}] [{hostname}|{podName}|{role}|{globalId}|{operationId}] {category} {message}
/// </code>
/// Replaces JSON structured console output with human-readable plain text suitable for
/// local development while retaining operational context. Shows BOTH global and operation
/// correlation IDs (operationId shown only when set in async context).
/// </summary>
public sealed class SnmpConsoleFormatter : ConsoleFormatter
{
    /// <summary>
    /// The formatter name used for registration and selection.
    /// </summary>
    public const string FormatterName = "snmp";

    private readonly IOptionsMonitor<SnmpConsoleFormatterOptions> _optionsMonitor;

    // Lazily resolved DI services (resolved on first Write call)
    private ICorrelationService? _correlationService;
    private ILeaderElection? _leaderElection;
    private bool _servicesResolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpConsoleFormatter"/> class.
    /// </summary>
    /// <param name="optionsMonitor">Monitor for formatter options containing the service provider.</param>
    public SnmpConsoleFormatter(IOptionsMonitor<SnmpConsoleFormatterOptions> optionsMonitor)
        : base(FormatterName)
    {
        _optionsMonitor = optionsMonitor
            ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    /// <inheritdoc />
    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null)
            return;

        EnsureServicesResolved();

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var level = GetLevelAbbreviation(logEntry.LogLevel);
        var hostname = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME") ?? Environment.MachineName;
        var podName = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        var role = _leaderElection?.CurrentRole ?? "unknown";
        var globalId = _correlationService?.CurrentCorrelationId ?? "none";
        var operationId = _correlationService?.OperationCorrelationId;
        var category = logEntry.Category;

        textWriter.Write(timestamp);
        textWriter.Write(" [");
        textWriter.Write(level);
        textWriter.Write("] [");
        textWriter.Write(hostname);
        textWriter.Write('|');
        textWriter.Write(podName);
        textWriter.Write('|');
        textWriter.Write(role);
        textWriter.Write('|');
        textWriter.Write(globalId);
        if (operationId is not null)
        {
            textWriter.Write('|');
            textWriter.Write(operationId);
        }
        textWriter.Write("] ");
        textWriter.Write(category);
        textWriter.Write(' ');
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }
    }

    /// <summary>
    /// Lazily resolves DI services from the service provider captured in options.
    /// Thread-safe: worst case resolves twice on concurrent first calls, both get same singletons.
    /// </summary>
    private void EnsureServicesResolved()
    {
        if (_servicesResolved)
            return;

        var sp = _optionsMonitor.CurrentValue.ServiceProvider;
        if (sp is null)
            return;

        _correlationService = sp.GetService<ICorrelationService>();
        _leaderElection = sp.GetService<ILeaderElection>();  // Phase 7: dynamic role
        _servicesResolved = true;
    }

    /// <summary>
    /// Maps <see cref="LogLevel"/> to a 3-character abbreviation.
    /// </summary>
    private static string GetLevelAbbreviation(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };
}

/// <summary>
/// Post-configures <see cref="SnmpConsoleFormatterOptions"/> to inject the root
/// <see cref="IServiceProvider"/>. This allows the formatter to lazily resolve
/// DI services (correlation, leader election) without constructor injection,
/// which is not supported by the <see cref="ConsoleFormatter"/> infrastructure.
/// </summary>
internal sealed class PostConfigureSnmpFormatterOptions
    : IPostConfigureOptions<SnmpConsoleFormatterOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public PostConfigureSnmpFormatterOptions(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public void PostConfigure(string? name, SnmpConsoleFormatterOptions options)
        => options.ServiceProvider = _serviceProvider;
}
