namespace SnmpCollector.Pipeline;

/// <summary>
/// Thread-safe correlation service using a volatile string field for lock-free reads.
/// <para>
/// <b>Concurrency model:</b> Single writer (startup code writes first, then CorrelationJob
/// is the sole writer at its configured interval), multiple readers (all jobs read
/// CurrentCorrelationId during execution). The volatile keyword ensures that writes
/// from the single writer are immediately visible to all reader threads without locks.
/// </para>
/// Replaces <c>StartupCorrelationService</c> which generated a fixed ID at construction.
/// </summary>
public sealed class RotatingCorrelationService : ICorrelationService
{
    private volatile string _correlationId = string.Empty;
    private static readonly AsyncLocal<string?> _operationCorrelationId = new();

    /// <inheritdoc />
    public string CurrentCorrelationId => _correlationId;

    /// <inheritdoc />
    public string? OperationCorrelationId
    {
        get => _operationCorrelationId.Value;
        set => _operationCorrelationId.Value = value;
    }

    /// <inheritdoc />
    public void SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
    }
}
