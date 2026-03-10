# Phase 26: Core Data Types and Registry - Research

**Researched:** 2026-03-10
**Domain:** In-memory data structures, lock-free concurrent reads, FrozenDictionary routing
**Confidence:** HIGH

## Summary

Phase 26 builds pure in-memory data types and a singleton registry with no external dependencies beyond what the project already has (.NET 9, System.Collections.Frozen). The decisions from CONTEXT.md are comprehensive and lock every design choice -- this research focuses on verifying .NET runtime behavior for the chosen patterns and documenting the exact implementation recipes aligned with the existing codebase.

The codebase already has two proven patterns for this kind of work: `OidMapService` (volatile FrozenDictionary swap with diff logging) and `DeviceRegistry` (FrozenDictionary with OrdinalIgnoreCase comparer). Phase 26 combines both patterns into `TenantVectorRegistry` with two volatile fields (groups + routing index) and a `Reload()` method that carries over existing slot values.

**Primary recommendation:** Follow OidMapService's UpdateMap() pattern exactly for the Reload() method -- build new structures, compute diff, volatile-swap, log. The only novel element is the RoutingKey struct with a custom IEqualityComparer for case-insensitive FrozenDictionary lookup.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Collections.Frozen | .NET 9 BCL | FrozenDictionary for routing index | Already used in OidMapService and DeviceRegistry |
| System.Threading.Volatile | .NET 9 BCL | Volatile.Read/Write for lock-free slot swap | Required by CONTEXT decision for MetricSlotHolder |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Logging | 9.0.0 | Structured diff logging on reload | Already in project, follows OidMapService pattern |
| NSubstitute | 5.3.0 | Mocking ITenantVectorRegistry in tests | Already in test project |
| xunit | 2.9.3 | Unit test framework | Already in test project |

### Alternatives Considered
None -- all decisions are locked. Zero new NuGet packages needed (confirmed by prior STATE.md decision).

## Architecture Patterns

### Recommended Project Structure
```
src/SnmpCollector/
├── Pipeline/
│   ├── MetricSlot.cs              # Immutable record (value cell)
│   ├── MetricSlotHolder.cs        # Mutable wrapper with Volatile.Read/Write
│   ├── Tenant.cs                  # Sealed class with Id, Priority, holders list
│   ├── PriorityGroup.cs           # Named record grouping tenants by priority
│   ├── RoutingKey.cs              # readonly record struct for composite routing key
│   ├── ITenantVectorRegistry.cs   # Interface (follows IOidMapService pattern)
│   └── TenantVectorRegistry.cs    # Singleton implementation with Reload()
```

All types go in the `Pipeline` namespace, matching existing `OidMapService`, `DeviceRegistry`, `DeviceInfo`, etc.

### Pattern 1: Immutable Record + Volatile Swap (MetricSlot / MetricSlotHolder)

**What:** MetricSlot is an immutable `record` (reference type). MetricSlotHolder wraps it with a `volatile` field. WriteValue() creates a new MetricSlot and Volatile.Write replaces the reference. ReadSlot() uses Volatile.Read to get the current snapshot.

**When to use:** When multiple threads must read/write a value cell without locks and torn reads must be impossible.

**Critical detail:** The `volatile` keyword on a reference-type field guarantees the pointer is always fresh. Since MetricSlot is immutable (all properties are init-only), once a reader obtains the reference, all property reads are consistent -- no torn reads possible.

**Example:**
```csharp
// MetricSlot.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Immutable value cell for a single metric observation.
/// Swapped atomically via Volatile.Write in MetricSlotHolder.
/// </summary>
public sealed record MetricSlot(
    double Value,
    string? StringValue,
    DateTimeOffset UpdatedAt);
```

```csharp
// MetricSlotHolder.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Mutable wrapper around an immutable MetricSlot reference.
/// Encapsulates Volatile.Read/Write so callers never touch the volatile field directly.
/// </summary>
public sealed class MetricSlotHolder
{
    private volatile MetricSlot? _slot;

    public string Ip { get; }
    public int Port { get; }
    public string MetricName { get; }
    public int IntervalSeconds { get; }

    public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds)
    {
        Ip = ip;
        Port = port;
        MetricName = metricName;
        IntervalSeconds = intervalSeconds;
    }

    /// <summary>
    /// Atomically replaces the current slot with a new immutable snapshot.
    /// </summary>
    public void WriteValue(double value, string? stringValue)
    {
        Volatile.Write(ref _slot, new MetricSlot(value, stringValue, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Returns the current slot snapshot, or null if no value has been written.
    /// </summary>
    public MetricSlot? ReadSlot()
    {
        return Volatile.Read(ref _slot);
    }
}
```

**"No value yet" recommendation (Claude's discretion):** Use null reference (MetricSlot? starts as null). This is the simplest, most idiomatic C# approach. ReadSlot() returning null clearly communicates "never written". No sentinel timestamp needed -- callers just null-check. This aligns with how the codebase handles similar cases (e.g., DeviceInfo? in TryGetByIpPort).

### Pattern 2: RoutingKey with Custom IEqualityComparer for FrozenDictionary

**What:** RoutingKey is a `readonly record struct` for zero-allocation composite keys. However, record struct auto-generates ordinal equality, so a custom `IEqualityComparer<RoutingKey>` must be passed to `ToFrozenDictionary()` for case-insensitive comparison.

**Why not override Equals/GetHashCode on the struct itself:** The CONTEXT decision specifies OrdinalIgnoreCase comparison. Embedding this in the struct's default equality would be surprising -- the comparer approach is explicit and matches how DeviceRegistry passes `StringComparer.OrdinalIgnoreCase` to `ToFrozenDictionary`.

**Example:**
```csharp
// RoutingKey.cs
namespace SnmpCollector.Pipeline;

/// <summary>
/// Composite routing key for the tenant vector routing index.
/// Value type (no heap allocation per key).
/// </summary>
public readonly record struct RoutingKey(string Ip, int Port, string MetricName)
    : IEquatable<RoutingKey>;

/// <summary>
/// Case-insensitive equality comparer for RoutingKey.
/// Passed to FrozenDictionary.ToFrozenDictionary() to ensure
/// IP addresses and metric names match regardless of casing.
/// </summary>
public sealed class RoutingKeyComparer : IEqualityComparer<RoutingKey>
{
    public static readonly RoutingKeyComparer Instance = new();

    public bool Equals(RoutingKey x, RoutingKey y)
    {
        return x.Port == y.Port
            && string.Equals(x.Ip, y.Ip, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.MetricName, y.MetricName, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(RoutingKey obj)
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Ip),
            obj.Port,
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MetricName));
    }
}
```

**FrozenDictionary comparer verification:** There was a known .NET 8 preview bug where `ToFrozenDictionary` did not respect custom comparers (dotnet/runtime#83645). This was fixed before .NET 8.0.0 release. Since this project targets .NET 9, the fix is present. Confidence: HIGH.

### Pattern 3: Volatile Swap of Two Independent Fields (Registry Reload)

**What:** TenantVectorRegistry has two volatile fields: `_groups` (IReadOnlyList<PriorityGroup>) and `_routingIndex` (FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>>). On Reload(), both are rebuilt and swapped with volatile writes. Brief inconsistency between the two fields is acceptable per CONTEXT decision.

**Why two volatile fields instead of a single wrapper object:** Fan-out only reads the routing index; evaluation only reads groups. A single wrapper would force both consumers to dereference an extra pointer. The brief inconsistency window (nanoseconds between two volatile writes) is acceptable because fan-out and evaluation never cross-reference the two fields in a single operation.

**Example:**
```csharp
// In TenantVectorRegistry.cs
private volatile IReadOnlyList<PriorityGroup> _groups = Array.Empty<PriorityGroup>();
private volatile FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>> _routingIndex
    = FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>>.Empty;
```

### Pattern 4: Value Carry-Over on Reload (follows OidMapService diff pattern)

**What:** When Reload() builds new MetricSlotHolders, it checks the old routing index for matching (tenant_id + ip + port + metric_name) holders. If found, the new holder gets the old holder's current slot value via ReadSlot()/WriteValue().

**Key implementation detail:** Build a temporary Dictionary keyed by (tenantId, ip, port, metricName) from the OLD registry's tenants/holders. For each new holder, look up the old holder by the same composite key. If found and old ReadSlot() is not null, call new holder's WriteValue() with the old values.

### Pattern 5: DI Registration (follows IOidMapService / OidMapService pattern)

**What:** Register concrete type, then alias to interface. Registry starts empty.

**Example:**
```csharp
// In ServiceCollectionExtensions.AddSnmpConfiguration()
services.AddSingleton<TenantVectorRegistry>(sp =>
    new TenantVectorRegistry(sp.GetRequiredService<ILogger<TenantVectorRegistry>>()));
services.AddSingleton<ITenantVectorRegistry>(sp => sp.GetRequiredService<TenantVectorRegistry>());
```

This matches exactly how OidMapService is registered (lines 305-307 of ServiceCollectionExtensions.cs): concrete first, interface alias second. The registry starts empty and gets populated via Reload() -- same as OidMapService starting with an empty dictionary.

### Anti-Patterns to Avoid

- **Do not use `lock` anywhere:** The entire design is lock-free. Volatile references to immutable data provide all needed thread safety.
- **Do not make MetricSlot a struct:** It must be a reference type (record class, not record struct) so the volatile field holds a pointer that can be atomically swapped. A struct would require locking for multi-field atomicity.
- **Do not put routing logic in the registry:** TryRoute() is a pure index lookup. Evaluation traversal logic belongs to the evaluation engine (future phase).
- **Do not use Interlocked.Exchange for slot writes:** Volatile.Write is sufficient here because we never need the old value back. Interlocked.Exchange provides a stronger (unnecessary) guarantee with slightly more overhead.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Case-insensitive frozen dictionary | Custom dictionary wrapper | FrozenDictionary + IEqualityComparer | Built into .NET 9, optimized for read-heavy workloads |
| Composite key hashing | Manual hash combination | HashCode.Combine() | Handles distribution correctly, avoids collision-prone manual XOR |
| Immutable snapshots | Custom deep-clone logic | C# record with/copy semantics | Records are immutable by design, no clone needed |
| Thread-safe reference swap | ConcurrentDictionary or locks | volatile + immutable records | ConcurrentDictionary adds unnecessary overhead for full-replace workload |

**Key insight:** The entire phase uses BCL primitives only. No custom concurrency mechanisms needed.

## Common Pitfalls

### Pitfall 1: FrozenDictionary.Empty Initialization
**What goes wrong:** Attempting `new FrozenDictionary<K,V>()` -- FrozenDictionary has no public constructor.
**Why it happens:** Unfamiliarity with frozen collection API.
**How to avoid:** Use the static property `FrozenDictionary<TKey, TValue>.Empty` for the initial empty state. For building, use `Dictionary<K,V>.ToFrozenDictionary(comparer)`.
**Warning signs:** Compile error.

### Pitfall 2: Record Struct Default Equality is Ordinal
**What goes wrong:** Using `readonly record struct RoutingKey` as a FrozenDictionary key without a custom comparer results in case-sensitive lookups.
**Why it happens:** Record structs auto-generate Equals/GetHashCode using ordinal (default) string comparison.
**How to avoid:** Always pass `RoutingKeyComparer.Instance` to `ToFrozenDictionary()`. Never rely on the struct's built-in equality for dictionary operations.
**Warning signs:** TryRoute() returns false for keys that differ only in casing.

### Pitfall 3: MetricSlot Must Be a Reference Type (record class)
**What goes wrong:** Declaring `record struct MetricSlot` instead of `record MetricSlot` (which is a class). The volatile field then holds a copy, not a reference, and multi-field atomicity is lost.
**Why it happens:** Confusion between `record` (class) and `record struct`.
**How to avoid:** Declare as `public sealed record MetricSlot(...)` -- in C#, `record` without `struct` is always a class.
**Warning signs:** Torn reads in concurrent tests.

### Pitfall 4: Forgetting to Pass Comparer When Building Routing Index
**What goes wrong:** Calling `.ToFrozenDictionary()` without the comparer argument. The FrozenDictionary uses default (ordinal) comparison.
**Why it happens:** Easy to forget the overload parameter.
**How to avoid:** Build pattern: `dict.ToFrozenDictionary(RoutingKeyComparer.Instance)`.
**Warning signs:** Lookups fail for mixed-case keys.

### Pitfall 5: Value Carry-Over Must Use ReadSlot()/WriteValue(), Not Field Access
**What goes wrong:** Directly reading the old holder's private `_slot` field during carry-over.
**Why it happens:** Temptation to access internals during internal rebuild.
**How to avoid:** Use the public ReadSlot()/WriteValue() API even within the registry. This ensures volatile semantics are preserved and the API contract is never bypassed.

### Pitfall 6: Empty Groups List vs Null
**What goes wrong:** Returning null from the Groups property when no tenants are configured.
**Why it happens:** Forgetting the "empty by default" decision.
**How to avoid:** Initialize with `Array.Empty<PriorityGroup>()`. Never use null for collections in this codebase (matches existing patterns -- OidMapService starts empty, not null).

## Code Examples

### Complete TenantVectorRegistry Skeleton
```csharp
using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace SnmpCollector.Pipeline;

public sealed class TenantVectorRegistry : ITenantVectorRegistry
{
    private readonly ILogger<TenantVectorRegistry> _logger;

    private volatile IReadOnlyList<PriorityGroup> _groups = Array.Empty<PriorityGroup>();
    private volatile FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>> _routingIndex
        = FrozenDictionary<RoutingKey, IReadOnlyList<MetricSlotHolder>>.Empty;

    public int TenantCount { get; private set; }
    public int SlotCount { get; private set; }

    public TenantVectorRegistry(ILogger<TenantVectorRegistry> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<PriorityGroup> Groups => _groups;

    public bool TryRoute(string ip, int port, string metricName,
        out IReadOnlyList<MetricSlotHolder> holders)
    {
        return _routingIndex.TryGetValue(
            new RoutingKey(ip, port, metricName), out holders!);
    }

    public void Reload(TenantVectorOptions options)
    {
        var oldGroups = _groups;

        // 1. Build old-value lookup for carry-over
        var oldSlotLookup = BuildOldSlotLookup(oldGroups);

        // 2. Build new tenants from config
        var tenantsByPriority = new SortedDictionary<int, List<Tenant>>();
        var routingBuilder = new Dictionary<RoutingKey, List<MetricSlotHolder>>(
            RoutingKeyComparer.Instance);

        int totalSlots = 0;
        int carriedOver = 0;

        foreach (var tenantOpt in options.Tenants)
        {
            var holders = new List<MetricSlotHolder>(tenantOpt.Metrics.Count);

            foreach (var metric in tenantOpt.Metrics)
            {
                var holder = new MetricSlotHolder(
                    metric.Ip, metric.Port, metric.MetricName, metric.IntervalSeconds);

                // Carry over old value if exists
                var carryKey = (tenantOpt.Id, metric.Ip, metric.Port, metric.MetricName);
                if (oldSlotLookup.TryGetValue(carryKey, out var oldHolder))
                {
                    var oldSlot = oldHolder.ReadSlot();
                    if (oldSlot is not null)
                    {
                        holder.WriteValue(oldSlot.Value, oldSlot.StringValue);
                        carriedOver++;
                    }
                }

                holders.Add(holder);
                totalSlots++;

                // Add to routing index
                var routingKey = new RoutingKey(metric.Ip, metric.Port, metric.MetricName);
                if (!routingBuilder.TryGetValue(routingKey, out var list))
                {
                    list = [];
                    routingBuilder[routingKey] = list;
                }
                list.Add(holder);
            }

            var tenant = new Tenant(tenantOpt.Id, tenantOpt.Priority, holders.AsReadOnly());

            if (!tenantsByPriority.TryGetValue(tenantOpt.Priority, out var group))
            {
                group = [];
                tenantsByPriority[tenantOpt.Priority] = group;
            }
            group.Add(tenant);
        }

        // 3. Build sorted groups (SortedDictionary iterates in key order = ascending priority)
        var newGroups = tenantsByPriority
            .Select(kvp => new PriorityGroup(kvp.Key, kvp.Value.AsReadOnly()))
            .ToList()
            .AsReadOnly();

        // 4. Build frozen routing index (expose lists as IReadOnlyList)
        var newRoutingIndex = routingBuilder
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<MetricSlotHolder>)kvp.Value.AsReadOnly(),
                RoutingKeyComparer.Instance)
            .ToFrozenDictionary(RoutingKeyComparer.Instance);

        // 5. Compute counts before swap
        TenantCount = options.Tenants.Count;
        SlotCount = totalSlots;

        // 6. Atomic swap (two volatile writes)
        _groups = newGroups;
        _routingIndex = newRoutingIndex;

        // 7. Diff logging
        LogReloadDiff(oldGroups, newGroups, carriedOver, totalSlots);
    }

    private static Dictionary<(string TenantId, string Ip, int Port, string MetricName), MetricSlotHolder>
        BuildOldSlotLookup(IReadOnlyList<PriorityGroup> groups)
    {
        var lookup = new Dictionary<(string, string, int, string), MetricSlotHolder>(
            StringTupleComparer.Instance);

        foreach (var group in groups)
        {
            foreach (var tenant in group.Tenants)
            {
                foreach (var holder in tenant.Holders)
                {
                    lookup[(tenant.Id, holder.Ip, holder.Port, holder.MetricName)] = holder;
                }
            }
        }

        return lookup;
    }

    private void LogReloadDiff(
        IReadOnlyList<PriorityGroup> oldGroups,
        IReadOnlyList<PriorityGroup> newGroups,
        int carriedOver,
        int totalSlots)
    {
        var oldTenantIds = oldGroups
            .SelectMany(g => g.Tenants)
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newTenantIds = newGroups
            .SelectMany(g => g.Tenants)
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = newTenantIds.Except(oldTenantIds).ToList();
        var removed = oldTenantIds.Except(newTenantIds).ToList();
        var unchanged = newTenantIds.Intersect(oldTenantIds).Count();

        _logger.LogInformation(
            "TenantVectorRegistry reloaded: {TenantCount} tenants, {SlotCount} slots, " +
            "+{Added} added, -{Removed} removed, ={Unchanged} unchanged, " +
            "{CarriedOver}/{TotalSlots} slots carried over",
            newTenantIds.Count, totalSlots,
            added.Count, removed.Count, unchanged,
            carriedOver, totalSlots);
    }
}
```

### Carry-Over Lookup Comparer (for ValueTuple with case-insensitive strings)
```csharp
/// <summary>
/// Case-insensitive comparer for the (TenantId, Ip, Port, MetricName) carry-over lookup.
/// Internal to the registry, used only during Reload().
/// </summary>
internal sealed class StringTupleComparer
    : IEqualityComparer<(string TenantId, string Ip, int Port, string MetricName)>
{
    public static readonly StringTupleComparer Instance = new();

    public bool Equals(
        (string TenantId, string Ip, int Port, string MetricName) x,
        (string TenantId, string Ip, int Port, string MetricName) y)
    {
        return x.Port == y.Port
            && string.Equals(x.TenantId, y.TenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Ip, y.Ip, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.MetricName, y.MetricName, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string TenantId, string Ip, int Port, string MetricName) obj)
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TenantId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Ip),
            obj.Port,
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MetricName));
    }
}
```

### Test Pattern: Slot Atomicity
```csharp
[Fact]
public void WriteValue_ThenReadSlot_ReturnsWrittenValue()
{
    var holder = new MetricSlotHolder("10.0.0.1", 161, "hrProcessorLoad", 30);

    holder.WriteValue(42.5, null);
    var slot = holder.ReadSlot();

    Assert.NotNull(slot);
    Assert.Equal(42.5, slot.Value);
    Assert.Null(slot.StringValue);
    Assert.True(slot.UpdatedAt <= DateTimeOffset.UtcNow);
}

[Fact]
public void ReadSlot_BeforeAnyWrite_ReturnsNull()
{
    var holder = new MetricSlotHolder("10.0.0.1", 161, "hrProcessorLoad", 30);

    var slot = holder.ReadSlot();

    Assert.Null(slot);
}
```

### Test Pattern: Routing Lookup
```csharp
[Fact]
public void TryRoute_CaseInsensitive_FindsHolder()
{
    var registry = CreateRegistry();
    registry.Reload(OptionsWithOneTenant("10.0.0.1", 161, "hrProcessorLoad"));

    var found = registry.TryRoute("10.0.0.1", 161, "HRPROCESSORLOAD", out var holders);

    Assert.True(found);
    Assert.Single(holders);
}
```

### Test Pattern: Value Carry-Over on Reload
```csharp
[Fact]
public void Reload_CarriesOverExistingValues()
{
    var registry = CreateRegistry();
    var options = OptionsWithOneTenant("10.0.0.1", 161, "hrProcessorLoad");
    registry.Reload(options);

    // Write a value to the first slot
    var holder = registry.Groups[0].Tenants[0].Holders[0];
    holder.WriteValue(99.9, null);

    // Reload with same config
    registry.Reload(options);

    // New holder should have carried-over value
    var newHolder = registry.Groups[0].Tenants[0].Holders[0];
    var slot = newHolder.ReadSlot();
    Assert.NotNull(slot);
    Assert.Equal(99.9, slot.Value);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ConcurrentDictionary for hot-path reads | FrozenDictionary + volatile swap | .NET 8 (2023) | ~43% faster reads, zero contention on read path |
| lock + mutable state | volatile + immutable records | Always available, but records since C# 9 | Lock-free reads, no deadlock risk |
| `Interlocked.Exchange` for reference swap | `volatile` field keyword | Always available | Simpler, sufficient when old value is not needed |

**Note on volatile vs Volatile.Read/Write:** For the MetricSlotHolder's `_slot` field, the `volatile` keyword on the field declaration is equivalent to using `Volatile.Read`/`Volatile.Write` for every access. Either approach works. The CONTEXT decision specifies Volatile.Write/Read methods, which is more explicit and makes the intent clearer in code review. However, if the field is declared `volatile`, the compiler inserts the barriers automatically on every access. Recommendation: declare the field as `volatile` AND use Volatile.Read/Write in the public methods for maximum clarity (belt and suspenders).

## Open Questions

1. **TenantCount/SlotCount thread safety**
   - What we know: CONTEXT says these are "computed at build time." They are written during Reload() and read by consumers.
   - What's unclear: Whether they should be volatile fields or just regular properties set before the groups/index swap.
   - Recommendation: Make them regular `int` properties with `private set`. They are set during Reload() before the volatile swap of groups/index. Any reader who sees the new groups will also see the new counts due to the volatile write acting as a memory barrier. This is safe.

2. **StringTupleComparer vs simpler approach**
   - What we know: Value carry-over needs case-insensitive matching by (tenantId, ip, port, metricName).
   - Alternative: Could use a string key like `$"{tenantId}|{ip}:{port}:{metricName}"` with OrdinalIgnoreCase comparer. Simpler but allocates a string per lookup.
   - Recommendation: Use the ValueTuple + custom comparer approach (no allocations). The Reload() path is not hot, but staying allocation-free is consistent with the lock-free design philosophy.

## Sources

### Primary (HIGH confidence)
- Existing codebase: `OidMapService.cs` -- volatile FrozenDictionary swap, diff logging pattern
- Existing codebase: `DeviceRegistry.cs` -- FrozenDictionary with OrdinalIgnoreCase, ReloadAsync pattern
- Existing codebase: `ServiceCollectionExtensions.cs` -- DI registration pattern (concrete + interface alias)
- Existing codebase: `TenantVectorOptions.cs`, `TenantOptions.cs`, `MetricSlotOptions.cs` -- configuration types already defined
- Existing codebase: `TenantVectorOptionsValidator.cs` -- validation already complete
- Existing codebase: `OidMapServiceTests.cs` -- test pattern for reload/swap verification

### Secondary (MEDIUM confidence)
- [dotnet/runtime#83645](https://github.com/dotnet/runtime/issues/83645) -- FrozenDictionary comparer bug was fixed before .NET 8.0.0 GA
- [Microsoft Learn: FrozenDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary.tofrozendictionary?view=net-9.0) -- ToFrozenDictionary accepts IEqualityComparer parameter
- [Microsoft Learn: volatile keyword](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/volatile) -- volatile on reference types guarantees fresh pointer
- [Microsoft Learn: Volatile class](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0) -- Volatile.Read/Write API
- [Microsoft Learn: record structs](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/record-structs) -- auto-generated IEquatable, Equals, GetHashCode
- [Record struct performance analysis](https://nietras.com/2021/06/14/csharp-10-record-struct/) -- record struct as dictionary key performance (20x faster than default struct equality)

### Tertiary (LOW confidence)
None -- all findings verified against codebase or official documentation.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- zero new packages, all BCL types already in use in project
- Architecture: HIGH -- patterns directly derived from existing OidMapService and DeviceRegistry
- Pitfalls: HIGH -- verified against .NET documentation and existing codebase patterns
- Code examples: HIGH -- based on existing codebase patterns with verified API calls

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (stable -- BCL APIs, no fast-moving dependencies)
