# Project Research Summary

**Project:** SnmpCollector -- Priority Vector Data Layer
**Domain:** Stateful in-memory fan-out data layer for SNMP metric tenant prioritization
**Researched:** 2026-03-10
**Confidence:** HIGH

## Executive Summary

The Priority Vector Data Layer is a pure pattern-replication milestone. Every building block -- volatile FrozenDictionary swap, ConcurrentDictionary with reference-type values, MediatR open generic behaviors, K8s ConfigMap watchers with SemaphoreSlim serialization -- already exists in the SnmpCollector codebase with proven implementations. Zero new NuGet packages are required. The work is creating new service classes that apply established patterns to a new domain: routing incoming SNMP samples to per-tenant metric slots based on a priority-ordered configuration.

The recommended approach is bottom-up construction: config models first, then core data types (slots, routing index), then the MediatR fan-out behavior that bridges the existing pipeline to the new data layer, and finally the ConfigMap watcher for hot-reload. This order maximizes unit-testability at each step and defers K8s integration to the end. The architecture adds a single new behavior to the MediatR pipeline (position 5, after OidResolution) that performs a FrozenDictionary lookup and writes to tenant slots as a side-effect, then unconditionally calls `next()` so existing OTel export is never disrupted.

The primary risk is the cross-dependency between the OID map and the tenant vector routing index: if OID-to-metric-name mappings change, the routing index silently becomes stale. This must be addressed at design time by subscribing the routing index rebuild to OID map change events. The secondary risk is the fan-out behavior accidentally killing the existing OTel export pipeline if it throws an exception -- prevented by mandatory internal try/catch with unconditional `next()` invocation. Both risks have clear, proven mitigation patterns.

## Key Findings

### Recommended Stack

No new dependencies. All data structures use .NET 9 BCL types already proven in this codebase. This is a pattern replication decision, not a technology decision.

**Core technologies:**
- `FrozenDictionary<RoutingKey, IReadOnlyList<TenantSlotRef>>`: routing index -- volatile swap for lock-free reads, matching OidMapService and DeviceRegistry
- `ConcurrentDictionary` or direct slot references in `TenantVector`: slot storage -- matching LivenessVectorService and DeviceUnreachabilityTracker patterns
- `Volatile.Write/Read` on slot fields: thread-safe value cell updates without locks
- `MediatR IPipelineBehavior<,>`: fan-out behavior -- 5th open generic behavior following the established registration pattern
- `BackgroundService` + K8s ConfigMap watch: structural clone of OidMapWatcherService for tenantvector.json hot-reload

### Expected Features

**Must have (10 table stakes):**
- TS-01: Config model (`TenantVectorOptions` POCO hierarchy)
- TS-02: ConfigMap watcher (`TenantVectorWatcherService` watching `simetra-tenantvector`)
- TS-03: Tenant registry with priority groups (ordered by priority, grouped tenants)
- TS-04: Metric slot value cells (single-cell, last-writer-wins, in-place overwrite)
- TS-05: Routing index -- `(ip, port, metric_name)` fan-out via FrozenDictionary
- TS-06: Pipeline behavior (`TenantVectorFanOutBehavior`, position 5 after OidResolution)
- TS-07: Port resolution via DeviceName lookup (no changes to SnmpOidReceived)
- TS-08: Unknown metric filtering (skip `MetricName == "Unknown"`)
- TS-09: Heartbeat filtering (skip `IsHeartbeat == true`)
- TS-10: Atomic rebuild on config change (FrozenDictionary volatile swap)

**Should have (4 differentiators):**
- D-01: Config validation at load time (unique IDs, valid metric references, valid ranges)
- D-02: Structured diff logging on reload (added/removed/changed tenants)
- D-04: Pipeline counter `snmp_tenantvector_routed_total` for observability
- D-05: Thread-safe value cells via immutable record + Volatile.Write swap

**Defer:**
- D-03: Diagnostic snapshot accessor -- no consumer exists yet; add when evaluation engine is built
- AF-01 through AF-08: Decision logic, staleness detection, external APIs, Prometheus export of tenant data, tenant-specific polling, history buffers, cross-tenant dedup, persistence -- all explicitly out of scope

### Architecture Approach

The fan-out behavior inserts as position 5 in the MediatR pipeline (after OidResolution sets MetricName, before OtelMetricHandler). It performs a single FrozenDictionary lookup on the routing index, writes to matching tenant slots via Volatile.Write, and always calls `next()`. The routing index and tenant registry are singletons rebuilt atomically via volatile FrozenDictionary swap when the ConfigMap changes. Only 3 existing files are modified: `ServiceCollectionExtensions.cs` (~25 lines), `Program.cs` (~15 lines for local-dev fallback). All other existing code is untouched.

**Major components:**
1. **Configuration Models** (`Configuration/`) -- POCOs for tenantvector.json deserialization + IValidateOptions validator
2. **Core Data Types** (`Pipeline/`) -- `RoutingKey` record struct, `TenantSlot` mutable cell with Volatile semantics, `Tenant` runtime model, `TenantVector` priority-grouped container
3. **TenantVectorRegistry** (`Pipeline/`) -- singleton holding FrozenDictionary routing index + volatile swap; `ITenantVectorRegistry` interface with `TryRoute()` and `Reload()`
4. **TenantVectorFanOutBehavior** (`Pipeline/Behaviors/`) -- MediatR behavior wiring pipeline to registry
5. **TenantVectorWatcherService** (`Services/`) -- BackgroundService watching `simetra-tenantvector` ConfigMap
6. **K8s ConfigMap** (`deploy/`) -- `simetra-tenantvector` ConfigMap manifest + local dev `tenantvector.json`

### Critical Pitfalls

1. **OID map reload desync (Pitfall 1, CRITICAL)** -- OID map rename changes metric names but routing index still uses old names, causing silent data loss. **Prevent:** Subscribe routing index rebuild to OID map change events; rebuild on ANY config source change; log routing misses at Warning level.

2. **Fan-out exception kills OTel export (Pitfall 8, CRITICAL)** -- ExceptionBehavior catches fan-out errors and short-circuits the entire downstream chain including OtelMetricHandler. **Prevent:** Fan-out behavior MUST catch its own exceptions internally and ALWAYS call `next()` unconditionally. Add integration test proving OtelMetricHandler fires even when fan-out throws.

3. **Wrong behavior registration order (Pitfall 3, CRITICAL)** -- Registering fan-out before OidResolution means MetricName is null, all routing lookups miss silently. **Prevent:** Register AFTER OidResolutionBehavior (position 5); add guard clause for null/Unknown MetricName; add unit test for ordering.

4. **Torn reads on slot value+timestamp (Pitfall 4, MODERATE)** -- Two-field write (value, timestamp) is not atomic; concurrent reader sees inconsistent pair. **Prevent:** Use immutable record + atomic reference swap via Volatile.Write from day one. Do NOT use mutable two-field approach.

5. **Per-pod state divergence (Pitfall 7, MODERATE)** -- 3 replicas hold different slot values; no cross-pod sync. **Prevent:** Decide leader-only vs all-pods upfront. If leader-only, gate fan-out behind IsLeader check. Document the choice.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Config Models and Validation
**Rationale:** Pure foundation with zero dependencies on existing code. Enables all downstream phases. Fully unit-testable in isolation.
**Delivers:** `TenantVectorOptions`, `TenantOptions`, `TenantMetricOptions` POCOs; `TenantVectorOptionsValidator`; unit tests for validation rules.
**Addresses:** TS-01, D-01
**Avoids:** No pitfalls -- pure model classes.

### Phase 2: Core Data Types and Registry
**Rationale:** Builds the in-memory data structures that all runtime behavior depends on. Can be tested with hardcoded config, no K8s dependency. Must design slot atomicity correctly from the start (Pitfall 4).
**Delivers:** `RoutingKey`, `TenantSlot`, `Tenant`, `TenantVector`, `ITenantVectorRegistry`, `TenantVectorRegistry` with FrozenDictionary routing index and volatile swap. Structured diff logging on rebuild. Orphaned slot cleanup.
**Addresses:** TS-03, TS-04, TS-05, TS-10, D-02, D-05
**Avoids:** Pitfall 4 (torn reads -- use immutable slot swap), Pitfall 5 (rebuild blocking -- atomic swap pattern), Pitfall 6 (orphaned slots -- compute diff on rebuild)

### Phase 3: Pipeline Integration (Fan-Out Behavior)
**Rationale:** Connects the new data layer to the existing pipeline. This is where the critical ordering and exception-safety pitfalls live. Must be implemented with defensive patterns from the first line of code.
**Delivers:** `TenantVectorFanOutBehavior`, DI registration in `ServiceCollectionExtensions.cs`, guard clauses for heartbeat/Unknown filtering, port resolution via DeviceName, internal try/catch, pipeline counter.
**Addresses:** TS-06, TS-07, TS-08, TS-09, D-04
**Avoids:** Pitfall 3 (wrong order -- register after OidResolution), Pitfall 8 (exception kills export -- internal catch + unconditional next()), Pitfall 10 (heartbeat pollution), Pitfall 12 (Unknown pollution)

### Phase 4: ConfigMap Watcher and Local Dev
**Rationale:** Last production code phase because it introduces K8s infrastructure dependency. The data layer and pipeline integration are already testable with hardcoded config. The watcher is a structural clone of OidMapWatcherService.
**Delivers:** `TenantVectorWatcherService`, `simetra-tenantvector` ConfigMap manifest, local-dev fallback in `Program.cs`, DI registration for watcher, OID map change notification subscription.
**Addresses:** TS-02
**Avoids:** Pitfall 1 (OID map desync -- subscribe to OidMapService changes and trigger routing rebuild), Pitfall 5 (rebuild blocking -- SemaphoreSlim on build only)

### Phase 5: E2E Validation and K8s Deployment
**Rationale:** Final integration verification in the actual K8s environment with real ConfigMaps and multi-replica deployment.
**Delivers:** E2E test scenarios, ConfigMap applied, watcher picks up config, samples route to tenant slots, Prometheus counter incrementing.
**Addresses:** Validation of all table stakes working together.
**Avoids:** Pitfall 7 (per-pod divergence -- validate leader-only/all-pods decision in multi-replica deployment)

### Phase Ordering Rationale

- **Bottom-up by dependency:** Config models have zero deps, data types depend on models, registry depends on data types, behavior depends on registry, watcher depends on registry. Each phase is testable before the next begins.
- **Defer K8s to the end:** Phases 1-3 are pure C# with no K8s dependency. This means faster iteration cycles and no need for a running cluster during core development.
- **Critical pitfalls concentrated in Phase 3:** The fan-out behavior is where most things can go wrong (ordering, exceptions, filtering). Isolating it as its own phase forces focused attention on defensive patterns.
- **OID map cross-dependency in Phase 4:** The watcher phase is where the OID map desync pitfall (Pitfall 1) must be addressed, because the watcher is the component that triggers routing index rebuilds.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Pipeline Integration):** Verify exact MediatR behavior registration semantics for open generics. Confirm that position 5 means "inside OidResolution's next() call." Write a spike test before committing to the design.
- **Phase 4 (ConfigMap Watcher):** Research the OID map change notification mechanism. OidMapService currently has no event/callback for map updates. Either add one or use a version-stamp polling approach.
- **Phase 5 (E2E):** Requires leader-only vs all-pods decision (Pitfall 7) to be resolved before testing. This is an architecture decision, not a research gap.

Phases with standard patterns (skip additional research):
- **Phase 1 (Config Models):** Established POCO + IValidateOptions pattern. No research needed.
- **Phase 2 (Core Data Types):** FrozenDictionary, ConcurrentDictionary, Volatile patterns all proven in codebase. No research needed.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Zero new packages. All BCL types proven in 3+ locations in the codebase. |
| Features | HIGH | Derived from authoritative spec (tenantvector.txt) and direct codebase analysis. Clear table-stakes/differentiator/anti-feature boundaries. |
| Architecture | HIGH | Every pattern (FrozenDictionary swap, MediatR behavior, ConfigMap watcher) is a structural clone of existing code. Build order validated against actual dependency graph. |
| Pitfalls | HIGH | All pitfalls verified against source code. Concurrency model analyzed from actual threading paths (Quartz pool, Channel consumer, async pipeline). |

**Overall confidence:** HIGH

### Gaps to Address

- **Leader-only vs all-pods decision (Pitfall 7):** Not resolved by research. Requires a product-level decision about whether the tenant vector is consumed only on the leader pod or on all pods. This decision affects fan-out gating, memory usage on followers, and leadership transition behavior. **Must be decided before Phase 3 implementation.**

- **OID map change notification mechanism (Pitfall 1):** OidMapService currently has no event or callback for map updates. The routing index rebuild needs a trigger. Options: (a) add an event to OidMapService, (b) version-stamp polling, (c) have both watchers funnel through a centralized reload orchestrator. **Must be decided during Phase 4 planning.**

- **RoutingKey design: record struct vs composite string:** STACK.md recommends composite string key `"{ip}:{port}:{metricName}"`. ARCHITECTURE.md recommends `readonly record struct RoutingKey`. The record struct is cleaner (proper equality, no formatting ambiguity) and avoids per-lookup string allocation. FrozenDictionary with record struct keys works correctly. **Recommendation: use record struct as ARCHITECTURE.md specifies.** The string key suggestion in STACK.md is superseded.

- **Value type in MetricSlot:** FEATURES.md specifies `object` (int, long, uint, ulong, string) to match SNMP type diversity. STACK.md specifies `double`. ARCHITECTURE.md specifies `double` + optional `string?`. **Recommendation: use double + string? as ARCHITECTURE.md specifies.** Most SNMP numeric types fit in double; string covers DisplayString OIDs.

## Sources

### Primary (HIGH confidence)
- `Docs/tenantvector.txt` -- authoritative design specification for priority vector
- Direct codebase analysis of all referenced source files (14 files in ARCHITECTURE.md, 10 in FEATURES.md, 10 in STACK.md, 17 in PITFALLS.md)

### Key Source Files Referenced
- `DeviceRegistry.cs` -- volatile FrozenDictionary swap, dual-dictionary pattern
- `OidMapService.cs` -- volatile FrozenDictionary swap, diff logging
- `DeviceUnreachabilityTracker.cs` -- ConcurrentDictionary with reference-type inner class + volatile fields
- `LivenessVectorService.cs` -- ConcurrentDictionary for timestamp slots
- `OidResolutionBehavior.cs` -- open generic behavior pattern, MetricName enrichment
- `OidMapWatcherService.cs` -- ConfigMap watch + SemaphoreSlim reload serialization
- `ServiceCollectionExtensions.cs` -- behavior registration order, DI patterns
- `ExceptionBehavior.cs` -- catch-all that can swallow downstream errors
- `MetricPollJob.cs` -- concurrent execution model, DeviceName/AgentIp construction
- `SnmpOidReceived.cs` -- message contract (no Port field)

### External References
- [FrozenDictionary benchmarks](https://dotnetbenchmarks.com/benchmark/1005) -- 43-69% faster reads vs Dictionary
- [Volatile vs Interlocked vs Lock](https://code-maze.com/csharp-volatile-interlocked-lock/) -- memory model semantics

---
*Research completed: 2026-03-10*
*Ready for roadmap: yes*
