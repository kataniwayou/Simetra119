namespace SnmpCollector.Telemetry;

/// <summary>
/// No-op implementation of <see cref="IPreferredStampReader"/> used when
/// the preferred-leader feature is disabled (local dev or PreferredNode absent).
/// Always returns false — no pod is considered preferred.
/// </summary>
public sealed class NullPreferredStampReader : IPreferredStampReader
{
    /// <inheritdoc />
    public bool IsPreferredStampFresh => false;
}
