namespace SnmpCollector.Pipeline;

/// <summary>
/// Tracks liveness of scheduled jobs via UTC timestamp stamps. Each job stamps its key
/// in a finally block after execution completes (success or failure), allowing health
/// checks to detect stalled jobs by comparing stamp age to expected interval.
/// <para>
/// Stamps are written ONLY by job completion -- not by trap arrival, not by pipeline
/// processing. This ensures the liveness vector reflects scheduler health, not data flow.
/// </para>
/// </summary>
public interface ILivenessVectorService
{
    /// <summary>
    /// Records the current UTC time for the given job key, indicating the job just completed.
    /// Called from the finally block of each job's Execute method.
    /// </summary>
    /// <param name="jobKey">The unique job key (e.g., "correlation").</param>
    void Stamp(string jobKey);

    /// <summary>
    /// Returns the last completion timestamp for a job key, or null if the job has never completed.
    /// </summary>
    /// <param name="jobKey">The unique job key to query.</param>
    /// <returns>The last stamp time, or null if never stamped.</returns>
    DateTimeOffset? GetStamp(string jobKey);

    /// <summary>
    /// Returns a snapshot of all liveness stamps as a read-only dictionary.
    /// The returned dictionary is a defensive copy -- mutations to the underlying store
    /// after this call do not affect the returned snapshot.
    /// </summary>
    /// <returns>A read-only dictionary mapping job keys to their last completion timestamps.</returns>
    IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps();

    /// <summary>
    /// Removes the liveness stamp for a job key. Called during config reload when
    /// a device or poll group is removed and its Quartz job is unscheduled.
    /// No-op if the key does not exist.
    /// </summary>
    /// <param name="jobKey">The unique job key to remove.</param>
    void Remove(string jobKey);
}
