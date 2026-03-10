# Technology Stack: Priority Vector Data Layer

**Project:** SnmpCollector -- Priority Vector Data Layer
**Researched:** 2026-03-10
**Overall confidence:** HIGH (all recommendations use BCL types already proven in this codebase)

---

## Executive Decision

**Zero new NuGet packages.** Every data structure needed ships in the .NET 9 BCL. The codebase already demonstrates the exact patterns required (volatile FrozenDictionary swap, ConcurrentDictionary for mutable state, reference-class wrappers for atomic field updates). This milestone extends existing patterns to a new domain -- it does not introduce new technology.

---

## Recommended Stack Additions

### 1. Tenant Metric Slot Storage

| Decision | Type | Namespace | Why |
|----------|------|-----------|-----|
| Slot container | `ConcurrentDictionary<string, MetricSlot>` | `System.Collections.Concurrent` | Same pattern as `LivenessVectorService` and `DeviceUnreachabilityTracker`. Pipeline thread writes value + timestamp; future consumers read. Lock-free for single-writer-per-key. |
| Slot value type | `sealed class MetricSlot` with `volatile` fields | BCL primitives | Reference type allows atomic swap of the dictionary entry. Volatile fields ensure cross-thread visibility without locks, matching `DeviceState` in `DeviceUnreachabilityTracker`. |
| Timestamp | `DateTimeOffset` via `volatile` field | `System` | Matches `LivenessVectorService` pattern. UTC only. |

**Concrete type design:**

```csharp
/// <summary>
/// Single metric value slot for a tenant. Written by pipeline thread,
/// potentially read by future consumers (API, export).
/// Reference type: ConcurrentDictionary stores reference, so reads always
/// get a consistent snapshot of (Value, UpdatedAt).
/// </summary>
public sealed class MetricSlot
{
    private volatile double _value;
    private volatile DateTimeOffset _updatedAt;

    public double Value => _value;
    public DateTimeOffset UpdatedAt => _updatedAt;

    public void Update(double value, DateTimeOffset timestamp)
    {
        _value = value;
        _updatedAt = timestamp;
    }
}
```

**Why NOT a struct:** ConcurrentDictionary with value-type entries requires `AddOrUpdate` with closures for atomic update. A reference-type entry lets us mutate in-place after `GetOrAdd`, which is lock-free for the common path. This is the exact pattern `DeviceUnreachabilityTracker.DeviceState` uses.

**Why NOT `Interlocked` for double:** `Interlocked.Exchange(ref double, ...)` exists in .NET but adds complexity. The pipeline has `[DisallowConcurrentExecution]` per device, so a given slot is written by at most one thread at a time. Volatile is sufficient for visibility, and torn reads on `double` are not possible on x64 (64-bit aligned). Defensive `Interlocked` could be added later if multi-writer becomes a concern.

### 2. Routing Index (Lookup Structure)

| Decision | Type | Namespace | Why |
|----------|------|-----------|-----|
| Routing index | `volatile FrozenDictionary<string, IReadOnlyList<TenantMetricRoute>>` | `System.Collections.Frozen` | Exact same pattern as `OidMapService._map` and `DeviceRegistry._byIpPort`. Built once on config load, atomically swapped on reload. O(1) lookup per sample. |
| Composite key | `string` formatted as `"{ip}:{port}:{metricName}"` | N/A | Matches `DeviceRegistry.IpPortKey()` pattern. String key avoids tuple allocation per lookup. Case-insensitive via `StringComparer.OrdinalIgnoreCase`. |
| Route target | `record TenantMetricRoute(string TenantId, int Priority, string SlotKey)` | N/A | Immutable record. SlotKey is the ConcurrentDictionary key for the MetricSlot. Priority is stored for future consumer ordering. |

**Atomic swap pattern (already proven in codebase):**

```csharp
// Field declaration
private volatile FrozenDictionary<string, IReadOnlyList<TenantMetricRoute>> _routingIndex;

// Rebuild on config reload (called from watcher service under SemaphoreSlim)
public void Rebuild(TenantVectorConfig config)
{
    var builder = new Dictionary<string, List<TenantMetricRoute>>(StringComparer.OrdinalIgnoreCase);
    // ... populate from config ...

    // Freeze lists, then freeze dictionary
    var frozen = builder.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<TenantMetricRoute>)kv.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase)
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    _routingIndex = frozen; // volatile write = atomic swap
}
```

**Why FrozenDictionary and NOT ImmutableDictionary:**
- FrozenDictionary reads are ~43-69% faster than standard Dictionary (per benchmarks). ImmutableDictionary reads are *slower* than standard Dictionary.
- We never mutate the index -- we rebuild and swap. FrozenDictionary is purpose-built for this "build once, read many" pattern.
- Already proven in this codebase with 3 separate services.

**Why NOT `ImmutableInterlocked.Update()`:**
- `ImmutableInterlocked` is designed for CAS-loop updates to `ImmutableDictionary` fields. We don't need CAS because rebuilds are serialized by the watcher's `SemaphoreSlim` (see `OidMapWatcherService._reloadLock`).
- Volatile write is sufficient when writes are serialized. This is the exact pattern used by `OidMapService.UpdateMap()` and `DeviceRegistry.ReloadAsync()`.

### 3. Configuration Model

| Decision | Type | Namespace | Why |
|----------|------|-----------|-----|
| Config deserialization | `System.Text.Json` with `JsonSerializerOptions` | `System.Text.Json` | Same as `OidMapWatcherService` and `DeviceWatcherService`. Already in BCL, already the project standard. |
| Config binding | POCO classes with `required` properties and `[Required]` validation | `System.ComponentModel.DataAnnotations` | Matches existing `DevicesOptions` / `DeviceOptions` pattern with `Microsoft.Extensions.Options.DataAnnotations`. |
| Config source | Separate ConfigMap key, deserialized directly in watcher | N/A | Matches `OidMapWatcherService` pattern (reads JSON from ConfigMap data key, deserializes, calls service update method). Keeps tenant config independent of main appsettings. |

**Config shape (`tenantvector.json`):**

```json
{
  "tenants": [
    {
      "id": "tenant-alpha",
      "priority": 1,
      "metrics": [
        { "device": "switch-01", "metricName": "ifInOctets" },
        { "device": "switch-01", "metricName": "ifOutOctets" }
      ]
    }
  ]
}
```

### 4. MediatR Fan-Out Behavior

| Decision | Type | Namespace | Why |
|----------|------|-----------|-----|
| Behavior type | `IPipelineBehavior<TNotification, TResponse>` | `MediatR` | Same open generic pattern as `OidResolutionBehavior`. Registered via `cfg.AddOpenBehavior()` in `ServiceCollectionExtensions`. |
| Pipeline position | After `OidResolutionBehavior` (5th behavior, innermost before handler) | N/A | Needs resolved `MetricName` to perform routing lookup. Must NOT short-circuit -- always calls `next()`. |
| Registration | `cfg.AddOpenBehavior(typeof(TenantFanOutBehavior<,>));` after OidResolution line | N/A | Single line addition to existing `AddMediatR` block. |

**Pipeline order after addition:**

```
Logging -> Exception -> Validation -> OidResolution -> TenantFanOut -> OtelMetricHandler
```

The fan-out behavior reads the routing index (volatile FrozenDictionary read), writes to matching MetricSlots (ConcurrentDictionary update), then calls `next()`. Zero allocation in the hot path if no tenants match (just a dictionary lookup returning false).

### 5. Watcher Service for Config Reload

| Decision | Type | Namespace | Why |
|----------|------|-----------|-----|
| Watcher | `BackgroundService` watching K8s ConfigMap | `Microsoft.Extensions.Hosting` | Clone of `OidMapWatcherService` pattern. Same watch loop, same `SemaphoreSlim` serialization, same error handling. |
| Reload target | Routing index service (rebuild + volatile swap) + slot cleanup | N/A | On reload: rebuild FrozenDictionary, swap, prune orphaned slots from ConcurrentDictionary. |

---

## What NOT to Add (and Why)

| Rejected Option | Why Not |
|-----------------|---------|
| **Redis / external cache** | In-memory is correct. Slots are per-pod, written and read locally. No cross-pod sharing needed. If multi-pod query is needed later, that is an API concern, not a data structure concern. |
| **`System.Collections.Immutable`** | `ImmutableDictionary` is slower for reads than `FrozenDictionary`. `ImmutableInterlocked` adds CAS-loop complexity we don't need (writes are serialized). |
| **`ReaderWriterLockSlim`** | Volatile + FrozenDictionary swap is lock-free for readers. RWLS would add contention where none exists. |
| **`Channel<T>` for fan-out** | Fan-out is synchronous (write a value to a slot). No queuing needed. Channel is for producer-consumer decoupling (like trap ingestion), not for value updates. |
| **`System.Reactive` / Rx.NET** | Massive dependency for a problem solved by a dictionary write. |
| **Any new NuGet package** | All types needed are in BCL or existing dependencies. Adding a package for this would be over-engineering. |
| **`lock` statement** | Volatile swap + ConcurrentDictionary gives lock-free reads on the hot path. `lock` would serialize pipeline throughput. |
| **Persistent storage / SQLite** | This is a hot-path data structure. Values are ephemeral (overwritten every poll cycle). Persistence adds latency for zero benefit. |

---

## Existing Dependencies (No Changes)

All existing packages remain at current versions. No upgrades needed.

| Package | Version | Role in This Feature |
|---------|---------|---------------------|
| `MediatR` | 12.5.0 | `IPipelineBehavior` for fan-out behavior |
| `KubernetesClient` | 18.0.13 | ConfigMap watch for `tenantvector.json` |
| `Microsoft.Extensions.Options.DataAnnotations` | 9.0.0 | Config validation |
| `Microsoft.Extensions.Hosting` | 9.0.0 | `BackgroundService` for watcher |

No new `PackageReference` entries in `.csproj`.

---

## Pattern Reuse Map

| Pattern | Existing Example | New Application |
|---------|-----------------|-----------------|
| Volatile FrozenDictionary swap | `OidMapService._map`, `DeviceRegistry._byIpPort` | Routing index `_routingIndex` |
| ConcurrentDictionary with reference-type value | `DeviceUnreachabilityTracker._state` | `TenantMetricSlotStore._slots` |
| Volatile fields on inner class | `DeviceState._count`, `DeviceState._isUnreachable` | `MetricSlot._value`, `MetricSlot._updatedAt` |
| K8s ConfigMap watcher + SemaphoreSlim | `OidMapWatcherService` | `TenantVectorWatcherService` |
| Open generic pipeline behavior | `OidResolutionBehavior<,>` | `TenantFanOutBehavior<,>` |
| Composite string key for dictionary | `DeviceRegistry.IpPortKey("{ip}:{port}")` | `"{ip}:{port}:{metricName}"` routing key |

**This is not a technology decision -- it is a pattern replication decision.** Every building block already exists in the codebase. The milestone creates new service classes using established patterns.

---

## Confidence Assessment

| Area | Confidence | Reason |
|------|------------|--------|
| ConcurrentDictionary for slots | HIGH | Proven in `LivenessVectorService`, `DeviceUnreachabilityTracker`, `SnmpMetricFactory` |
| FrozenDictionary for routing index | HIGH | Proven in `OidMapService`, `DeviceRegistry`. BCL type since .NET 8, unchanged in .NET 9 |
| Volatile swap pattern | HIGH | Used in 5+ locations in codebase. Well-understood .NET memory model behavior on x64 |
| MediatR open behavior | HIGH | 4 existing behaviors demonstrate the pattern |
| K8s ConfigMap watcher | HIGH | 2 existing watchers demonstrate the pattern |
| No new packages needed | HIGH | All types in BCL or existing dependencies |

---

## Sources

- Codebase: `DeviceRegistry.cs` -- volatile FrozenDictionary swap pattern (lines 19-20, 141-143)
- Codebase: `OidMapService.cs` -- volatile FrozenDictionary swap with diff logging (lines 20, 62-63)
- Codebase: `DeviceUnreachabilityTracker.cs` -- ConcurrentDictionary with reference-type inner class + volatile fields (lines 17, 42-74)
- Codebase: `LivenessVectorService.cs` -- ConcurrentDictionary for timestamp slots (lines 11, 15-17)
- Codebase: `OidResolutionBehavior.cs` -- open generic pipeline behavior pattern
- Codebase: `OidMapWatcherService.cs` -- K8s ConfigMap watch + SemaphoreSlim reload serialization
- Codebase: `ServiceCollectionExtensions.cs` -- behavior registration order (lines 336-341)
- [FrozenDictionary benchmarks](https://dotnetbenchmarks.com/benchmark/1005) -- 43-69% faster reads vs Dictionary
- [Volatile vs Interlocked vs Lock](https://code-maze.com/csharp-volatile-interlocked-lock/) -- memory model semantics
- [High-Performance Dictionary Strategies in .NET](https://medium.com/@rserit/high-performance-dictionary-strategies-in-net-immutable-and-frozen-dictionary-c54ffb05f8ce) -- FrozenDictionary read perf for concurrent scenarios

---
*Stack research for: Priority Vector Data Layer*
*Researched: 2026-03-10*
