using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Liveness health check (HLTH-03). Iterates all liveness vector stamps and compares
/// each job's stamp age against its configured interval multiplied by the grace multiplier.
/// Also checks the heartbeat pipeline-arrival stamp (HB-06, HB-07) to detect silent pipelines
/// (blocked channel, crashed consumer, broken MediatR registration).
/// Returns Unhealthy with diagnostic data when any stamp is stale; returns Healthy silently
/// when all stamps are fresh (no log on healthy -- HLTH-07).
/// <para>
/// K8s liveness probe with periodSeconds=15 and failureThreshold=3 means 45s of consecutive
/// stale responses before pod restart. With GraceMultiplier=2.0 and a 30s poll job, staleness
/// fires at >60s -- providing margin before K8s acts.
/// </para>
/// </summary>
public sealed class LivenessHealthCheck : IHealthCheck
{
    private readonly ILivenessVectorService _liveness;
    private readonly IJobIntervalRegistry _intervals;
    private readonly double _graceMultiplier;
    private readonly IHeartbeatLivenessService _heartbeatLiveness;
    private readonly int _heartbeatIntervalSeconds;
    private readonly ILogger<LivenessHealthCheck> _logger;

    public LivenessHealthCheck(
        ILivenessVectorService liveness,
        IJobIntervalRegistry intervals,
        IOptions<LivenessOptions> options,
        IHeartbeatLivenessService heartbeatLiveness,
        IOptions<SnmpHeartbeatJobOptions> heartbeatOptions,
        ILogger<LivenessHealthCheck> logger)
    {
        _liveness = liveness;
        _intervals = intervals;
        _graceMultiplier = options.Value.GraceMultiplier;
        _heartbeatLiveness = heartbeatLiveness;
        _heartbeatIntervalSeconds = heartbeatOptions.Value.IntervalSeconds;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stamps = _liveness.GetAllStamps();
        var now = DateTimeOffset.UtcNow;
        var staleEntries = new Dictionary<string, object>();
        var allEntries = new Dictionary<string, object>();

        foreach (var (jobKey, lastStamp) in stamps)
        {
            if (!_intervals.TryGetInterval(jobKey, out var intervalSeconds))
                continue; // unregistered job key -- skip

            var threshold = TimeSpan.FromSeconds(intervalSeconds * _graceMultiplier);
            var age = now - lastStamp;

            var entry = new
            {
                ageSeconds = Math.Round(age.TotalSeconds, 1),
                thresholdSeconds = threshold.TotalSeconds,
                lastStamp = lastStamp.ToString("O"),
                stale = age > threshold
            };

            allEntries[jobKey] = entry;

            if (age > threshold)
                staleEntries[jobKey] = entry;
        }

        // Pipeline-arrival liveness: detect silent pipeline (blocked channel, crashed consumer, etc.)
        var pipelineThreshold = TimeSpan.FromSeconds(_heartbeatIntervalSeconds * _graceMultiplier);
        var pipelineArrival = _heartbeatLiveness.LastArrival;

        if (pipelineArrival.HasValue)
        {
            var pipelineAge = now - pipelineArrival.Value;
            var pipelineEntry = new
            {
                ageSeconds = Math.Round(pipelineAge.TotalSeconds, 1),
                thresholdSeconds = pipelineThreshold.TotalSeconds,
                lastStamp = pipelineArrival.Value.ToString("O"),
                stale = pipelineAge > pipelineThreshold
            };
            allEntries["pipeline-heartbeat"] = pipelineEntry;
            if (pipelineAge > pipelineThreshold)
                staleEntries["pipeline-heartbeat"] = pipelineEntry;
        }
        else
        {
            // Never stamped since startup — treat as stale.
            // K8s failureThreshold=3 at periodSeconds=15 provides 45s margin.
            // SnmpHeartbeatJob fires at StartNow(), so first arrival within 15-30s.
            var pipelineEntry = new
            {
                ageSeconds = (double?)null,
                thresholdSeconds = pipelineThreshold.TotalSeconds,
                lastStamp = (string?)null,
                stale = true
            };
            allEntries["pipeline-heartbeat"] = pipelineEntry;
            staleEntries["pipeline-heartbeat"] = pipelineEntry;
        }

        if (staleEntries.Count > 0)
        {
            // HLTH-05: 503 with diagnostic log
            _logger.LogWarning(
                "Liveness check failed: {StaleCount} stale job(s) detected",
                staleEntries.Count);

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"{staleEntries.Count} stale job(s)",
                data: allEntries.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.Ordinal) as IReadOnlyDictionary<string, object>));
        }

        // HLTH-07: Healthy returns 200 silently (no log)
        return Task.FromResult(HealthCheckResult.Healthy(
            data: allEntries.ToDictionary(
                kv => kv.Key,
                kv => kv.Value,
                StringComparer.Ordinal) as IReadOnlyDictionary<string, object>));
    }
}
