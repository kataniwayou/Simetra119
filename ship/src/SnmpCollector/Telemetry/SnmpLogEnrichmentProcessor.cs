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
/// </summary>
public sealed class SnmpLogEnrichmentProcessor : BaseProcessor<LogRecord>
{
    private readonly ICorrelationService _correlationService;
    private readonly string _hostName;
    private readonly Func<string> _roleProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpLogEnrichmentProcessor"/> class.
    /// </summary>
    /// <param name="correlationService">Service providing the current correlation ID.</param>
    /// <param name="hostName">Host name resolved from <c>PHYSICAL_HOSTNAME</c> env var or <c>Environment.MachineName</c>.</param>
    /// <param name="roleProvider">
    /// Delegate returning the current role (e.g. "leader" or "follower").
    /// Evaluated on every log record to reflect dynamic leadership changes.
    /// Bound to ILeaderElection.CurrentRole via closure in DI wiring.
    /// </param>
    public SnmpLogEnrichmentProcessor(
        ICorrelationService correlationService,
        string hostName,
        Func<string> roleProvider)
    {
        _correlationService = correlationService
            ?? throw new ArgumentNullException(nameof(correlationService));
        _hostName = hostName
            ?? throw new ArgumentNullException(nameof(hostName));
        _roleProvider = roleProvider
            ?? throw new ArgumentNullException(nameof(roleProvider));
    }

    /// <inheritdoc />
    public override void OnEnd(LogRecord data)
    {
        // Null-check Attributes -- it can be null when no structured log parameters
        // are provided (e.g. logger.LogInformation("plain message")).
        var attributes = data.Attributes?.ToList()
            ?? new List<KeyValuePair<string, object?>>(3);

        attributes.Add(new KeyValuePair<string, object?>("host_name", _hostName));
        attributes.Add(new KeyValuePair<string, object?>("role", _roleProvider()));
        attributes.Add(new KeyValuePair<string, object?>("correlationId",
            _correlationService.OperationCorrelationId ?? _correlationService.CurrentCorrelationId));

        data.Attributes = attributes;
    }
}
