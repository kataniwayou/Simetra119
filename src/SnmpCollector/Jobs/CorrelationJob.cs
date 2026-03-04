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
    private readonly ILogger<CorrelationJob> _logger;

    public CorrelationJob(
        ICorrelationService correlation,
        ILogger<CorrelationJob> logger)
    {
        _correlation = correlation;
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            // SCHED-07: Generate new correlationId
            var newCorrelationId = Guid.NewGuid().ToString("N");
            _correlation.SetCorrelationId(newCorrelationId);

            _logger.LogInformation(
                "Correlation ID rotated to {CorrelationId}",
                newCorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Correlation job {JobKey} failed", jobKey);
        }

        return Task.CompletedTask;
    }
}
