# Domain Pitfalls

**Domain:** Priority vector data layer -- stateful fan-out for in-memory tenant metric slots in an SNMP monitoring pipeline
**Researched:** 2026-03-10
**Confidence:** HIGH (verified against system source code: MediatR pipeline, FrozenDictionary reload patterns, K8s watcher concurrency model, leader election gating)

---

## Critical Pitfalls

Mistakes that cause data corruption, silent data loss, or require architectural rework.

### Pitfall 1: Routing Index and OID Map Reload Are Independent -- Stale Metric Names in Tenant Slots

**What goes wrong:**
The OID map changes (OID `1.3.6.1.2.1.2.2.1.10.1` is renamed from `ifInOctets` to `interface_in_bytes`), but the tenant vector routing index still has entries keyed by `(ip, port, ifInOctets)`. New samples arrive with `MetricName = "interface_in_bytes"` (resolved by the updated OidMapService), miss the routing index lookup, and silently drop -- the tenant slot for the old metric name goes stale while the new name has no slot.

**Why it happens:**
The system has two independent ConfigMap watchers: `OidMapWatcherService` (watches `simetra-oidmaps`) and a future `TenantVectorWatcherService` (watches tenant vector config). These fire independently. The OID map watcher updates `OidMapService` via atomic `FrozenDictionary` swap. The OidResolutionBehavior uses the new map immediately for the next `SnmpOidReceived`. But the tenant vector routing index was built using the OLD metric names and is not aware that metric names changed.

This is the single most dangerous pitfall because the two config sources are coupled by the metric name key but reload independently.

**Consequences:**
- Silent data loss: samples with new metric names are not routed to any tenant slot
- Stale data: tenant slots hold the last value under the old metric name indefinitely
- No error signal: the pipeline processes successfully (OtelMetricHandler still exports), so pipeline counters look healthy

**Prevention:**
1. **Subscribe the tenant vector routing index to OID map reload events.** When `OidMapService.UpdateMap()` completes, the routing index must rebuild. The simplest approach: have the priority vector service observe the OidMapService (event, callback, or periodic version check) and trigger a routing index rebuild when the OID map version changes.

2. **Add a version stamp to OidMapService.** Currently `UpdateMap()` returns void. Add a monotonically increasing version number (or use the FrozenDictionary reference as a cheap identity check). The tenant vector can compare "did the OID map change since my last routing index build?" on each rebuild or periodically.

3. **Rebuild routing index on ANY config change.** Since tenant vector config and OID map config are both small (hundreds of entries, not millions), rebuilding the routing index on either change is cheap and eliminates ordering dependencies. Use a single `ReloadOrchestrator` that serializes: (a) apply OID map, (b) apply device registry, (c) apply tenant vector config, (d) rebuild routing index from current state of all three.

4. **Log routing misses explicitly.** When a `(ip, port, metric_name)` tuple arrives and no routing entry exists, log at Warning with the tuple. This makes stale routing immediately visible in logs rather than silently dropping.

**Detection:**
- Tenant slot `updated_at` timestamps stop advancing after an OID map change
- Warning logs for "no routing entry for (ip, port, metric_name)" appearing after an oidmaps ConfigMap change
- OTel business metrics still flowing (they use the resolved metric name directly) but tenant vector slots stale

**Phase to address:** Routing index design phase. The routing index rebuild trigger must be designed alongside the index itself, not bolted on later.

---

### Pitfall 2: Non-Atomic Two-Dictionary Swap in DeviceRegistry Creates a Read Window of Inconsistency

**What goes wrong:**
The existing `DeviceRegistry.ReloadAsync()` performs two volatile writes sequentially:
```csharp
_byIpPort = newByIpPort;   // volatile write 1
_byName = newByName;        // volatile write 2
```
Between write 1 and write 2, a concurrent reader calling `TryGetByIpPort()` sees the new dictionary while another reader calling `TryGetDeviceByName()` still sees the old dictionary. If the priority vector behavior reads both dictionaries during this window (e.g., looking up device by IP to get the device name, then looking up by name for tenant routing), it can get inconsistent results.

**Why it happens:**
`volatile` guarantees visibility of each individual write but does NOT guarantee atomicity across two writes. The current system tolerates this because `MetricPollJob` only uses `TryGetByIpPort` and `ChannelConsumerService` only uses `DeviceName` from the envelope. No existing code path reads both dictionaries in sequence for the same request. But the priority vector behavior might.

**Consequences:**
- Intermittent routing failures during device reload: a sample arrives with IP lookup succeeding against new registry but name lookup failing against old registry (or vice versa)
- Extremely hard to reproduce: requires a device reload to land between two reads in the same pipeline execution, which is a microsecond-scale window

**Prevention:**
1. **Do not add a code path that reads both DeviceRegistry dictionaries in sequence for the same request.** The priority vector routing index should use `(ip, port, metric_name)` as its key (as designed), which requires only a single lookup into the routing index -- no DeviceRegistry lookup needed at fan-out time.

2. **If you must cross-reference DeviceRegistry during routing:** Capture the device info at the start of the pipeline (e.g., in a behavior that runs once and attaches the `DeviceInfo` to the request) rather than looking it up again in the fan-out behavior.

3. **For the routing index rebuild (triggered by device registry reload):** The rebuild reads `AllDevices` which returns a snapshot `_byIpPort.Values.ToList()`. This is safe because it reads a single volatile field. The routing index build should use this single snapshot, not interleave reads from both dictionaries.

**Detection:**
- Sporadic "device not found" warnings during config reload that resolve on the next poll cycle
- Priority vector routing misses that correlate with device registry reload timestamps

**Phase to address:** Fan-out behavior implementation. Ensure the behavior reads from the routing index only, not from DeviceRegistry.

---

### Pitfall 3: MediatR Behavior Ordering -- Fan-Out Before OidResolution Means No MetricName

**What goes wrong:**
The fan-out behavior is registered before `OidResolutionBehavior` in the MediatR pipeline. When it executes, `SnmpOidReceived.MetricName` is still `null` (it gets set by OidResolutionBehavior). The routing index lookup by `(ip, port, metric_name)` fails because `metric_name` is null. Every sample misses routing. Zero data reaches tenant slots.

**Why it happens:**
MediatR behavior registration order in `AddSnmpPipeline()` determines execution order. Currently:
```
1. LoggingBehavior       (outermost)
2. ExceptionBehavior
3. ValidationBehavior
4. OidResolutionBehavior (innermost, sets MetricName)
```
The fan-out behavior MUST run after OidResolutionBehavior, meaning it must be registered after it (closer to the handler) or integrated into the handler itself. But the MediatR open behavior registration model means "after OidResolution" is actually "between OidResolution and OtelMetricHandler" -- which is inside the OidResolution behavior's `next()` call.

This is counterintuitive: in MediatR's pipeline model, behaviors are nested like middleware. A behavior registered AFTER OidResolutionBehavior runs INSIDE it (after OidResolution calls `next()`). This is correct for fan-out. But if someone registers it BEFORE OidResolution (e.g., between Validation and OidResolution), MetricName will be null.

**Consequences:**
- Complete data loss to tenant slots -- routing index never matches
- No error (null metric_name simply does not match any routing entry)
- Pipeline counters look healthy (OtelMetricHandler still processes the sample)

**Prevention:**
1. **Register the fan-out behavior AFTER `OidResolutionBehavior` in `AddSnmpPipeline()`.** The registration should be:
   ```
   cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // 1st
   cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));     // 2nd
   cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));    // 3rd
   cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>)); // 4th
   cfg.AddOpenBehavior(typeof(TenantFanOutBehavior<,>));  // 5th -- MUST be after OidResolution
   ```

2. **Add a guard clause in the fan-out behavior:** If `msg.MetricName` is null or `"Unknown"`, skip fan-out for this sample. This both prevents null-key routing failures and ensures only OID-map-resolved metrics enter tenant slots (which is the design intent: "only OID-map-resolved metrics allowed").

3. **Add a unit test that verifies fan-out receives a non-null MetricName.** Register the behaviors in order, send an `SnmpOidReceived` through the pipeline, and assert the fan-out behavior saw a non-null MetricName. This test catches accidental reordering.

**Detection:**
- ALL tenant slots have null or zero values despite active polling
- Debug logs in fan-out behavior showing `MetricName=null` or `MetricName=Unknown`
- Unit test failure when behavior registration order changes

**Phase to address:** Behavior registration phase. This must be the first thing validated when the fan-out behavior is wired up.

---

### Pitfall 4: Slot Write Torn Reads -- value and updated_at Are Not Atomically Written Together

**What goes wrong:**
A metric slot holds `(value, updated_at)`. The fan-out behavior writes `slot.Value = newValue` then `slot.UpdatedAt = DateTimeOffset.UtcNow`. A concurrent reader (e.g., a future API or dashboard poller) reads between the two writes: it sees the new value but the OLD timestamp. It incorrectly concludes the value is stale (old timestamp) or, worse, serves the new value with a misleading timestamp.

**Why it happens:**
The MediatR pipeline is async but `ISender.Send` is awaited sequentially by both `MetricPollJob.DispatchResponseAsync` and `ChannelConsumerService.ExecuteAsync`. Within a single pod, the pipeline for one sample runs to completion before the next sample starts. So for the write path, there is no concurrent write contention.

However, the READ path is a different story. If any consumer reads tenant slots concurrently with the write path (health check, diagnostic endpoint, future API, metrics export), the reader can see a partially-written slot.

**Consequences:**
- Misleading timestamps on slot values
- If the reader uses `updated_at` for staleness detection, it may incorrectly flag fresh data as stale (or vice versa)
- In a multi-threaded read scenario (e.g., OTel export running on a background timer), the torn read window is real

**Prevention:**
1. **Make the slot an immutable struct and swap atomically.** Instead of mutating two fields, create a new `MetricSlot` value and assign it in a single reference write:
   ```csharp
   // Slot is a readonly record struct or sealed class
   record MetricSlot(double Value, DateTimeOffset UpdatedAt);

   // Atomic swap (single volatile/Interlocked write)
   _slots[key] = new MetricSlot(newValue, DateTimeOffset.UtcNow);
   ```
   A single reference write is atomic on .NET (guaranteed for reference types and structs <= pointer size on the platform). For a struct larger than pointer size, use `Volatile.Write` or store as a reference type.

2. **Use `Interlocked.Exchange` for the slot reference** if you want an explicit memory barrier guarantee, though for reference types a simple volatile write suffices.

3. **Do NOT use a lock for slot writes.** The write path runs on the hot pipeline for every sample. Lock contention would degrade throughput. Immutable-swap is lock-free and sufficient.

**Detection:**
- Reader seeing `updated_at` older than expected despite `value` being current
- Staleness health checks flapping (detecting stale slots that are actually being written)

**Phase to address:** Slot data structure design phase. The slot type must be designed as immutable-swap from the start. Retrofitting atomicity onto mutable fields is error-prone.

---

## Moderate Pitfalls

Mistakes that cause incorrect behavior, performance issues, or unnecessary complexity.

### Pitfall 5: Routing Index Rebuild Blocks the Pipeline During Config Reload

**What goes wrong:**
When tenant vector config changes, the routing index rebuilds. If the rebuild holds a lock or blocks while building a new `FrozenDictionary`, all incoming samples queue behind the rebuild. With 3 replicas each polling 10+ devices every 10 seconds, even a 100ms rebuild can cause a visible pipeline stall.

**Why it happens:**
The existing pattern in `OidMapService.UpdateMap()` is: compute diff, build new FrozenDictionary, volatile write. The diff computation iterates old and new maps (O(n) where n = entries). This is fast for the OID map (~100 entries). But the tenant vector routing index could be larger: (devices x metric_names x tenants). If there are 50 devices, 20 metrics each, and 5 tenants, the routing index is 5,000 entries. Still fast, but the rebuild should not block readers.

**Prevention:**
1. **Follow the existing FrozenDictionary atomic-swap pattern exactly.** Build the new routing index in a local variable, then assign it via a single volatile write. Readers see either the old or new index, never a partially-built one. No lock needed on the read path.

2. **Serialize rebuilds with a SemaphoreSlim(1,1)** (same pattern as `OidMapWatcherService._reloadLock` and `DeviceWatcherService._reloadLock`). This prevents two concurrent config changes from interleaving their rebuild logic. But the semaphore guards the BUILD, not the READ. Readers always read the volatile field directly.

3. **Do not hold the semaphore while building the FrozenDictionary.** The pattern should be:
   ```
   await _rebuildLock.WaitAsync(ct);
   try {
       var newIndex = BuildRoutingIndex(currentOidMap, currentDevices, currentTenantConfig);
       _routingIndex = newIndex; // volatile write -- readers see this immediately
   } finally {
       _rebuildLock.Release();
   }
   ```

**Detection:**
- Pipeline latency spikes correlating with ConfigMap change timestamps
- `snmp_event_handled_total` rate dropping momentarily during config reload

**Phase to address:** Routing index implementation phase. Use the existing codebase patterns (OidMapService, DeviceRegistry) as templates.

---

### Pitfall 6: Device Removal Leaves Orphaned Tenant Slots With Stale Data

**What goes wrong:**
A device is removed from the device registry (via ConfigMap change). The routing index is rebuilt without entries for that device. But the tenant slots for that device's metrics still exist in memory, holding the last known values. If a consumer reads "all slots for tenant X," it gets stale data for a removed device. If the device is later re-added with the same IP:port, the old stale values are served until the first new sample overwrites them.

**Why it happens:**
The routing index controls which NEW samples reach which slots. But existing slots are not garbage-collected when routing entries are removed. The slot storage is a flat dictionary (or similar) that only grows.

**Consequences:**
- Stale data served for removed devices
- Memory growth over time if devices are frequently added/removed
- Confusion when a device is re-added and momentarily shows old values

**Prevention:**
1. **On routing index rebuild, compute the diff of slot keys and explicitly remove orphaned slots.** After building the new routing index, compare the old and new sets of `(ip, port, metric_name, tenant)` keys. Any key in the old set but not the new set should have its slot removed (or marked as expired).

2. **Include `updated_at` in consumer reads and let consumers apply their own staleness threshold.** A consumer that sees `updated_at` from 30 minutes ago can decide to treat it as stale. This is simpler than active garbage collection but requires all consumers to implement staleness logic.

3. **Log slot cleanup on rebuild:** "Removed N orphaned slots for device X, tenant Y" at Information level. This provides operational visibility into slot lifecycle.

**Detection:**
- Slots with `updated_at` timestamps that stopped advancing long ago
- Memory usage growing linearly with config changes over time
- Consumer displaying data for devices that no longer exist in the device registry

**Phase to address:** Routing index rebuild phase. Slot cleanup must be part of the rebuild, not a separate maintenance task.

---

### Pitfall 7: Per-Pod State Divergence in Multi-Replica Deployment

**What goes wrong:**
Each of the 3 replicas maintains its own independent priority vector state. Because pods poll different devices (due to Quartz scheduling timing differences) and receive traps independently, each pod's tenant slots may hold different values for the same `(ip, port, metric_name)` at any given moment. A consumer that reads from different pods on successive requests gets inconsistent data.

**Why it happens:**
The priority vector is in-memory and per-pod by design. Each pod runs its own MediatR pipeline, receives its own SNMP poll responses, and writes to its own slot storage. There is no cross-pod synchronization.

For the existing OTel metrics path, this is fine: the leader exports business metrics, and Prometheus deduplicates via labels. But the priority vector is a direct-read data structure, not an export-to-Prometheus flow. If any consumer reads from it, the answer depends on which pod it hits.

**Consequences:**
- Different values returned depending on which pod serves the request
- Difficulty reasoning about "what is the current value?" when 3 pods each have a different answer
- If only the leader pod's slots are authoritative (matching the leader-gated export pattern), follower pods maintain slot state for no reason

**Prevention:**
1. **Decide upfront: is tenant vector state leader-only or all-pods?** If leader-only: the fan-out behavior should check `ILeaderElection.IsLeader` and skip slot writes on follower pods. This saves memory and CPU on followers and makes the answer deterministic (always from the leader). If all-pods: document that consumers must either (a) always read from the leader or (b) aggregate across pods.

2. **If leader-only: handle leadership transitions.** When a follower becomes leader, its slots are either empty (if it was skipping writes) or stale (if it was writing but nobody was reading). The new leader must wait for at least one full poll cycle to populate its slots before serving data. Add a health/readiness signal: "tenant vector populated" = at least one sample per routing entry received.

3. **If all-pods: accept eventual consistency.** Document that the value returned is "the most recent sample THIS pod received" and is not globally consistent. For most monitoring use cases, this is acceptable (the value will converge within one poll interval).

**Detection:**
- Consumer getting different values when load-balanced across pods
- After leadership failover, tenant vector returning stale or empty data for a full poll cycle

**Phase to address:** Architecture decision phase. This is a design-time decision that affects every downstream implementation choice. Must be decided before writing any slot code.

---

### Pitfall 8: ExceptionBehavior Swallows Fan-Out Errors Silently

**What goes wrong:**
The fan-out behavior throws an exception (null reference, routing index not initialized, slot storage full). The `ExceptionBehavior` catches it, logs a Warning, increments the error counter, and returns `default!`. The pipeline continues. No sample reaches either the tenant slots OR the OtelMetricHandler. The business metrics stop exporting silently.

**Why it happens:**
The `ExceptionBehavior` is registered as the 2nd behavior (inside Logging, outside everything else). It catches ALL exceptions from downstream behaviors and handlers. If the fan-out behavior (5th) throws, ExceptionBehavior catches it and short-circuits the entire downstream chain, including OtelMetricHandler (the terminal handler). This means a bug in the fan-out behavior kills the existing OTel export path.

The current pipeline has no behavior that throws in normal operation (OidResolution never throws, Validation short-circuits via `return default!` not via exception). The fan-out behavior introduces a new exception source inside the pipeline.

**Consequences:**
- A bug in fan-out (new code) kills OTel export (existing, working code)
- Silent: ExceptionBehavior logs a Warning but the metric export just stops
- Prometheus dashboards go flat with no obvious error signal

**Prevention:**
1. **The fan-out behavior MUST catch its own exceptions internally and NOT let them propagate.** The pattern:
   ```csharp
   // In TenantFanOutBehavior.Handle():
   if (notification is SnmpOidReceived msg && msg.MetricName != null)
   {
       try
       {
           FanOutToTenantSlots(msg);
       }
       catch (Exception ex)
       {
           _logger.LogWarning(ex, "Tenant fan-out failed for {MetricName}", msg.MetricName);
           // Do NOT re-throw. Let the pipeline continue to OtelMetricHandler.
       }
   }
   return await next(); // ALWAYS call next() regardless of fan-out success
   ```

2. **ALWAYS call `next()` unconditionally.** The fan-out behavior is supplementary (writes to tenant slots) not gatekeeping (decides whether the sample proceeds). It must never short-circuit the pipeline.

3. **Add an integration test that verifies: when fan-out throws, OtelMetricHandler still receives the sample.** Register a fan-out behavior that always throws, send a sample, and assert the handler's `_pipelineMetrics.IncrementHandled()` was called.

**Detection:**
- `snmp_event_handled_total` stops incrementing while `snmp_event_published_total` continues
- `snmp_pipeline_errors_total` incrementing on every sample
- Prometheus `snmp_gauge` goes flat but pod logs show no obvious failure

**Phase to address:** Fan-out behavior implementation phase. The try/catch-and-continue pattern must be the FIRST thing written in the behavior, before any fan-out logic.

---

### Pitfall 9: FrozenDictionary Rebuild Allocates on Every Config Change -- GC Pressure in Long-Running Pod

**What goes wrong:**
The routing index uses `FrozenDictionary<TKey, TValue>` (matching OidMapService and DeviceRegistry patterns). Each config reload creates a new FrozenDictionary and abandons the old one. In a long-running pod with frequent config changes (e.g., automated OID map updates from a CI pipeline), the Gen2 GC heap accumulates abandoned FrozenDictionary instances.

**Why it happens:**
`FrozenDictionary` is optimized for read performance by pre-computing hash buckets. Creating one is more expensive than creating a regular `Dictionary` (it does internal optimization passes). The abandoned old dictionary must be collected by GC. For the OID map (~100 entries) and device registry (~50 entries), this is trivial. But if the routing index is large (5,000+ entries with nested slot references), the allocation and GC cost per reload increases.

**Consequences:**
- Gen2 GC pauses during config reload in long-running pods
- Memory spikes during reload (old + new dictionaries both in memory until GC)
- In extreme cases (very large routing index + frequent reloads), observable pipeline latency spikes during GC

**Prevention:**
1. **This is unlikely to be a real problem at the expected scale.** With 50 devices x 20 metrics x 5 tenants = 5,000 routing entries, a FrozenDictionary is ~200KB. Two copies during reload = ~400KB. Gen2 GC of this size is sub-millisecond. Do not optimize prematurely.

2. **If scale grows beyond expectations (1000+ devices):** Consider using a `ConcurrentDictionary` for the routing index instead of FrozenDictionary. The read performance difference is negligible for lookup operations (both are O(1)). ConcurrentDictionary supports incremental updates without full rebuild.

3. **Monitor via `System.Runtime` GC metrics** already exported by OTel (`gc_heap_size`, `gc_count`). If Gen2 collections spike after config reloads, investigate.

**Detection:**
- Gen2 GC count incrementing after config reloads (visible in Prometheus via `process_runtime_dotnet_gc_collections_total{generation="gen2"}`)
- Pod memory usage sawtoothing during frequent config changes

**Phase to address:** Not a priority for initial implementation. Monitor after deployment. Only optimize if metrics show a problem.

---

## Minor Pitfalls

Mistakes that cause confusion or minor bugs but are easily fixed.

### Pitfall 10: Heartbeat Samples Polluting Tenant Slots

**What goes wrong:**
The HeartbeatJob sends a loopback trap through the full MediatR pipeline. If the fan-out behavior does not filter out heartbeat messages, it attempts to route them to tenant slots. The heartbeat uses a sentinel DeviceName and has `IsHeartbeat = true`, but if the routing index does not explicitly exclude heartbeats, they either (a) match no routing entry (harmless miss) or (b) accidentally match a routing entry if the sentinel IP/port collides with a real device.

**Prevention:**
1. **Check `msg.IsHeartbeat` early in the fan-out behavior and skip.** This matches the pattern in `OidResolutionBehavior` and `OtelMetricHandler`:
   ```csharp
   if (msg.IsHeartbeat) return await next();
   ```

2. **Check `msg.MetricName == OidMapService.Unknown` and skip.** Unresolved OIDs should not enter tenant slots (design decision: "only OID-map-resolved metrics allowed").

**Detection:**
- Tenant slots with DeviceName matching the heartbeat sentinel
- Unexpected routing misses logged for heartbeat OIDs

**Phase to address:** Fan-out behavior implementation. Add guard clauses at the top of the behavior.

---

### Pitfall 11: Tenant Config Validation Gap -- Invalid Metric Names Pass Silently

**What goes wrong:**
A tenant vector ConfigMap references a metric name that does not exist in the current OID map (typo or stale config). The routing index is built with entries that will never match any incoming sample. The tenant slot is created but never written to. No error is logged.

**Prevention:**
1. **During routing index build, cross-reference tenant config metric names against the current OID map's known metric names.** Log a Warning for any metric name in tenant config that is not in the OID map: "Tenant 'X' references metric 'Y' which is not in the current OID map -- slot will not receive data."

2. **This is a warning, not an error.** The metric might be added to the OID map later. But the warning gives operators visibility into misconfiguration.

3. **Consider a startup health check** that verifies at least 80% of tenant-referenced metric names exist in the OID map. This catches bulk misconfiguration without being brittle to individual OID map timing.

**Detection:**
- Tenant slots with `updated_at = null` (never written to)
- Warning logs during routing index build listing unmatched metric names

**Phase to address:** Routing index build phase. Add cross-reference validation as part of the build.

---

### Pitfall 12: Forgetting to Skip Fan-Out for Unresolved ("Unknown") Metrics

**What goes wrong:**
An OID arrives that is not in the OID map. `OidResolutionBehavior` sets `MetricName = "Unknown"`. The fan-out behavior routes by `(ip, port, "Unknown")`. If a tenant happens to have a catch-all or wildcard routing entry, ALL unresolved OIDs from ALL devices pile into one slot, overwriting each other. The slot value is meaningless (whichever unresolved OID was polled last wins).

**Prevention:**
1. **Skip fan-out when `msg.MetricName == OidMapService.Unknown`.** This is the correct behavior: the design states "only OID-map-resolved metrics allowed" in the priority vector.

2. **Do not support wildcard metric names in tenant routing config.** The metric name must be an exact match against the OID map's resolved names.

**Detection:**
- A slot with `metric_name = "Unknown"` receiving updates from multiple devices
- Rapid `updated_at` changes on a slot that should be device-specific

**Phase to address:** Fan-out behavior guard clauses, routing config validation.

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Architecture decision: leader-only vs all-pods | Per-pod divergence (Pitfall 7) | Decide before writing slot code; if leader-only, gate fan-out behind IsLeader |
| Slot data structure design | Torn reads (Pitfall 4) | Use immutable record + atomic swap from day one |
| Fan-out behavior registration | Wrong pipeline ordering (Pitfall 3) | Register AFTER OidResolutionBehavior; add unit test for ordering |
| Fan-out behavior implementation | Exception kills OTel export (Pitfall 8) | Internal try/catch; always call next(); integration test |
| Fan-out behavior guard clauses | Heartbeat/Unknown pollution (Pitfalls 10, 12) | Check IsHeartbeat and MetricName early |
| Routing index design | OID map reload desync (Pitfall 1) | Subscribe to OidMapService changes; rebuild routing on any config change |
| Routing index implementation | Pipeline blocking during rebuild (Pitfall 5) | FrozenDictionary atomic swap; SemaphoreSlim on build only |
| Routing index rebuild | Orphaned slots for removed devices (Pitfall 6) | Compute diff on rebuild; remove orphaned slots |
| Tenant config validation | Invalid metric name references (Pitfall 11) | Cross-reference against OID map; log warnings |
| Multi-config reload coordination | Independent watcher race (Pitfall 1) | Centralized rebuild trigger; serialize all config changes |

---

## Concurrency Model Summary

The existing system's concurrency model is crucial context for the priority vector design:

| Component | Threading Model | Safe For Concurrent Access? |
|-----------|----------------|---------------------------|
| MediatR pipeline (single request) | Single-threaded within one `ISender.Send` call | Yes -- no concurrent writes from pipeline |
| MetricPollJob (per device) | Concurrent across devices (Quartz thread pool) | Each device's samples are serialized by `DisallowConcurrentExecution`, but DIFFERENT devices run concurrently |
| ChannelConsumerService | Single reader on the trap channel | Serialized -- one sample at a time |
| OidMapService reload | `SemaphoreSlim(1,1)` in OidMapWatcherService | Serialized writes, lock-free reads via volatile |
| DeviceRegistry reload | `SemaphoreSlim(1,1)` in DeviceWatcherService | Serialized writes, lock-free reads via volatile |

**Implication for priority vector:** Multiple `MetricPollJob` instances can call `ISender.Send` concurrently (one per device on different Quartz threads). Each Send runs the full behavior pipeline including the fan-out behavior. If two devices have metrics routed to the same tenant slot, the slot receives concurrent writes from different threads. The slot write MUST be safe for concurrent access (immutable-swap via `ConcurrentDictionary` or `Interlocked`).

This is NOT the same as "pipeline is synchronous within a single request." The pipeline is synchronous for ONE request, but multiple requests run concurrently across Quartz threads.

---

## Sources

- System source code analysis (HIGH confidence, direct verification):
  - `OidMapService.cs`: volatile FrozenDictionary swap, UpdateMap() pattern
  - `DeviceRegistry.cs`: dual volatile FrozenDictionary swap, ReloadAsync() pattern
  - `OidResolutionBehavior.cs`: MetricName enrichment point in pipeline
  - `OtelMetricHandler.cs`: terminal handler, heartbeat filtering
  - `ExceptionBehavior.cs`: catch-all swallowing pattern
  - `ServiceCollectionExtensions.cs`: behavior registration order, DI patterns
  - `MetricPollJob.cs`: concurrent execution model, DisallowConcurrentExecution
  - `ChannelConsumerService.cs`: single consumer, sequential dispatch
  - `OidMapWatcherService.cs`: SemaphoreSlim reload serialization
  - `DeviceWatcherService.cs`: SemaphoreSlim reload serialization, dual-step reload (registry + scheduler)
  - `LivenessVectorService.cs`: ConcurrentDictionary slot pattern (architectural reference)
  - `MetricRoleGatedExporter.cs`: leader-gating pattern for export
  - `SnmpOidReceived.cs`: request message structure, mutable MetricName
- .NET FrozenDictionary documentation: thread-safe for concurrent reads after construction (HIGH confidence, official .NET docs)
- .NET memory model: reference-type assignments are atomic on all .NET implementations (HIGH confidence, ECMA-335 spec)

---
*Pitfalls research for: Priority vector data layer -- stateful fan-out for in-memory tenant metric slots*
*Researched: 2026-03-10*
