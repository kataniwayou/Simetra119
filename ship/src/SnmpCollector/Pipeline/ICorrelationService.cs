namespace SnmpCollector.Pipeline;

/// <summary>
/// Provides a correlation ID for linking related pipeline operations across all jobs
/// within a time window. The correlation ID is rotated periodically by the CorrelationJob,
/// with the first ID generated directly on startup before any job fires.
/// </summary>
public interface ICorrelationService
{
    /// <summary>
    /// Gets the current global correlation ID. Thread-safe for concurrent readers.
    /// This value rotates when <see cref="SetCorrelationId"/> is called by CorrelationJob.
    /// </summary>
    string CurrentCorrelationId { get; }

    /// <summary>
    /// Gets or sets the operation-scoped correlation ID for the current async flow.
    /// Backed by <see cref="System.Threading.AsyncLocal{T}"/>, so each async context
    /// (job execution, trap processing) carries its own captured value.
    /// <para>
    /// Set this at the start of a job or pipeline operation to the correlationId that was
    /// current when the operation began. The formatter and OTLP enrichment read this first,
    /// falling back to <see cref="CurrentCorrelationId"/> if null.
    /// </para>
    /// </summary>
    string? OperationCorrelationId { get; set; }

    /// <summary>
    /// Sets a new global correlation ID. Must be called from a single writer at a time --
    /// startup code sets the first ID, then CorrelationJob is the sole writer.
    /// </summary>
    /// <param name="correlationId">The new correlation ID to set.</param>
    void SetCorrelationId(string correlationId);
}
