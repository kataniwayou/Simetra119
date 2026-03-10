namespace SnmpCollector.Pipeline;

/// <summary>
/// Represents a single tenant with its priority and the set of metric slot holders
/// that track current metric values for that tenant.
/// </summary>
public sealed class Tenant
{
    public string Id { get; }
    public int Priority { get; }
    public IReadOnlyList<MetricSlotHolder> Holders { get; }

    public Tenant(string id, int priority, IReadOnlyList<MetricSlotHolder> holders)
    {
        Id = id;
        Priority = priority;
        Holders = holders;
    }
}
