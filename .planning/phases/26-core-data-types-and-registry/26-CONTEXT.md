# Phase 26: Core Data Types and Registry - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

In-memory data structures that hold tenant metric slots in priority order with a lock-free routing index. The registry is the central data layer consumed by the fan-out behavior (Phase 27) and future evaluation engine. No external API, no Prometheus export, no evaluation logic.

</domain>

<decisions>
## Implementation Decisions

### MetricSlot Value Model

- MetricSlot is an **immutable record** with: `double Value`, `string? StringValue`, `DateTimeOffset UpdatedAt`
- Slot is pure data (value + timestamp only) — no routing key on the record
- **Both fields always set** on every write: gauge sets Value + StringValue=null; info sets Value=0 + StringValue
- Timestamp uses `DateTimeOffset.UtcNow` (wall clock)
- "No value yet" representation: Claude's discretion (null reference or sentinel timestamp)

### MetricSlotHolder

- **MetricSlotHolder** is the runtime object that wraps a volatile reference to an immutable MetricSlot
- Holder carries config metadata: Ip, Port, MetricName, IntervalSeconds (for future staleness detection)
- Exposes `WriteValue(double value, string? stringValue)` — encapsulates record creation + Volatile.Write
- Exposes `ReadSlot()` — encapsulates Volatile.Read, returns MetricSlot? snapshot
- Named `MetricSlotHolder` (explicit about its purpose)

### Priority Group Ordering

- Runtime structure: `IReadOnlyList<PriorityGroup>` sorted by priority value (ascending = highest priority first)
- `PriorityGroup` is a **named record**: `record PriorityGroup(int Priority, IReadOnlyList<Tenant> Tenants)`
- Within a group, tenants maintain **insertion order** (natural iteration of config array) — no explicit index needed
- **Tenant** is a **sealed class** with readonly properties: Id, Priority, list of MetricSlotHolders
- Registry exposes sorted groups only — **no flat tenant-by-ID lookup** (groups only)
- Registry exposes `TenantCount` and `SlotCount` properties (computed at build time)
- Registry exposes `IReadOnlyList<PriorityGroup>` for iteration — evaluation engine handles traversal logic

### Reload Behavior

- **Carry over values** on reload: if a metric (matched by tenant_id + ip + port + metric_name) exists in both old and new config, the new MetricSlotHolder gets the old slot's current value
- **Writes to old holders during swap are fine** — orphaned old holders receive stale writes that are discarded; next sample uses new routing index. Lock-free, no retry logic.
- **Registry singleton with Reload()** method — `TenantVectorRegistry` is a DI singleton, `Reload(TenantVectorOptions)` rebuilds internal state
- **Synchronous** `Reload()` — CPU-bound only (FrozenDictionary creation, value carry-over), matches `OidMapService.UpdateMap()` pattern
- **Empty by default** on startup — registry starts with empty groups and empty routing index; fan-out finds no matches until first Reload()
- **Structured diff logging** on reload — log tenants added/removed/unchanged, slots carried over vs fresh

### Routing Index Design

- **Case-insensitive** comparison for the composite key (OrdinalIgnoreCase) — safe for IPs and any future DNS names
- **RoutingKey** is a `readonly record struct` implementing `IEquatable<RoutingKey>` — value type, no heap allocation per key
- Routing index value: `IReadOnlyList<MetricSlotHolder>` per routing key (built as List<>, exposed as IReadOnlyList<>)
- **Two volatile fields on registry** (groups + routing index) — brief inconsistency during swap is acceptable since fan-out only uses routing index and evaluation only uses groups
- Registry exposes `TryRoute(string ip, int port, string metricName, out IReadOnlyList<MetricSlotHolder> holders)` — encapsulates routing index behind clean API
- Registry registered via **ITenantVectorRegistry interface** — follows IOidMapService pattern, testable with mocks

</decisions>

<specifics>
## Specific Ideas

- MetricSlotHolder.WriteValue() pattern mirrors the "encapsulate volatile write" approach — callers never touch the volatile field directly
- ReadSlot()/WriteValue() symmetry makes the thread-safety contract explicit in the API surface
- Value carry-over matches by (tenant_id + ip + port + metric_name) — tenant renames lose old values (by design)
- Diff logging follows OidMapService pattern: tenants added/removed, slots carried over count

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 26-core-data-types-and-registry*
*Context gathered: 2026-03-10*
