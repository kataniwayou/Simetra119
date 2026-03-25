namespace SnmpCollector.Pipeline;

/// <summary>
/// Registry mapping job keys to their configured trigger interval in seconds.
/// Populated during <c>AddSnmpScheduling</c> for each Quartz job, consumed by
/// <see cref="SnmpCollector.HealthChecks.LivenessHealthCheck"/> to compute per-job
/// staleness thresholds.
/// </summary>
public interface IJobIntervalRegistry
{
    /// <summary>
    /// Registers (or overwrites) the interval for a job key.
    /// Called during Quartz job/trigger configuration in <c>AddSnmpScheduling</c>.
    /// </summary>
    /// <param name="jobKey">The unique job key (e.g., "correlation").</param>
    /// <param name="intervalSeconds">The trigger repeat interval in seconds.</param>
    void Register(string jobKey, int intervalSeconds);

    /// <summary>
    /// Attempts to retrieve the registered interval for a job key.
    /// </summary>
    /// <param name="jobKey">The unique job key to query.</param>
    /// <param name="intervalSeconds">The interval in seconds, if found.</param>
    /// <returns><c>true</c> if the job key was found; otherwise <c>false</c>.</returns>
    bool TryGetInterval(string jobKey, out int intervalSeconds);

    /// <summary>
    /// Removes a job key from the registry. Called during config reload when
    /// a device or poll group is removed and its Quartz job is unscheduled.
    /// No-op if the key does not exist.
    /// </summary>
    /// <param name="jobKey">The unique job key to remove.</param>
    void Unregister(string jobKey);
}
