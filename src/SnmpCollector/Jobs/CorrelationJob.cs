using Microsoft.Extensions.Logging;
using Quartz;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Jobs;

/// <summary>
/// Generates a new correlationId and sets it via <see cref="ICorrelationService.SetCorrelationId"/>,
/// rotating the shared correlation ID used for log grouping across all pipeline operations.
/// This is the sole scheduled writer of correlationId -- startup sets the first value, then
/// this job is the only writer.
/// </summary>
[DisallowConcurrentExecution]
public sealed class CorrelationJob : IJob
{
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly ILogger<CorrelationJob> _logger;

    public CorrelationJob(
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        ILogger<CorrelationJob> logger)
    {
        _correlation = correlation;
        _liveness = liveness;
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        // Capture the current global correlationId for this job's log context.
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            // SCHED-07: Generate new correlationId
            var newCorrelationId = Guid.NewGuid().ToString("N");
            _correlation.SetCorrelationId(newCorrelationId);

            // Update operation scope to the new ID so the rotation log line carries it.
            _correlation.OperationCorrelationId = newCorrelationId;

            _logger.LogInformation(
                "Correlation ID rotated to {CorrelationId}",
                newCorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Correlation job {JobKey} failed", jobKey);
        }
        finally
        {
            // HLTH-05: Stamp liveness vector on completion (always, even on failure)
            _liveness.Stamp(jobKey);
            // Clear operation-scoped correlationId.
            _correlation.OperationCorrelationId = null;
        }

        return Task.CompletedTask;
    }
}
