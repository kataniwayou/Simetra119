using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Telemetry;

/// <summary>
/// OpenTelemetry log processor that enriches every <see cref="LogRecord"/> with
/// host name, dynamic role (from leader election), and the current correlation ID.
/// <para>
/// These attributes appear on all log records regardless of the logger category,
/// providing consistent structured context for OTLP-exported logs.
/// </para>
/// <para>
/// Dependencies are resolved lazily to avoid DI deadlocks during builder.Build().
/// ILoggerFactory is created during Build(), which triggers this processor's factory
/// while holding the service provider construction lock. Eagerly resolving singletons
/// with deep dependency chains (K8sLeaseElection -> PreferredLeaderService ->
/// IOptions&lt;LeaseOptions&gt;) deadlocks against the same lock.
/// </para>
/// </summary>
public sealed class SnmpLogEnrichmentProcessor : BaseProcessor<LogRecord>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _hostName;

    // Resolved on first successful access. Volatile for cross-thread visibility.
    private volatile ICorrelationService? _correlationService;
    private volatile ILeaderElection? _leaderElection;
    private volatile bool _resolved;

    // Guard against reentrant resolution: if resolving ILeaderElection triggers a log
    // (e.g. PreferredLeaderService constructor logs), the OnEnd callback fires recursively.
    // This flag prevents the recursive call from trying to resolve again (deadlock).
    [ThreadStatic]
    private static bool _resolving;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpLogEnrichmentProcessor"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for lazy resolution of dependencies.</param>
    /// <param name="hostName">Host name resolved from <c>PHYSICAL_HOSTNAME</c> env var or <c>Environment.MachineName</c>.</param>
    public SnmpLogEnrichmentProcessor(
        IServiceProvider serviceProvider,
        string hostName)
    {
        _serviceProvider = serviceProvider
            ?? throw new ArgumentNullException(nameof(serviceProvider));
        _hostName = hostName
            ?? throw new ArgumentNullException(nameof(hostName));
    }

    /// <inheritdoc />
    public override void OnEnd(LogRecord data)
    {
        EnsureResolved();

        // Null-check Attributes -- it can be null when no structured log parameters
        // are provided (e.g. logger.LogInformation("plain message")).
        var attributes = data.Attributes?.ToList()
            ?? new List<KeyValuePair<string, object?>>(3);

        attributes.Add(new KeyValuePair<string, object?>("host_name", _hostName));
        attributes.Add(new KeyValuePair<string, object?>("role", _leaderElection?.CurrentRole ?? "unknown"));
        attributes.Add(new KeyValuePair<string, object?>("correlationId",
            _correlationService?.OperationCorrelationId ?? _correlationService?.CurrentCorrelationId ?? "none"));

        data.Attributes = attributes;
    }

    private void EnsureResolved()
    {
        if (_resolved || _resolving) return;

        _resolving = true;
        try
        {
            _correlationService = _serviceProvider.GetService<ICorrelationService>();
            _leaderElection = _serviceProvider.GetService<ILeaderElection>();
            _resolved = true;
        }
        catch
        {
            // Swallow — services not yet available. Will retry on next OnEnd.
        }
        finally
        {
            _resolving = false;
        }
    }
}
