using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Jobs;

/// <summary>
/// Evaluates tenant priority groups on a fixed interval, running a 4-tier evaluation
/// loop (staleness, resolved-gate, evaluate-threshold, command dispatch) per tenant.
/// Stamps liveness vector on completion for <see cref="HealthChecks.LivenessHealthCheck"/>
/// staleness detection.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SnapshotJob : IJob
{
    private readonly ITenantVectorRegistry _registry;
    private readonly ISuppressionCache _suppressionCache;
    private readonly ICommandChannel _commandChannel;
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly SnapshotJobOptions _options;
    private readonly ILogger<SnapshotJob> _logger;

    public SnapshotJob(
        ITenantVectorRegistry registry,
        ISuppressionCache suppressionCache,
        ICommandChannel commandChannel,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        PipelineMetricService pipelineMetrics,
        IOptions<SnapshotJobOptions> options,
        ILogger<SnapshotJob> logger)
    {
        _registry = registry;
        _suppressionCache = suppressionCache;
        _commandChannel = commandChannel;
        _correlation = correlation;
        _liveness = liveness;
        _pipelineMetrics = pipelineMetrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            foreach (var group in _registry.Groups)
            {
                // Tier evaluation logic added in plans 48-02 through 48-04
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot job {JobKey} failed", jobKey);
        }
        finally
        {
            _liveness.Stamp(jobKey);
            _correlation.OperationCorrelationId = null;
        }
    }
}
