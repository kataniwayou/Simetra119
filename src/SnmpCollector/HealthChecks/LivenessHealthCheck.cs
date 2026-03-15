using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Liveness health check (HLTH-03). Iterates all liveness vector stamps and compares
/// each job's stamp age against its configured interval multiplied by the grace multiplier.
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
    private readonly ILogger<LivenessHealthCheck> _logger;

    public LivenessHealthCheck(
        ILivenessVectorService liveness,
        IJobIntervalRegistry intervals,
        IOptions<LivenessOptions> options,
        ILogger<LivenessHealthCheck> logger)
    {
        _liveness = liveness;
        _intervals = intervals;
        _graceMultiplier = options.Value.GraceMultiplier;
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
