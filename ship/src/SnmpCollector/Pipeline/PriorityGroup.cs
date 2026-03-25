namespace SnmpCollector.Pipeline;

/// <summary>
/// Groups tenants that share the same priority level.
/// Used by the fan-out engine to process tenants in priority order.
/// </summary>
public record PriorityGroup(int Priority, IReadOnlyList<Tenant> Tenants);
