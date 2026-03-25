# Project Research Summary

**Project:** Simetra119 SNMP Collector — v3.0 Preferred Leader Election (Site-Affinity)
**Domain:** Distributed K8s leader election with topological preference — two-lease mechanism
**Researched:** 2026-03-25
**Confidence:** HIGH

## Executive Summary

The v3.0 milestone adds site-affinity to an existing, working K8s Lease-based leader election system. The problem is well-understood: the SNMP collector pod co-located with monitored network devices at a given site should hold leadership at steady state because it has the lowest round-trip latency to those devices. The recommended approach is a two-lease pattern — the existing leadership lease remains the single source of truth for who is leader, and a new heartbeat lease (written only by the preferred pod) acts as an advisory presence signal. Non-preferred pods read the heartbeat stamp and either back off (if preferred is healthy) or compete normally (if preferred is absent). This pattern works on any standard Kubernetes version with no Alpha feature gates and requires no new NuGet packages.

The existing codebase is well-positioned for this extension. `K8sLeaseElection`, `GracefulShutdownService`, `ILeaderElection`, and all downstream consumers (`MetricRoleGatedExporter`, `CommandWorkerService`) are preserved without interface changes. Three new files are created (`SiteAffinityOptions`, `IPreferredStampReader`, `PreferredHeartbeatService`) and one existing file is surgically modified (`K8sLeaseElection.cs`) to add two decision gates: backoff before competing, and voluntary yield while leading. The feature is fully backward compatible — when `PreferredNode` config is absent, behavior is identical to today.

The highest implementation risk is modifying the core election loop in `K8sLeaseElection`. The mechanism uses a cooperative inner `CancellationTokenSource` to interrupt the `LeaderElector` library's `RunAndTryToHoldLeadershipForeverAsync` without fighting its internal state machine. Clock skew across nodes, heartbeat lease lifecycle during pod restarts, and shutdown sequence ordering for two leases are the pitfalls most likely to produce subtle bugs in production that do not surface in single-node test environments.

---

## Key Findings

### Recommended Stack

No new NuGet packages are required. `KubernetesClient` 18.0.13 (already present) exposes the full `CoordinationV1` API needed for heartbeat lease CRUD: `CreateNamespacedLeaseAsync`, `ReadNamespacedLeaseAsync`, `ReplaceNamespacedLeaseAsync`, and `DeleteNamespacedLeaseAsync`. The existing `Microsoft.Extensions.Hosting` `BackgroundService` base class handles the new `PreferredHeartbeatService`. The only deployment change is adding Downward API environment variables to the Kubernetes Deployment manifest — specifically reusing the existing `PHYSICAL_HOSTNAME` (injected from `spec.nodeName`) rather than introducing a new `NODE_NAME` alias.

**Core technologies:**
- `KubernetesClient` 18.0.13: all four `CoordinationV1` Lease CRUD methods confirmed present and in use today — no upgrade needed
- `Microsoft.Extensions.Hosting` 9.0.0: `BackgroundService` for `PreferredHeartbeatService` — no change to hosting model
- K8s Downward API (`spec.nodeName` → `PHYSICAL_HOSTNAME` env var): pod self-identifies as preferred at startup without any Kubernetes API call
- `Microsoft.Extensions.Options` with `[Range]` validators: bounds-checking for new `SiteAffinityOptions` (cross-field: `HeartbeatRenewIntervalSeconds < HeartbeatDurationSeconds`)

Do not upgrade `KubernetesClient` to 19.0.2. The newer version adds kubectl-layer helpers irrelevant to this feature. Do not use `preferredHolder` / `strategy` fields on `V1LeaseSpec` — these are Alpha-gated and unavailable on standard clusters.

### Expected Features

**Must have (table stakes — P1, this milestone):**
- `PreferredNode` configuration field — feature is fully inert (backward compatible) when absent or empty
- `PHYSICAL_HOSTNAME` env var wired to `spec.nodeName` in Deployment manifest (reuse existing var, do not add a new one)
- `PreferredHeartbeatService` — stamps heartbeat lease after readiness gate passes; lifecycle on shutdown (TTL-based expiry preferred over explicit delete)
- Non-preferred backoff — poll heartbeat stamp before leadership retry; extend retry delay when stamp is fresh
- Voluntary yield — non-preferred leader releases leadership lease when preferred stamp becomes fresh
- Graceful heartbeat lease handling on shutdown — TTL expiry or explicit deletion, choice must be documented and deliberate
- Structured log at each preferred-election decision point (INFO level)

**Should have (operational excellence — P2, after v1 validation):**
- `is_preferred_node` metric label — enables alerting on "preferred pod not leading for > N minutes" SLO
- Configurable `PreferredStaleThresholdSeconds` — per-environment tuning after observing actual stamp timing
- `preferredNodeIsLeading` health check field — for monitoring systems that scrape `/health/ready`

**Defer (v2+):**
- K8s Event on voluntary yield — useful for cluster audit but low priority if log lines suffice
- Multi-level priority chain (site-1 > site-2 > site-3) — not needed for current binary topology
- Dynamic preferred-node based on traffic topology — requires a separate monitoring component, out of scope

### Architecture Approach

The two-lease architecture is strictly additive. `ILeaderElection` and all its consumers are unchanged. A new `IPreferredStampReader` interface (single `bool IsPreferredStampFresh` property) decouples the heartbeat reader from the election logic and enables unit testing without a live Kubernetes cluster. `PreferredHeartbeatService` implements both `BackgroundService` and `IPreferredStampReader`: on the preferred pod it runs a write loop stamping the heartbeat lease after readiness passes; on non-preferred pods it runs a periodic read loop updating an in-memory `volatile bool`. `K8sLeaseElection` reads `IsPreferredStampFresh` as a local boolean — zero network calls in the election gate path.

**Major components:**
1. `SiteAffinityOptions` — config: `PreferredNode`, `HeartbeatLeaseName`, `HeartbeatDurationSeconds` (30s default), `HeartbeatRenewIntervalSeconds` (10s default); located in `Configuration/`
2. `IPreferredStampReader` — narrow interface; `PreferredHeartbeatService` implements it; `K8sLeaseElection` consumes it via DI
3. `PreferredHeartbeatService` — dual-path `BackgroundService`: writer (preferred pod, post-readiness gate) or reader (non-preferred pod, periodic poll); owns heartbeat lease lifecycle
4. `K8sLeaseElection` (modified) — adds `_isPreferredPod` flag, `_innerCts` for cooperative cancellation, Gate 1 (backoff before acquire), Gate 2 (yield while leading)
5. `ServiceCollectionExtensions` (modified) — binds `SiteAffinityOptions`, registers `PreferredHeartbeatService` as singleton + hosted service + `IPreferredStampReader`

Files not touched: `ILeaderElection.cs`, `AlwaysLeaderElection.cs`, `MetricRoleGatedExporter.cs`, `CommandWorkerService.cs`. `GracefulShutdownService.cs` may receive an optional explicit heartbeat cleanup step but is not required.

### Critical Pitfalls

1. **`OnStoppedLeading` fires on every transient leadership loss, not just permanent exit** — keep the handler idempotent (`_isLeader = false` only); never perform destructive teardown inside it. The forever-loop re-enters after transient loss; destructive handlers corrupt re-acquisition. Verified against kubernetes-client/csharp `LeaderElector.cs` source.

2. **Heartbeat lease 404 window during preferred pod restart causes spurious election** — do NOT explicitly delete the heartbeat lease on shutdown; let TTL expire. Set heartbeat TTL to at least 2× `terminationGracePeriodSeconds`. Non-preferred pods must treat 404 identically to a stale timestamp — absence is not an instant "preferred is down" signal.

3. **Clock skew across nodes corrupts freshness decisions** — set freshness threshold to `heartbeatInterval + toleranceSeconds` (add 5s tolerance), never exactly equal to the heartbeat interval. This pitfall does not surface in minikube or k3s single-node test environments; it only appears in multi-node clusters.

4. **Wrong env var name silently disables preferred mode** — the existing deployment injects `PHYSICAL_HOSTNAME` from `spec.nodeName`, not `NODE_NAME`. Read `PHYSICAL_HOSTNAME`. Add a startup validator that logs a warning or throws when `PreferredNode` is configured but the node-name env var resolves to empty.

5. **Both lease names must differ; startup validator must enforce it** — heartbeat lease default name (`snmp-collector-preferred`) must not match the leadership lease name. A misconfiguration causes `409 Conflict` on every write and degrades election reliability silently; a startup validator asserting `LeaseOptions.Name != SiteAffinityOptions.HeartbeatLeaseName` costs two lines and prevents a hard-to-diagnose failure.

---

## Implications for Roadmap

The build order is dictated by strict dependency flow: config before interface, interface before service, service before gate wiring. Each phase is independently committable and testable. Six phases are suggested.

### Phase 1: Config and Interface Foundation

**Rationale:** `SiteAffinityOptions` and `IPreferredStampReader` are pure declarations with zero runtime risk. Establishing them first locks the dependency direction before any behavioral code exists. Startup validators (lease name collision, empty node name) belong here and protect every subsequent phase.

**Delivers:** `SiteAffinityOptions.cs`, `SiteAffinityOptionsValidator.cs`, `IPreferredStampReader.cs`, DI registration in `ServiceCollectionExtensions` (options bind + validator only). App starts and runs identically to today.

**Addresses:** `PreferredNode` configuration field (table stakes), Pitfall 8 (lease name collision guard), Pitfall 4 (empty node name guard).

**Avoids:** Pitfall 8 caught at startup before any election logic runs.

---

### Phase 2: PreferredHeartbeatService — Reader Path (Non-Preferred Poll Loop)

**Rationale:** The non-preferred pod's poll loop is pure read — it never writes a lease. It is the lower-risk half of the service. Building it first proves the `IPreferredStampReader` contract, the freshness computation (with clock-skew tolerance baked in), and the DI singleton wiring before the write path is added. All non-preferred pods now maintain an in-memory freshness bool, but no gates in `K8sLeaseElection` consume it yet — behavior is still identical to today.

**Delivers:** `PreferredHeartbeatService.cs` (non-preferred branch only). `IPreferredStampReader.IsPreferredStampFresh` returns a real value derived from polling the heartbeat lease via `ReadNamespacedLeaseAsync`.

**Uses:** `CoordinationV1.ReadNamespacedLeaseAsync`, `volatile bool _isStampFresh`, `HeartbeatRenewIntervalSeconds` poll cadence.

**Addresses:** Pitfall 5 (clock skew) — freshness threshold = `HeartbeatDurationSeconds + 5s tolerance` wired here. Pitfall 3 (RBAC) — both leases in `simetra` namespace confirmed before any writes attempted.

---

### Phase 3: PreferredHeartbeatService — Writer Path and Readiness Gate

**Rationale:** The preferred pod's write loop is higher risk because it creates a real Kubernetes resource that other pods will read. The readiness gate is the critical correctness property: a pod stamping before it is genuinely ready causes the non-preferred leader to yield to an unready pod, creating a true monitoring gap. The readiness gate mechanism must be decided in this phase (three options: `IHostApplicationLifetime.ApplicationStarted`, `IHealthCheckService` poll, or shared `TaskCompletionSource`). The TTL-based shutdown strategy (do not delete heartbeat lease on graceful shutdown) must also be confirmed here.

**Delivers:** Preferred pod writes and renews heartbeat lease after readiness passes. `PreferredHeartbeatService` is now fully functional on both paths. Heartbeat lease lifecycle documented and tested.

**Addresses:** Stability gate (table stakes), Pitfall 2 (heartbeat lease 404 window — TTL-based expiry decided here), ARCHITECTURE.md Anti-Pattern 4 (stamping before readiness).

**Avoids:** Premature preemption loop when preferred pod crash-restarts.

---

### Phase 4: K8sLeaseElection — Gate 1 (Backoff Before Acquire)

**Rationale:** Gate 1 is additive to the election loop — it only delays the retry, it does not change what happens when the election runs. The worst case of a Gate 1 bug is no backoff (current behavior), not an incorrect yield. This phase introduces the outer loop with `_innerCts` even though Gate 2 is not yet active — the structural change is made once, not twice.

**Delivers:** Non-preferred pods delay leadership retry when preferred stamp is fresh. Preferred pod behavior unchanged. The `_innerCts`-based outer loop structure is in place and tested.

**Addresses:** Non-preferred backoff (table stakes P1), Pitfall 1 (`OnStoppedLeading` idempotency — validated as part of the outer loop restructure).

**Avoids:** Fighting the `LeaderElector` library — the cooperative `_innerCts` cancellation pattern must be proven here before Gate 2 depends on it.

---

### Phase 5: K8sLeaseElection — Gate 2 (Voluntary Yield While Leading)

**Rationale:** Gate 2 is the highest-risk change: it causes an in-production leadership transfer from a running leader to the preferred pod. It depends on Gate 1 and the `_innerCts` structure being proven in Phase 4. The yield sequence (cancel inner CTS → `OnStoppedLeading` fires → `_isLeader = false` → lease deletion → preferred pod acquires) must be tested end-to-end.

**Delivers:** Non-preferred leader releases leadership lease when preferred stamp becomes fresh. Preferred pod re-acquires within `RetryPeriod`. Full site-affinity behavior at steady state and after preferred pod recovery.

**Addresses:** Voluntary yield (table stakes P1 — the hardest feature), Pitfall 7 (command channel drain after yield — document drop-and-re-evaluate contract explicitly), Pitfall 6 (shutdown sequence for two leases — explicit decision here).

**Avoids:** Pitfall 1 — `OnStoppedLeading` must only set `_isLeader = false`; the yield path replicates lease deletion logic directly rather than calling `StopAsync` (which would also cancel the host).

---

### Phase 6: Observability and Deployment Wiring

**Rationale:** The mechanism is correct after Phase 5. This phase adds the structured logs, metric label, and deployment manifest changes that make the system operatable in production. Observability is designed last because the state transitions are concrete and the log messages can name the exact conditions observed during Phase 4-5 testing.

**Delivers:** Structured log lines at each election decision ("preferred stamp fresh, delaying retry", "yielding to preferred pod X", "preferred stamp stale, competing normally"). `is_preferred_node` metric label (P2). Deployment manifest update confirming `PHYSICAL_HOSTNAME` from `spec.nodeName` and `SiteAffinity` config section in ConfigMap / `appsettings.Production.json`. E2E validation checklist execution per ARCHITECTURE.md Step 6.

**Addresses:** Structured logs (table stakes P1), `is_preferred_node` metric label (P2 differentiator), deployment wiring (Pitfall 4 — node name env var verified in production manifest).

---

### Phase Ordering Rationale

- **Config before behavior:** Phases 1-2 establish wiring and read path with zero behavioral change. These can be deployed to production without any election impact.
- **Read before write:** Non-preferred reader path (Phase 2) is testable in isolation. The preferred writer path (Phase 3) creates real Kubernetes resources and must be sequenced after the read path is proven.
- **Gate 1 before Gate 2:** Backoff (Gate 1) is recoverable if wrong (worst case: no backoff = today's behavior). Voluntary yield (Gate 2) triggers an irreversible leadership transfer during the election cycle.
- **Observability last:** Log messages and metric labels are authored against observed behavior, not speculation. This avoids instrumenting code paths that change during Phase 4-5 development.

### Research Flags

Phases with standard patterns (skip `/gsd:research-phase`):
- **Phase 1 (Config/Interface):** Standard `SectionName` options pattern already used by `LeaseOptions`. Validator pattern already used by existing options validators. No unknowns.
- **Phase 2 (Reader Path):** Simple Kubernetes API read with TTL comparison. All methods confirmed in `KubernetesClient` 18.0.13. No unknowns.
- **Phase 6 (Observability):** Follows existing log/metrics patterns directly. No unknowns.

Phases that benefit from a focused implementation spike before coding:
- **Phase 3 (Readiness Gate):** Three viable readiness gate options exist (`IHostApplicationLifetime.ApplicationStarted`, `IHealthCheckService` poll, shared `TaskCompletionSource<bool>`). A quick inspection of how `ReadinessHealthCheck` is wired internally — and whether it exposes a signal that can be shared — resolves the choice before writing the writer path. Picking the wrong option means rework when the readiness ordering is tested under load.
- **Phase 5 (Voluntary Yield):** The cooperative `_innerCts` / `LeaderElector` interaction should be validated with a minimal unit test harness before modifying the live election loop. Key question: does `LeaderElector` clean up internal state correctly when its token is cancelled mid-renewal, or does it leave a stale `resourceVersion` that causes the next create/replace to fail with 409?

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All claims from direct codebase inspection + NuGet registry + official K8s API docs. No new packages. KubernetesClient 18.0.13 confirmed sufficient via existing `DeleteNamespacedLeaseAsync` call site. |
| Features | HIGH | Table stakes and anti-features derived from direct reads of `K8sLeaseElection.cs`, `GracefulShutdownService.cs`, `MetricRoleGatedExporter.cs`; supplemented by Amazon Builders' Library and Azure Architecture Center. |
| Architecture | HIGH | All integration points verified against named source files. Build order validated by dependency graph. `LeaderElector` cooperative cancellation pattern confirmed against kubernetes-client/csharp source. |
| Pitfalls | HIGH (K8s library behavior) / MEDIUM (operational edge cases) | `OnStoppedLeading` transient-fire behavior confirmed against library source. Clock skew and 404-window pitfalls derived from multi-source reasoning and official K8s docs on clock skew in lease-based election. |

**Overall confidence:** HIGH

### Gaps to Address

- **Readiness gate mechanism selection (Phase 3):** Three implementation options identified; the correct choice depends on how `ReadinessHealthCheck` exposes a healthy signal. Resolve with a brief code inspection before starting Phase 3.

- **GracefulShutdownService extension decision (Phase 5):** Two valid paths — (a) explicit heartbeat lease deletion as a new step in `GracefulShutdownService`, or (b) TTL-based expiry. The TTL-based approach is acceptable if `HeartbeatDurationSeconds` is tuned to be shorter than the preferred pod's typical restart time. This choice must be made explicitly and documented in the Phase 5 plan — leaving it implicit invites the Pitfall 6 failure mode.

- **`PHYSICAL_HOSTNAME` vs `NODE_NAME` naming:** The existing deployment injects `PHYSICAL_HOSTNAME` from `spec.nodeName`. The STACK.md research references `NODE_NAME` as a conceptual name. The implementation must use the exact existing var name. Confirm in `deployment.yaml` before Phase 1 to ensure the startup validator checks the right name.

- **`LeaderElector` state after cancellation (Phase 5 spike):** It is unconfirmed whether `LeaderElector` correctly handles mid-renewal cancellation without leaving a stale `resourceVersion` that causes the next election cycle's write to fail with 409 Conflict. This is the primary unknowing entering Phase 5.

---

## Sources

### Primary (HIGH confidence — direct codebase inspection)
- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — existing election loop, `OnStoppedLeading` usage, `DeleteNamespacedLeaseAsync` pattern, `RunAndTryToHoldLeadershipForeverAsync` call site
- `src/SnmpCollector/Lifecycle/GracefulShutdownService.cs` — shutdown step sequence and time budget
- `src/SnmpCollector/Configuration/LeaseOptions.cs` — existing config shape, `SectionName` pattern, validation ranges
- `src/SnmpCollector/Configuration/PodIdentityOptions.cs` — `PHYSICAL_HOSTNAME` env var usage (precedent for Downward API node name resolution)
- `src/SnmpCollector/HealthChecks/ReadinessHealthCheck.cs` — readiness gate conditions, `HealthCheckResult` data dictionary pattern
- `src/SnmpCollector/SnmpCollector.csproj` — `KubernetesClient` 18.0.13 confirmed
- kubernetes-client/csharp `LeaderElector.cs` source (GitHub) — `OnStoppedLeading` transient-fire behavior confirmed; `RunAndTryToHoldLeadershipForeverAsync` loop structure
- client-go `leaderelection.go` source (GitHub) — timing parameter relationships (`LeaseDuration > RenewDeadline > RetryPeriod × 1.2`); clock skew caveat documented explicitly
- Kubernetes Lease API reference (kubernetes.io) — `V1LeaseSpec` fields, `preferredHolder` / `strategy` confirmed Alpha-gated
- Kubernetes Downward API docs (kubernetes.io) — `spec.nodeName` env-var-only availability confirmed (not available as volume file)

### Secondary (MEDIUM confidence)
- Amazon Builders' Library: Leader Election in Distributed Systems — lease semantics, zero-or-two-leaders correctness window, correctness invariant guidance
- Microsoft Azure Architecture Center: Leader Election pattern — graceful handoff, nondeterministic election, lease-based mutex
- kubernetes/kubernetes #67651 — split-brain window confirmed between lease deletion and non-preferred pod acquisition

### Tertiary (LOW confidence)
- Topology Aware Leader Election for Dynamic Networks (IEEE, 2021) — closeness centrality as election criterion; academic framing only, not directly applied to implementation

---
*Research completed: 2026-03-25*
*Ready for roadmap: yes*
