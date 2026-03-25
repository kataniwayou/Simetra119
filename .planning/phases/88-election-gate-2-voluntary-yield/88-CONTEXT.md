# Phase 88: K8sLeaseElection — Gate 2 (Voluntary Yield While Leading) - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

When a non-preferred pod is leader and the preferred pod recovers (stamp becomes fresh), the non-preferred pod voluntarily deletes the leadership lease and cancels inner election so the preferred pod can acquire it. The `_innerCts` and `CancelInnerElection()` mechanism from Phase 87 is the foundation.

</domain>

<decisions>
## Implementation Decisions

### Yield trigger mechanism
- PreferredHeartbeatJob detects the yield condition and triggers it — no new polling loops
- Detection logic: on each tick, after updating stamp freshness, check: `IsPreferredStampFresh AND this pod is leader AND this pod is NOT preferred` → trigger yield
- PreferredHeartbeatJob injects concrete `K8sLeaseElection` (already registered as singleton in K8s DI) to call `CancelInnerElection()`
- No new interface — direct concrete injection for `CancelInnerElection()` access

### Lease deletion on yield
- Explicit delete of leadership lease (same `DeleteNamespacedLeaseAsync` as graceful shutdown) — near-instant handover
- PreferredHeartbeatJob deletes the leadership lease first, then calls `CancelInnerElection()`
- Deletion happens in the job (close to the decision point), not inside K8sLeaseElection
- Job needs `IKubernetes` (already injected) and `LeaseOptions` (already injected) for the delete call

### Recovery after yield
- Normal Gate 1 flow — no special post-yield behavior
- Outer loop restarts → Gate 1 sees fresh stamp → backs off DurationSeconds → preferred pod acquires naturally
- Non-preferred pod stays in backoff loop until preferred stamp goes stale (preferred crashes) or shutdown

### Claude's Discretion
- Whether to add a `YieldLeadership()` helper method on PreferredHeartbeatJob or inline the delete+cancel sequence
- Whether the delete failure (e.g. lease already gone) should be logged as Warning or ignored silently
- Exact condition ordering in the yield check (which field checked first for short-circuit efficiency)

</decisions>

<specifics>
## Specific Ideas

- The yield sequence in PreferredHeartbeatJob: `DeleteNamespacedLeaseAsync(leaseOptions.Name, leaseOptions.Namespace)` → `leaseElection.CancelInnerElection()`
- After delete, `_isLeader` becomes false when `OnStoppedLeading` fires from the inner cancel
- The preferred pod is already in its own election loop (not backing off since it's preferred) — it acquires the now-vacant lease

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 88-election-gate-2-voluntary-yield*
*Context gathered: 2026-03-26*
