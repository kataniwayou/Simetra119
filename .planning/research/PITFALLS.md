# Pitfalls Research

**Domain:** Preferred/priority leader election with site-affinity added to existing K8s lease-based distributed SNMP monitoring system
**Researched:** 2026-03-25
**Confidence:** HIGH for K8s library behavior (verified against kubernetes-client/csharp and client-go source); HIGH for integration pitfalls (verified against project source: K8sLeaseElection.cs, GracefulShutdownService.cs, CommandWorkerService.cs, deployment.yaml, rbac.yaml); MEDIUM for operational edge cases (multi-source reasoning)

> **Scope:** Pitfalls specific to adding a preferred/priority leader election mechanism on top of the existing two-lease design (leadership lease + preferred heartbeat lease). The existing system already uses `LeaderElector.RunAndTryToHoldLeadershipForeverAsync`, voluntary yield via lease deletion on SIGTERM, and `ILeaderElection.IsLeader` gating in `MetricRoleGatedExporter` and `CommandWorkerService`. Known pitfalls already surfaced in design conversations (split-brain window, flapping preemption, LeaderElector state mismatch from force-acquisition, in-flight command loss during yield, network partition at preferred site, PreferredNode config staleness) are **not repeated here**. This document covers additional pitfalls not yet identified.

---

## Critical Pitfalls

Mistakes that cause incorrect behavior, rewrites, or data loss.

---

### Pitfall 1: `OnStoppedLeading` Fires on Every Leadership Loss — Not Just Permanent Exit

**What goes wrong:** The implementation assumes `OnStoppedLeading` fires only when the pod permanently surrenders leadership. In fact, `RunAndTryToHoldLeadershipForeverAsync` loops: it calls `RunUntilLeadershipLostAsync` repeatedly, and `OnStoppedLeading` fires in the `finally` block of each inner call — including transient losses (renewal blip, short API hiccup). Code that reacts to `OnStoppedLeading` by performing irreversible teardown (clearing in-memory state, closing channels, initiating drain) will corrupt the pod's ability to re-acquire and function as leader.

**Why it happens:** `RunUntilLeadershipLostAsync` is documented as "completes and does not retry." The forever-loop wraps it, making `OnStoppedLeading` fire on every inner-loop exit, not just final application shutdown. The distinction is not obvious without reading the library source.

**How to avoid:** Keep `OnStoppedLeading` handlers idempotent and non-destructive. Set `_isLeader = false` only. Do not drain channels, release resources, or alter scheduler state inside this handler. Reserve irreversible teardown for `StopAsync` / `CancellationToken` cancellation.

The preferred-leader yield path uses `StopAsync` which deletes the leadership lease and then sets `_isLeader = false` — this is correct. The new preferred heartbeat service must not hook `OnStoppedLeading` for any destructive action.

**Warning signs:** Pod logs show `OnStoppedLeading` multiple times in a run but the pod later re-acquires leadership and resumes export — confirms the handler fires on transient losses. If any resource that should persist across re-acquisition is absent after re-acquisition, suspect destructive `OnStoppedLeading` logic.

**Phase to address:** Preferred heartbeat service implementation (Phase: two-lease coordination).

---

### Pitfall 2: Preferred Heartbeat Lease Read Occurs While the Preferred Pod Deletes and Recreates It

**What goes wrong:** The two-lease design uses a second lease ("preferred heartbeat lease") that the preferred pod writes periodically to signal liveness. Non-preferred pods read this lease to decide whether to back off. The K8s API returns a 404 for a deleted lease. If the preferred pod deletes its heartbeat lease during shutdown (e.g., a clean SIGTERM) and immediately recreates it on restart, there is a window where the non-preferred leader reads 404, concludes the preferred pod is absent, and stops backing off — potentially winning or retaining leadership just as the preferred pod restarts and expects to preempt.

**Why it happens:** Lease deletion + pod restart + next election cycle can overlap within seconds on fast nodes. The non-preferred pod's back-off check fires during that 404 window, sees a stale absence signal, and proceeds.

**How to avoid:** On the preferred pod: do NOT explicitly delete the heartbeat lease during shutdown. Let it expire naturally via TTL. The non-preferred pod's "preferred is present" check should use a freshness window (e.g., `renewTime` must be within the last N seconds) rather than treating "lease exists" as a binary signal. A 404 should be treated the same as a stale timestamp — both indicate "preferred is not fresh," which is a necessary but not sufficient condition for yielding.

Corollary: set the heartbeat lease TTL to be longer than the pod restart time (at least 2× the `terminationGracePeriodSeconds` = 60s) so a clean restart does not create a spurious absence window.

**Warning signs:** Metrics show leadership bouncing rapidly around the time a preferred pod restarts cleanly. Logs show the non-preferred pod logging "preferred not fresh, no longer backing off" followed seconds later by "preferred pod acquired leadership" from the preferred pod.

**Phase to address:** Two-lease coordination design (heartbeat lease lifecycle and TTL selection).

---

### Pitfall 3: RBAC Does Not Cover the New Heartbeat Lease Name or Namespace

**What goes wrong:** The existing `rbac.yaml` grants `["get", "list", "watch", "create", "update", "patch", "delete"]` on `leases` in the `simetra` namespace. This covers any lease in that namespace by name. However: if the heartbeat lease is placed in a different namespace for isolation (e.g., a shared coordination namespace), or if a cluster-scoped lease is used by mistake, the pod's service account (`simetra-sa`) will receive 403 Forbidden on all operations against the heartbeat lease. The `LeaderElector` and any direct `IKubernetes` calls will fail with `HttpOperationException`.

**Why it happens:** The existing RBAC uses a `Role` (namespace-scoped), not `ClusterRole`. Adding a lease in a different namespace requires a new `Role` + `RoleBinding` in that namespace. Operators often forget this when copying the heartbeat lease design from documentation that shows leases in `kube-system` or a different coordination namespace.

**How to avoid:** Keep both leases (leadership + heartbeat) in the `simetra` namespace. The existing `Role` already covers this. Explicitly document in the design that both lease names must be in the same namespace as the RBAC binding. Add a startup validation that attempts a `GetNamespacedLeaseAsync` for both lease names during `IHostedService.StartAsync` and fails fast with a clear error if 403 is returned, rather than silently failing at election time.

**Warning signs:** Pod startup logs show `403 Forbidden` on lease operations. `K8sLeaseElection` remains in follower state permanently (the `OnStartedLeading` event never fires). The pod never exports business metrics.

**Phase to address:** RBAC and lease configuration (pre-implementation checklist).

---

### Pitfall 4: `NODE_NAME` Environment Variable Is Empty at Process Startup

**What goes wrong:** The preferred-leader check compares `PreferredNode` config against `NODE_NAME` (sourced from the Downward API `spec.nodeName`). The existing `deployment.yaml` already injects `PHYSICAL_HOSTNAME` from `spec.nodeName`. If the preferred-leader code reads `NODE_NAME` (or whatever the env var is named) before the container environment is fully initialized — for example, in a static field initializer or a `IOptions<>` validator that runs in the DI container build phase — it may receive an empty string. An empty `NODE_NAME` will never match `PreferredNode`, meaning the pod always behaves as non-preferred even when it is running on the preferred node.

**Why it happens:** `spec.nodeName` is guaranteed to be populated by the time the pod's `spec` is committed by the scheduler, so the env var will be present at container startup. The actual risk is a code bug: reading the wrong env var name (case sensitivity), reading `NODE_NAME` when the deployment injects it as `PHYSICAL_HOSTNAME`, or using a fallback that silently returns empty string instead of throwing.

**How to avoid:** Use the exact env var name that `deployment.yaml` injects. Currently `PHYSICAL_HOSTNAME` is used (from `spec.nodeName`). Do not introduce a second `NODE_NAME` var — reuse the existing one. Add a startup validator that checks `string.IsNullOrEmpty(nodeName)` and logs a warning or throws if the preferred-leader feature is enabled but no node name is available. Test locally by launching with `PHYSICAL_HOSTNAME=` (empty) and verifying the fallback behavior is deliberate, not silent.

**Warning signs:** `PreferredNode` is configured, `PHYSICAL_HOSTNAME` matches the node, but the pod never enters preferred mode. Logs show "this pod is non-preferred" on every election cycle. `Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME")` returns null in the production environment (check with a diagnostic log line at startup).

**Phase to address:** Configuration wiring (PreferredNode + env var binding).

---

### Pitfall 5: Stability Gate Clock Uses Wall Time — Pod Clock Skew Causes Premature or Delayed Preemption

**What goes wrong:** The stability gate (preventing flapping preemption: preferred pod must be stable for N seconds before stamping the heartbeat lease) uses `DateTimeOffset.UtcNow` or a `Stopwatch` started at pod startup. In a multi-node cluster, node clocks can skew by seconds even with NTP. The non-preferred pod reads the `renewTime` field of the heartbeat lease to determine freshness. `renewTime` is written by the preferred pod using its local clock. If the preferred pod's clock is fast by 5s and the non-preferred pod's clock is slow by 5s, the non-preferred pod sees `renewTime` as 10s newer than its own now, potentially treating a stale heartbeat as fresh.

**Why it happens:** K8s explicitly documents that the leader election client "does not consider timestamps in the leader election record to be accurate because these timestamps may not have been produced by a local clock." The same caveat applies to heartbeat lease timestamps read by non-preferred pods.

**How to avoid:** Build in a clock-skew tolerance into the freshness threshold. If the heartbeat interval is T seconds, the freshness threshold should be `T + max_clock_skew_tolerance` (e.g., T + 5s). Do not set the freshness threshold equal to the heartbeat interval. Document the assumed max clock skew (2-5s is typical in well-managed K8s clusters).

**Warning signs:** In test environments where all pods run on the same node (minikube, k3s single-node), this pitfall does not surface. It only appears in multi-node clusters. Symptom: non-preferred pod logs "preferred is fresh, backing off" for longer than expected after preferred pod crashes, or "preferred not fresh, no longer backing off" before the stability gate has actually expired.

**Phase to address:** Heartbeat freshness check implementation.

---

### Pitfall 6: `GracefulShutdownService` Does Not Know About the Heartbeat Lease

**What goes wrong:** The existing `GracefulShutdownService` has a hard-coded 5-step shutdown sequence. Step 1 calls `K8sLeaseElection.StopAsync` which deletes the leadership lease. The heartbeat lease has no equivalent cleanup step. If the preferred pod shuts down and the heartbeat lease is not deleted (relying on TTL expiry), the non-preferred leader will continue to back off for up to `leaseDuration` seconds after the preferred pod is gone, delaying the election stabilization. Worse: if the new preferred heartbeat service is a `BackgroundService`, it will be stopped by the host framework after `GracefulShutdownService.StopAsync` completes, meaning the framework stop order (reverse registration order) now affects heartbeat lease cleanup.

**Why it happens:** `GracefulShutdownService` was designed as the SINGLE orchestrator for shutdown steps. Adding a new lease without extending the shutdown sequence violates that design and creates an implicit dependency on framework stop ordering.

**How to avoid:** Extend `GracefulShutdownService` to include heartbeat lease cleanup as a new step (e.g., Step 1b: delete heartbeat lease if preferred pod). This keeps the shutdown sequence explicit and time-budgeted. Alternatively, design the heartbeat service to self-delete its lease in its own `StopAsync`, and document that `GracefulShutdownService` does not need to orchestrate it.

Choose one pattern and be explicit. The risk is subtle: if the heartbeat lease is deleted by the BackgroundService framework stop (after `GracefulShutdownService`), it happens AFTER leadership has already been released and the new leader may already be elected — making the heartbeat cleanup redundant but harmless. If it is NOT deleted, the TTL delay is the cost.

**Warning signs:** After a clean preferred-pod shutdown, the non-preferred leader delays preemption by exactly `heartbeatLeaseDuration` seconds. Logs on the non-preferred pod show "preferred is fresh, backing off" after the preferred pod has already exited.

**Phase to address:** Shutdown sequence extension (modify GracefulShutdownService or define heartbeat BackgroundService contract).

---

### Pitfall 7: Channel-Based Command Dispatch Does Not Drain Before Yield Handover Completes

**What goes wrong:** When the non-preferred leader yields (deletes leadership lease), `CommandWorkerService` continues draining the command `Channel<CommandRequest>` for the duration of the shutdown sequence (8s drain budget in Step 4 of `GracefulShutdownService`). However, after yield, the preferred pod may acquire leadership and start dispatching commands to the same devices concurrently — before the yielding pod has finished draining its channel. Both pods send SET commands to devices for the same tenant within the same SnapshotJob cycle window.

**Why it happens:** The channel drain (Step 4) happens after the leadership lease is deleted (Step 1). Between Step 1 and Step 4, the preferred pod has acquired leadership and started processing. The `CommandWorkerService` leadership gate (`if (!_leaderElection.IsLeader) return;`) correctly prevents new SETs after yield — but commands already in the channel buffer were enqueued before yield and will be executed during drain if `IsLeader` was true when they were dequeued.

Wait — the gate is checked per-command inside `ExecuteCommandAsync`. After `StopAsync` sets `_isLeader = false`, the gate will return early for all in-channel commands. This is the correct behavior. The actual risk is the opposite: the channel may have commands enqueued by the last SnapshotJob cycle before yield, and those commands are silently dropped (gate returns early) rather than being re-dispatched by the new leader.

**How to avoid:** Accept the silent drop as designed behavior. The new leader's SnapshotJob will re-evaluate tenants within the next cycle (15s) and re-dispatch any needed commands. Document this as "at-least-once evaluation, not at-least-once command" — consistent with the existing suppression cache design. Do NOT attempt to transfer the in-flight channel buffer to the new leader (this would require distributed queuing and is out of scope).

Ensure the suppression cache TTL (`SuppressionWindowSeconds`) is long enough that the new leader does not immediately re-dispatch the same command that the old leader just (partially) sent during its drain window.

**Warning signs:** After handover, a device receives a duplicate SET command within 15s. Check whether the suppression cache on the new leader is empty (new pod, no shared state) or whether the old leader's suppression entry had expired.

**Phase to address:** Yield handover design (document drop-and-re-evaluate contract explicitly).

---

### Pitfall 8: Two-Lease Write Conflicts If Both Pods Target the Same Lease Name

**What goes wrong:** The two-lease design uses separate lease names to avoid `resourceVersion` conflicts. If configuration or code has both the leadership lease name and heartbeat lease name set to the same value (e.g., both default to `"snmp-collector-leader"`), every write from the preferred heartbeat service will conflict with writes from `LeaderElector`, causing 409 Conflict responses, incremented retry counters, and degraded election reliability.

**Why it happens:** Config defaults often copy from the existing `LeaseOptions.Name`. A new `PreferredHeartbeatLeaseOptions.Name` with the same default as `LeaseOptions.Name` will silently produce the same lease name unless distinctly configured.

**How to avoid:** Assign a clearly distinct default name (e.g., `"snmp-collector-preferred-hb"`) in `PreferredHeartbeatLeaseOptions`. Add a startup validator that asserts `LeaseOptions.Name != PreferredHeartbeatLeaseOptions.Name` and throws `InvalidOperationException` at startup if they match. This is trivial to implement and prevents a silent, hard-to-diagnose failure.

**Warning signs:** Kubernetes API server returns `409 Conflict` on lease update operations. `K8sLeaseElection` logs increased renewal failures. Leadership oscillates or degrades. Both pod logs show `resourceVersion` mismatch errors on lease writes.

**Phase to address:** Configuration validation (startup validators for preferred lease options).

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Skip heartbeat lease cleanup on shutdown | Simpler service lifecycle | Delays non-preferred pod's election by up to TTL (15–30s) after preferred-pod restart | Acceptable if TTL is tuned to be short (< preferred pod's typical restart time) |
| Hardcode stability gate duration as a constant | Avoids new config option | Cannot tune per-environment without redeployment | Never — make it configurable from the start |
| Use `PHYSICAL_HOSTNAME` directly in options binding without fallback | Reuses existing env var | Silent empty-string match failures if var is absent | Acceptable only if a startup validator throws on empty |
| Share suppression cache state between old and new leader via external store | Prevents brief duplicate commands after handover | Adds Redis/external dependency, far exceeds problem scope | Never — at-least-once evaluation is the correct contract |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `LeaderElector` + preferred heartbeat service | Starting preferred heartbeat stamping immediately on pod startup, before stability gate expires | Gate heartbeat writes behind a `Task.Delay(stabilityWindowSeconds)` or a liveness check that ensures the pod has been up for N seconds |
| `GracefulShutdownService` + new heartbeat service | Assuming BackgroundService framework stop handles heartbeat lease cleanup in the correct order | Explicitly add heartbeat lease deletion to `GracefulShutdownService` or document that TTL-based expiry is acceptable |
| `PreferredNode` config + Downward API | Reading `NODE_NAME` when deployment injects `PHYSICAL_HOSTNAME` | Reuse the existing `PHYSICAL_HOSTNAME` env var; validate non-empty at startup |
| RBAC + heartbeat lease | Adding heartbeat lease in a different namespace than the leadership lease | Keep both leases in `simetra` namespace; existing `Role` covers all lease names within namespace |
| Freshness check + clock skew | Setting freshness threshold exactly equal to heartbeat interval | Add `+ clockSkewToleranceSeconds` (5s) to freshness threshold |
| `K8sLeaseElection.StopAsync` + preferred heartbeat stop | Stopping leadership lease and heartbeat lease in undefined order via framework | Define explicit order: leadership lease deletion first, then heartbeat lease deletion, matching `GracefulShutdownService` sequence |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Polling heartbeat lease on every SnapshotJob cycle (15s) via Kubernetes API | Increased API server load; watch quota exhaustion at scale | Use a dedicated background loop for heartbeat reads; cache last-read timestamp in memory; only re-read if cache is older than T seconds | At 3+ pods × 4 reads/min each — negligible at this scale, but sets a bad pattern |
| Preferred back-off check blocks SnapshotJob evaluation | SnapshotJob cycle duration increases; liveness vector staleness | Heartbeat freshness result should be a cached bool refreshed by background loop, not a synchronous API call in the job | Immediately if the K8s API is slow (>1s response) |

---

## "Looks Done But Isn't" Checklist

- [ ] **Heartbeat lease name:** Startup validator confirms it differs from leadership lease name — verify `LeaseOptions.Name != PreferredHeartbeatLeaseOptions.Name` throws at startup if equal.
- [ ] **NODE_NAME / PHYSICAL_HOSTNAME binding:** Startup log prints the resolved node name at INFO level — verify the correct env var name is used and non-empty.
- [ ] **RBAC:** Both lease names are in the `simetra` namespace — verify no cross-namespace lease is introduced.
- [ ] **Stability gate:** Preferred pod does not stamp heartbeat lease until stability window has elapsed — verify with a test that restarts preferred pod rapidly and confirms no preemption loop.
- [ ] **Shutdown sequence:** `GracefulShutdownService` either explicitly cleans up the heartbeat lease or the TTL-based expiry is documented as acceptable with its delay cost stated.
- [ ] **`OnStoppedLeading` handlers:** No destructive teardown in `OnStoppedLeading` — verify handlers are limited to `_isLeader = false` assignment.
- [ ] **Clock skew tolerance:** Freshness threshold is `heartbeatInterval + toleranceSeconds`, not `heartbeatInterval` exactly — verify in configuration or code.
- [ ] **Command channel drain:** Post-yield drain correctly short-circuits via `IsLeader` gate — verify `_isLeader = false` is set before channel drain step in shutdown sequence.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Destructive `OnStoppedLeading` (Pitfall 1) | HIGH — requires code fix and redeployment | Identify what state is cleared; restore it in `OnStartedLeading`; or move teardown to `CancellationToken` path only |
| Heartbeat lease 404 window causes wrong election (Pitfall 2) | LOW — self-heals in one heartbeat interval | Tune TTL to cover restart time; add freshness threshold tolerance; document expected transient window |
| RBAC 403 on heartbeat lease (Pitfall 3) | MEDIUM — requires `rbac.yaml` update and re-apply | Apply corrected Role/RoleBinding; pod will self-heal without restart if RBAC is updated while running |
| Empty NODE_NAME (Pitfall 4) | LOW — configuration fix | Set correct env var in deployment.yaml; rolling restart |
| Clock skew causing wrong freshness (Pitfall 5) | LOW — configuration tuning | Increase `clockSkewToleranceSeconds`; rolling restart |
| Duplicate command dispatch during handover (Pitfall 7) | LOW — at-most transient; suppression cache will catch subsequent repeats | Tune `SuppressionWindowSeconds` to exceed handover window |
| Same lease name for both leases (Pitfall 8) | MEDIUM — requires configuration fix and restart | Rename heartbeat lease in config; rolling restart |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| `OnStoppedLeading` fires on transient loss (Pitfall 1) | Two-lease coordination implementation | Unit test: mock `LeaderElector`, fire `OnStoppedLeading` twice, verify no state corruption on second call |
| Heartbeat lease 404 window (Pitfall 2) | Heartbeat lifecycle design | E2E test: kill preferred pod cleanly, confirm non-preferred leader does not preempt within TTL window |
| RBAC missing for heartbeat lease (Pitfall 3) | Pre-implementation RBAC review | Smoke test: deploy both leases in same namespace, verify no 403 in pod logs |
| Empty NODE_NAME / wrong env var (Pitfall 4) | Configuration wiring | Startup validator test: set `PHYSICAL_HOSTNAME=""`, verify startup throws/warns |
| Clock skew freshness threshold (Pitfall 5) | Heartbeat freshness check implementation | Code review: confirm `freshness = interval + tolerance`; document tolerance value |
| Shutdown sequence missing heartbeat lease (Pitfall 6) | GracefulShutdownService extension | Verify shutdown sequence steps list includes heartbeat lease deletion or TTL policy is documented |
| Command channel drain after yield (Pitfall 7) | Yield handover design doc | Manual test: trigger yield mid-SnapshotJob cycle, verify no duplicate SETs reach device within suppression window |
| Same lease name collision (Pitfall 8) | Configuration validation | Startup validator: assert names differ; unit test the validator |

---

## Sources

- kubernetes-client/csharp `LeaderElector.cs` source: [GitHub](https://github.com/kubernetes-client/csharp/blob/master/src/KubernetesClient/LeaderElection/LeaderElector.cs) — confirmed `OnStoppedLeading` fires on every inner-loop exit; `RunAndTryToHoldLeadershipForeverAsync` retries after loss (HIGH confidence)
- client-go `leaderelection.go` source: [GitHub](https://github.com/kubernetes/client-go/blob/master/tools/leaderelection/leaderelection.go) — confirmed timing parameter relationships (`LeaseDuration > RenewDeadline > RetryPeriod × 1.2`); clock skew caveat documented explicitly; `OnStoppedLeading` not guaranteed to fire only after `OnStartedLeading` (HIGH confidence)
- Kubernetes Downward API docs: [kubernetes.io](https://kubernetes.io/docs/concepts/workloads/pods/downward-api/) — `spec.nodeName` available via env var at container start; only env var (not volume) is supported for `spec.nodeName` (HIGH confidence)
- client-go split brain issue: [kubernetes/kubernetes #67651](https://github.com/kubernetes/client-go/issues/67651) — confirmed split-brain window exists between lease deletion and non-preferred pod acquisition (MEDIUM confidence)
- Project source code: `K8sLeaseElection.cs`, `GracefulShutdownService.cs`, `CommandWorkerService.cs`, `deployment.yaml`, `rbac.yaml` — verified integration points directly (HIGH confidence)

---
*Pitfalls research for: preferred leader election with site-affinity added to existing K8s SNMP monitoring system*
*Researched: 2026-03-25*
