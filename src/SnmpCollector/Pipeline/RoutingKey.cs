namespace SnmpCollector.Pipeline;

/// <summary>
/// Composite routing key identifying a single metric stream: (IP, port, metric name).
/// Designed to be used as a FrozenDictionary key with RoutingKeyComparer for case-insensitive lookups.
/// </summary>
public readonly record struct RoutingKey(string Ip, int Port, string MetricName) : IEquatable<RoutingKey>;

/// <summary>
/// Case-insensitive equality comparer for RoutingKey, suitable for FrozenDictionary construction.
/// Treats Ip and MetricName as OrdinalIgnoreCase; Port is an exact integer match.
/// </summary>
public sealed class RoutingKeyComparer : IEqualityComparer<RoutingKey>
{
    public static readonly RoutingKeyComparer Instance = new();

    private RoutingKeyComparer() { }

    public bool Equals(RoutingKey x, RoutingKey y)
        => x.Port == y.Port
           && string.Equals(x.Ip, y.Ip, StringComparison.OrdinalIgnoreCase)
           && string.Equals(x.MetricName, y.MetricName, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(RoutingKey key)
        => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Ip),
            key.Port,
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.MetricName));
}
