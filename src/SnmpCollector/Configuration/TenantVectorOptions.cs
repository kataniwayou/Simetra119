namespace SnmpCollector.Configuration;

/// <summary>
/// Top-level options for tenant vector configuration.
/// Bound from the "TenantVector" JSON section.
/// Each tenant defines prioritized metric slots for poll scheduling.
/// </summary>
public sealed class TenantVectorOptions
{
    public const string SectionName = "TenantVector";

    /// <summary>
    /// List of tenant configurations, each with an ID, priority, and metric slots.
    /// An empty list is valid (no tenants configured).
    /// </summary>
    public List<TenantOptions> Tenants { get; set; } = [];
}
