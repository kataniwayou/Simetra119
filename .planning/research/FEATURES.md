# Feature Landscape: Priority Vector Data Layer

**Domain:** Stateful in-memory data layer for SNMP metric prioritization
**Researched:** 2026-03-10
**Confidence:** HIGH -- derived from tenantvector.txt spec and codebase analysis of existing pipeline

---

## Table Stakes

Features the data layer MUST have for downstream consumers (future decision logic) to be viable.

### TS-01: Tenant Vector Configuration Model

**What:** Strongly-typed C# model for tenantvector.json: `TenantVectorConfig` containing a list of `TenantConfig` objects, each with `id`, `priority`, `graceMultiplier`, and a list of `MetricSlotConfig` objects with `ip`, `port`, `oid`, `source` (poll/trap), `intervalSeconds`.
**Why Expected:** Without a deserialization target, the config cannot be loaded. This is the foundation everything else depends on.
**Complexity:** Low
**Depends On:** Nothing -- pure model classes

### TS-02: TenantVectorWatcherService (ConfigMap Hot-Reload)

**What:** BackgroundService that watches the `simetra-tenantvector` ConfigMap via K8s API and triggers a full rebuild of the in-memory data structures on change. Must follow the exact same pattern as OidMapWatcherService: initial load, then watch loop with 5s reconnect on error, SemaphoreSlim serialization, graceful handling of malformed JSON / missing keys / null deserialization.
**Why Expected:** The existing system uses ConfigMap watchers for all dynamic configuration (OidMap, Devices). A third watcher for the tenant vector is the established pattern. Without hot-reload, any config change requires a pod restart.
**Complexity:** Medium
**Depends On:** TS-01 (config model), existing K8s client infrastructure (IKubernetes injection)

### TS-03: Tenant Registry with Priority Groups

**What:** Singleton service that holds the fully materialized tenant vector in memory. Internal structure: an ordered list of priority groups, where each group contains one or more tenants at the same priority level. Tenants within a group are ordered by their position in the config array. Each tenant contains its metric slots as value cells.
**Why Expected:** This is the core data structure the spec defines. Without it, there is nothing for decision logic to evaluate.
**Complexity:** Medium
**Depends On:** TS-01 (config model), TS-02 (watcher feeds it)

### TS-04: Metric Slot Value Cell

**What:** Per-tenant, per-metric mutable cell holding: `value` (object -- int, long, uint, ulong, string depending on SNMP type), `updated_at` (DateTimeOffset), and the slot's address key `(ip, port, oid)`. The cell is overwritten in-place on each sample arrival. No history buffer.
**Why Expected:** The spec explicitly defines single-cell semantics: "When a new sample arrives, the value and updated_at are overwritten in place. The previous value is discarded."
**Complexity:** Low
**Depends On:** TS-03 (cells live inside tenants in the registry)

### TS-05: Routing Index -- (ip, port, metric_name) Fan-Out

**What:** A dictionary mapping `(ip, port, metric_name)` to a list of `(tenant_id, metric_slot_ref)` pairs. Built at load time from all metric slots across all tenants. When a sample arrives matching a key, every slot in the list gets its value cell updated. This is the fan-out mechanism.
**Why Expected:** Without routing, incoming samples cannot reach tenant metric slots. This is the bridge between the existing pipeline and the tenant vector.
**Complexity:** Medium
**Depends On:** TS-03 (registry provides the tenants to index), TS-06 (pipeline integration feeds samples)

**Key design note on the routing key:** The spec uses `(ip, port, oid)` but the milestone context says `(ip, port, metric_name)`. The spec also says "only OID-map-resolved metrics allowed (Unknown filtered out)." Using `metric_name` (the OID-resolved name) as the routing key rather than raw OID is the correct choice because:
1. It naturally filters out Unknown OIDs -- if an OID does not resolve, it has no metric_name to match.
2. Config authors think in metric names ("obp_r1_power_L1"), not raw OIDs ("1.3.6.1.4.1.47477.10.21.1.3.4.0").
3. It decouples the tenant vector from OID map changes -- if an OID is remapped to a new name, the tenant vector config uses the new name.

### TS-06: Pipeline Integration -- TenantVectorBehavior or Post-Handler Hook

**What:** A mechanism to feed resolved SNMP samples into the routing index. Two options:

- **Option A (MediatR behavior):** A new `IPipelineBehavior` that runs after OidResolutionBehavior, reads `MetricName`, `AgentIp`, and the device's port (from DeviceRegistry lookup), and writes to matching routing index slots. Always calls `next()` -- never short-circuits.
- **Option B (Post-handler notification):** OtelMetricHandler publishes a lightweight notification after processing; a separate handler writes to routing index.

**Recommendation: Option A (pipeline behavior)** because:
- It follows the existing pattern (behaviors are the pipeline's extension point).
- It can access `SnmpOidReceived` properties directly (no new message type needed).
- It runs before the terminal handler, so the value cell is updated even if OtelMetricHandler has issues.
- The behavior order becomes: Logging -> Exception -> Validation -> OidResolution -> **TenantVectorRouting** -> OtelMetricHandler.

**Why Expected:** Without pipeline integration, the tenant vector is an empty data structure that never receives data.
**Complexity:** Medium
**Depends On:** TS-05 (routing index to write into), existing MediatR pipeline, DeviceRegistry (for port lookup)

### TS-07: Port Resolution for Routing Key

**What:** The `SnmpOidReceived` message currently carries `AgentIp` (IPAddress) but NOT `Port`. The routing key requires `(ip, port, metric_name)`. Port must be resolved from DeviceRegistry via `TryGetByIpPort` or derived from the device name.

**Problem:** DeviceRegistry.TryGetByIpPort requires both IP and port -- but we only have IP from the message. The lookup is backwards: we need the port, but the lookup requires the port.

**Resolution approaches:**
1. **Use DeviceName as lookup key:** `SnmpOidReceived.DeviceName` is always set (validated by ValidationBehavior). Use `DeviceRegistry.TryGetDeviceByName(deviceName)` to get `DeviceInfo.Port`. This is reliable because DeviceName is set by both the poll path (from JobDataMap) and the trap path (from community string).
2. **Add Port to SnmpOidReceived:** Enrich the message with port at creation time (MetricPollJob already knows the port; ChannelConsumerService can resolve it).

**Recommendation: Option 1 (DeviceName lookup)** because it requires zero changes to existing code. The behavior does `_deviceRegistry.TryGetDeviceByName(msg.DeviceName)` and reads `.Port` from the result.

**Why Expected:** Without port, the routing key is incomplete and cannot match config entries that specify port.
**Complexity:** Low
**Depends On:** Existing DeviceRegistry, TS-06 (the behavior that needs the port)

### TS-08: Unknown Metric Filtering

**What:** Samples where `MetricName == OidMapService.Unknown` must NOT be routed to the tenant vector. The spec says "only OID-map-resolved metrics allowed."
**Why Expected:** Routing unknown metrics would pollute tenant value cells with unidentifiable data. The routing index naturally handles this if keyed by metric_name -- no Unknown entries will exist in the index. But the behavior must explicitly check and skip before attempting a routing lookup.
**Complexity:** Low
**Depends On:** TS-06 (the behavior checks MetricName before routing)

### TS-09: Heartbeat Filtering

**What:** Heartbeat messages (`IsHeartbeat == true`) must NOT be routed to the tenant vector. Heartbeats are internal liveness signals, not real SNMP data.
**Why Expected:** The existing pipeline already skips heartbeats in OtelMetricHandler. The tenant vector behavior must do the same.
**Complexity:** Low
**Depends On:** TS-06 (the behavior checks IsHeartbeat before routing)

### TS-10: Atomic Rebuild on Config Change

**What:** When tenantvector.json changes (via ConfigMap watcher), the entire tenant registry, all value cells, and the routing index must be rebuilt atomically using the FrozenDictionary volatile-swap pattern. The old structure continues serving reads until the new one is fully built, then a single volatile write swaps in the new structure.
**Why Expected:** This is the established concurrency pattern in the codebase (OidMapService, DeviceRegistry both use it). Without atomic swap, concurrent reads during a rebuild could see partially constructed state.
**Complexity:** Medium
**Depends On:** TS-02 (watcher triggers rebuild), TS-03 (registry is what gets rebuilt), TS-05 (routing index is rebuilt alongside)

---

## Differentiators

Features that make the data layer robust and production-ready. Not strictly required for basic functionality, but strongly recommended.

### D-01: Config Validation at Load Time

**What:** When tenantvector.json is loaded, validate:
- All tenant IDs are unique within the vector
- All metric slot OIDs resolve to a known metric_name in the current OID map (warn if not)
- No duplicate `(ip, port, oid)` within a single tenant (error -- pointless)
- Priority values are valid integers
- Source is either "poll" or "trap"
- IntervalSeconds > 0
- IP addresses are parseable
- Port is in valid range (1-65535)
- Referenced devices exist in DeviceRegistry (warn if not -- device may be added later)

**Value Proposition:** Catches misconfiguration at load time with structured log output instead of silent misbehavior at runtime. Follows the pattern set by DeviceRegistry (throws on duplicate IP+Port).
**Complexity:** Medium
**Depends On:** TS-01 (model to validate), OidMapService (to verify OID resolution), DeviceRegistry (to verify device existence)

### D-02: Structured Diff Logging on Reload

**What:** When the tenant vector config is reloaded, log a structured diff: tenants added, tenants removed, tenants with changed metric slots. Follow the pattern established by OidMapService.UpdateMap which logs added/removed/changed entries.
**Value Proposition:** Operators can see exactly what changed without comparing config files. Critical for troubleshooting in production.
**Complexity:** Low
**Depends On:** TS-10 (rebuild path where diff is computed)

### D-03: Diagnostic Snapshot Accessor

**What:** A read-only accessor on the tenant registry that returns the current state: number of tenants, number of groups, total metric slots, routing index size, per-tenant slot count with latest `updated_at` values. NOT an HTTP API -- just a method that health checks or diagnostic logging can call.
**Value Proposition:** Makes the data layer inspectable without an external API. Future health checks can verify "are metric slots receiving updates?" without adding an HTTP endpoint.
**Complexity:** Low
**Depends On:** TS-03 (registry to inspect)

### D-04: Tenant Vector Pipeline Counter

**What:** An OTel counter `snmp_tenantvector_routed_total` that increments each time a sample is successfully routed to at least one tenant metric slot. Optionally with a `tenant_count` attribute showing how many slots were updated (fan-out degree). Follows the existing PipelineMetricService pattern.
**Value Proposition:** Provides observability into whether the tenant vector is receiving data. A zero-increment counter means either no matching samples or a routing bug.
**Complexity:** Low
**Depends On:** TS-06 (the behavior that does the routing increments the counter)

### D-05: Thread-Safe Value Cell Updates

**What:** Ensure that concurrent sample arrivals for the same `(ip, port, metric_name)` do not corrupt value cells. Since poll jobs and trap processing run concurrently on different threads, two samples for the same metric could arrive simultaneously. The value cell write (value + updated_at) must be atomic from the reader's perspective.

Options:
- **Lock per cell:** Fine-grained but many locks for many cells.
- **Interlocked + immutable cell record:** Replace the entire cell record atomically via `Volatile.Write` or `Interlocked.Exchange`. Since cells are small (value + timestamp), creating a new immutable cell and swapping it in is cheap.
- **Accept last-writer-wins without atomicity:** Value and updated_at are two separate writes; a reader might see new value with old timestamp. In practice, this is unlikely to matter since both are written in the same method call and the reader (future decision logic) is not time-critical.

**Recommendation: Immutable cell record with Volatile.Write.** Create a `readonly record struct MetricCell(object Value, DateTimeOffset UpdatedAt)` and swap it atomically. Clean, zero-lock, matches the project's FrozenDictionary philosophy.
**Complexity:** Low
**Depends On:** TS-04 (cell design)

---

## Anti-Features

Things to deliberately NOT build in this milestone.

### AF-01: Decision Logic / Evaluation Engine

**What:** Do NOT implement the priority-group evaluation cascade ("Group 1 before Group 2 before Group 3"). Do NOT implement any decision-making based on metric values. Do NOT implement "is this tenant clear?" logic.
**Why Avoid:** The spec describes evaluation as a separate concern. This milestone builds the DATA LAYER only -- the stateful substrate that holds metric values. Decision logic is a future milestone.
**What to Do Instead:** Build the data layer so decision logic can trivially iterate groups in priority order and read value cells. The data structure enables the logic without containing it.

### AF-02: GraceMultiplier / Staleness Detection

**What:** Do NOT implement grace period logic (metric slot is "stale" if `now - updated_at > intervalSeconds * graceMultiplier`). Do NOT implement any timeout or expiry behavior on metric slots.
**Why Avoid:** GraceMultiplier is referenced in the spec but its semantics belong to the evaluation engine, not the data layer. The data layer stores `updated_at` and `intervalSeconds` -- the decision logic computes staleness.
**What to Do Instead:** Store `intervalSeconds` and `graceMultiplier` as config values on the metric slot so the future evaluation engine can use them. Do not act on them.

### AF-03: External API / HTTP Endpoints

**What:** Do NOT expose the tenant vector via REST API, gRPC, or any external interface.
**Why Avoid:** The spec says "Internal only -- no external API, no Prometheus export." The tenant vector is consumed by in-process decision logic, not external systems.
**What to Do Instead:** Provide internal C# interfaces (ITenantVectorRegistry) for in-process consumers. D-03 (diagnostic snapshot) is the furthest extent of accessibility.

### AF-04: Prometheus Metric Export of Tenant Data

**What:** Do NOT create OTel instruments that export per-tenant or per-slot metric values to Prometheus.
**Why Avoid:** The spec says "no Prometheus export." Tenant metric values are internal state. The existing `snmp_gauge` / `snmp_info` instruments already export raw SNMP data to Prometheus. Duplicating that data through the tenant lens would create cardinality explosion (tenants x metrics x labels).
**What to Do Instead:** D-04 (pipeline counter for routing) is acceptable because it counts routing events, not metric values. It does not expose tenant-level data.

### AF-05: Tenant-Specific Polling

**What:** Do NOT create new Quartz poll jobs for tenant metric slots. Do NOT modify the existing MetricPollJob or DynamicPollScheduler.
**Why Avoid:** The spec says metric slots declare `source` (poll/trap) and `intervalSeconds`, but the existing poll infrastructure already handles all SNMP polling. The tenant vector consumes data from the pipeline -- it does not generate new SNMP traffic. The spec's `intervalSeconds` on a metric slot is metadata for staleness calculation, not a polling directive.
**What to Do Instead:** The tenant vector behavior (TS-06) passively observes samples flowing through the existing pipeline. Tenants that reference a metric will receive updates whenever the existing poll/trap system produces a matching sample.

### AF-06: History / Time-Series Buffer

**What:** Do NOT implement any per-metric-slot history buffer, ring buffer, or time-series storage.
**Why Avoid:** The spec is explicit: "There is no per-metric history buffer -- history is the job of the decision_series." Each slot holds exactly one value cell (latest sample only).
**What to Do Instead:** Store `value` and `updated_at` only. If decision logic needs history, it will be a separate data structure in a future milestone.

### AF-07: Cross-Tenant Deduplication of Polling

**What:** Do NOT deduplicate or optimize when multiple tenants reference the same `(ip, port, oid)`. Do NOT merge polling requests or share value cells across tenants.
**Why Avoid:** The spec is explicit: "Each tenant maintains its own independent slot for that address -- its own value cell, its own updated_at." Independence is by design. Shared cells would create coupling between tenants that the spec explicitly forbids.
**What to Do Instead:** When a sample arrives, iterate all matching slots in the routing index and update each independently. The fan-out cost is a dictionary lookup + N cell writes, which is trivially fast for realistic tenant counts.

### AF-08: Persistence / Durable Storage

**What:** Do NOT persist tenant vector state to disk, database, or any durable storage.
**Why Avoid:** The tenant vector is an ephemeral in-memory structure rebuilt from config on startup. Metric values are transient snapshots, not historical data. Persistence adds complexity with no value -- the next poll cycle will repopulate all cells.
**What to Do Instead:** Accept that on pod restart, all value cells start empty (null value, no updated_at). The first poll/trap cycle repopulates them.

---

## Feature Dependencies

```
TS-01 (Config Model)
    |
    +--> TS-02 (ConfigMap Watcher) --> TS-10 (Atomic Rebuild)
    |                                      |
    +--> TS-03 (Tenant Registry) ----------+
    |         |
    |         +--> TS-04 (Value Cells) --> D-05 (Thread Safety)
    |         |
    |         +--> D-03 (Diagnostic Snapshot)
    |
    +--> TS-05 (Routing Index)
              |
              +--> TS-06 (Pipeline Behavior) --> TS-07 (Port Resolution)
              |         |                    --> TS-08 (Unknown Filter)
              |         |                    --> TS-09 (Heartbeat Filter)
              |         |
              |         +--> D-04 (Pipeline Counter)
              |
              +--> D-01 (Config Validation)

D-02 (Diff Logging) depends on TS-10 (Atomic Rebuild)
```

### Critical Path

The minimum viable path is: TS-01 -> TS-03 + TS-04 -> TS-05 -> TS-06 + TS-07 + TS-08 + TS-09 -> TS-02 + TS-10

Explanation: You can build and test the data structures (registry, cells, routing index) and pipeline integration first using hardcoded test config. Then add the ConfigMap watcher and atomic rebuild last. This allows unit testing the core logic before introducing K8s dependencies.

---

## MVP Recommendation

For the priority vector data layer milestone, prioritize in this order:

**Must build (10 features -- all table stakes):**
1. TS-01: Config model (foundation)
2. TS-03: Tenant registry with priority groups (core structure)
3. TS-04: Metric slot value cells (data storage)
4. TS-05: Routing index (fan-out mechanism)
5. TS-06: Pipeline behavior (data flow)
6. TS-07: Port resolution (routing key completion)
7. TS-08: Unknown metric filtering (data quality)
8. TS-09: Heartbeat filtering (data quality)
9. TS-02: ConfigMap watcher (operational necessity)
10. TS-10: Atomic rebuild (concurrency safety)

**Should build (4 differentiators):**
1. D-01: Config validation (catches mistakes early)
2. D-02: Diff logging (operational visibility)
3. D-04: Pipeline counter (observability)
4. D-05: Thread-safe value cells (correctness)

**Defer (1 differentiator):**
- D-03: Diagnostic snapshot -- useful but not needed until decision logic exists to consume it. Can be added when there is a consumer.

**Explicitly do NOT build (8 anti-features):**
- AF-01 through AF-08 as documented above.

---

## Spec Ambiguity Notes

### OID vs metric_name as routing key

The spec (tenantvector.txt) uses `oid` in the metric slot definition and `(ip, port, oid)` as the routing key. The milestone context uses `(ip, port, metric_name)`. These are reconcilable:

- **Config file (tenantvector.json):** Metric slots specify `oid` (the raw OID string). This is what the config author writes.
- **Runtime routing index:** Keyed by `(ip, port, metric_name)` where `metric_name` is the OID-map-resolved name. This is what the pipeline behavior uses to match incoming samples.
- **At load time:** The watcher resolves each configured OID to its metric_name via OidMapService. If an OID does not resolve (Unknown), the slot is logged as a warning (D-01) and excluded from the routing index.

This means the routing index is rebuilt not only when tenantvector.json changes, but also when the OID map changes (since metric_name mappings may change). This cross-dependency should be handled by having the TenantVectorWatcherService listen for OID map reloads as well, or by having OidMapService notify the tenant registry of changes.

### Port in SnmpOidReceived

The current `SnmpOidReceived` message does not carry a port. The recommended approach (TS-07) uses DeviceName-based lookup via DeviceRegistry. This works for all current data paths but assumes every sample has a known DeviceName with a registered device. This is guaranteed by ValidationBehavior (rejects null DeviceName) and the device registration requirement.

---

## Sources

- Spec analysis: `Docs/tenantvector.txt` -- Priority vector design specification (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` -- message model, no Port field (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/OidMapService.cs` -- FrozenDictionary pattern, Unknown constant (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/DeviceRegistry.cs` -- TryGetDeviceByName, FrozenDictionary atomic swap (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Services/OidMapWatcherService.cs` -- ConfigMap watcher pattern, SemaphoreSlim, reconnect (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` -- terminal handler, heartbeat skip, type dispatch (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` -- MetricName enrichment (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` -- DeviceName null rejection (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Jobs/MetricPollJob.cs` -- port from JobDataMap, DeviceName from device (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Services/SnmpTrapListenerService.cs` -- trap path, no port in VarbindEnvelope (HIGH confidence)

---
*Feature research for: Priority Vector Data Layer*
*Researched: 2026-03-10*
