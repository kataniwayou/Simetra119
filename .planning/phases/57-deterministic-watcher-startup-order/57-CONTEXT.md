# Phase 57: Deterministic Watcher Startup Order - Context

**Gathered:** 2026-03-18
**Status:** Ready for planning

<domain>
## Phase Boundary

Enforce sequential initial load order for ConfigMap watchers (OID metric map -> devices -> command map -> tenants) so tenant validation always runs against fully populated registries. Hot-reload watch loops remain independent after startup. No cascade reloads.

</domain>

<decisions>
## Implementation Decisions

### Startup sequencing mechanism
- Sequential calls in Program.cs — call each watcher's InitialLoadAsync explicitly in order before starting the host
- Each watcher exposes a new public `async Task InitialLoadAsync()` method
- ExecuteAsync only starts the K8s watch loop (no initial load)
- Watchers remain registered as BackgroundServices for the watch loop
- Order: OidMapWatcher -> DeviceWatcher -> CommandMapWatcher -> TenantVectorWatcher
- Local-dev path follows the same sequential init order (same 1->2->3->4)

### Hot-reload after startup
- No cascade — watch loops stay fully independent after initial load
- Command map change does NOT trigger tenant re-validation
- Operator's responsibility to deploy configs in dependency order
- System validates at load time; stale references caught on next tenant config change

### Failure handling during startup
- Initial load failure: crash immediately, no retries, no backoff — let K8s restart the pod
- Chain failure: crash at the first failed watcher, don't attempt downstream watchers
- Hot-reload failure: reject new config, LogError, retain previous valid config (existing behavior, no change needed)

### Observability & operator contract
- Per-watcher INFO log line as each completes: "[INF] OidMapWatcher initial load complete (112 entries)"
- Summary INFO line after all 4 complete: "[INF] Startup sequence: OidMap=112 (0.3s) -> Devices=3 (0.5s) -> CommandMap=13 (0.1s) -> Tenants=4 (0.2s) -- total 1.1s"
- Comment block in tenant ConfigMap YAML noting dependency deploy order

### Claude's Discretion
- Exact method signature for InitialLoadAsync (parameters, return type)
- How to refactor existing initial load code out of ExecuteAsync
- Whether to extract a common base class or keep each watcher independent
- Startup timing implementation (Stopwatch vs DateTimeOffset)

</decisions>

<specifics>
## Specific Ideas

- The existing watchers already handle hot-reload failures gracefully (catch + log + retain previous config) — that behavior stays unchanged
- InitialLoadAsync should be extractable from the existing ExecuteAsync initial-load block in each watcher — minimal new code
- The SuppressionCache TrySuppress already handles SuppressionWindowSeconds=-1 correctly (TimeSpan.FromSeconds(-1) = negative = comparison always false = never suppressed)

</specifics>

<deferred>
## Deferred Ideas

- Cascade reload (command map change triggers tenant re-validation) — could be a future phase if operator friction is too high
- Periodic stale-reference audit log (warn about tenant CommandNames that no longer resolve after command map reload) — observability enhancement

</deferred>

---

*Phase: 57-deterministic-watcher-startup-order*
*Context gathered: 2026-03-18*
