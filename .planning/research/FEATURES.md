# Feature Research

**Domain:** Preferred/priority leader election with site-affinity for a K8s-deployed SNMP monitoring system
**Researched:** 2026-03-25
**Confidence:** HIGH — derived from direct source reads of K8sLeaseElection.cs, GracefulShutdownService.cs,
MetricRoleGatedExporter.cs, ReadinessHealthCheck.cs, LeaseOptions.cs, PodIdentityOptions.cs; supplemented
by Amazon Builders' Library, Microsoft Azure Architecture Center, and Kubernetes operator observability
research from multiple verified sources.

---

## Scope

This research defines the feature landscape for adding preferred-node leader election with site-affinity
to an existing K8s SNMP monitoring system. The system already has fair K8s Lease election (any pod can
win), graceful lease release on shutdown, and leader-gated metric/command export. The goal is to add
topological preference: the pod co-located with monitored devices at a given site should be the preferred
leader because it has lowest SNMP round-trip latency.

**Deployment topology:** 1 K8s cluster, 3 nodes (one node per physical site), 3 replicas (one pod per
node). Pod-to-site affinity is already established via K8s node affinity rules. Leadership preference
means: at steady-state, the pod on the site's node should hold the leader lease.

**Design already decided (by milestone context):**
- Two-lease mechanism: existing leadership lease + new preferred heartbeat lease
- `PreferredNode` config field; pod compares `NODE_NAME` env var to determine if it is preferred
- Non-preferred pods back off (longer retry delay) when the preferred stamp is fresh
- Non-preferred leader voluntarily yields by releasing the leadership lease when preferred recovers
- Stability gate: preferred pod only stamps the heartbeat lease after passing readiness

**What already exists that this milestone builds on:**
- `K8sLeaseElection` — single leadership lease, fair election, graceful release on shutdown
- `GracefulShutdownService` — lease release is step 1 of the shutdown sequence (3s budget)
- `ILeaderElection` / `MetricRoleGatedExporter` — consumers of `IsLeader` flag
- `PodIdentityOptions` — pod identity for lease holder field
- `LeaseOptions` — lease name, namespace, duration, renew interval
- `ReadinessHealthCheck` — readiness gate already checks trap listener + scheduler

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features that an operator running a site-affinity deployment expects. Missing any of these breaks the
advertised topology guarantee or makes the system unoperatable.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Preferred-node configuration field | Operators must be able to declare which node name is preferred per deployment without code changes. If preference cannot be configured, the feature is not deployable across different environments. | LOW | Single `PreferredNode` string in config (e.g., `Lease:PreferredNode`). Pod reads `NODE_NAME` env var (injected by K8s downward API) and compares. If match: this pod is preferred candidate. |
| Preferred pod stamps a heartbeat lease | Non-preferred pods must be able to observe that the preferred pod is alive and healthy before backing off. Without a shared signal, non-preferred pods have no way to differentiate "preferred pod is healthy and should lead" from "preferred pod is absent". | MEDIUM | Second K8s Lease resource (e.g., `snmp-collector-preferred`). Preferred pod writes its identity as holder + updates `renewTime` on a timer. Non-preferred pods watch or poll `renewTime` age. This is the mechanism; the lease is not a true election lease, only a liveness stamp. |
| Stability gate before preferred pod stamps | If the preferred pod stamps immediately on startup before it is actually ready, non-preferred pods will back off before the preferred pod can serve traffic. This causes a window where no pod is actively leading. The stamp must wait for readiness. | LOW | Preferred pod begins stamping only after `ReadinessHealthCheck` passes (trap listener bound + scheduler running). Already have readiness infrastructure. Stamp loop starts after a readiness confirmation check, not immediately on startup. |
| Non-preferred pods back off when preferred stamp is fresh | Without backoff, all pods compete with equal aggressiveness. The preferred pod wins by chance over time, not topology. Operators expect preferred-node leadership to be the normal steady-state, not a random outcome. | MEDIUM | Non-preferred pod checks preferred heartbeat lease age. If stamp age < threshold (e.g., 2x the preferred renew interval), non-preferred pod extends its retry delay before attempting leadership lease acquisition. This is a soft preference: fair election still runs if stamp is stale. |
| Non-preferred leader voluntarily yields when preferred recovers | If a non-preferred pod is currently leader (because preferred was absent), and the preferred pod recovers and starts stamping, the non-preferred leader must release the leadership lease voluntarily. Without voluntary yield, the non-preferred leader holds the lease until TTL expiry (up to `DurationSeconds`), creating a latency penalty for up to 15 seconds. | HIGH | Non-preferred leader polls preferred heartbeat lease while holding leadership. If stamp becomes fresh and pod identity differs from self: release the leadership lease (same mechanism as graceful shutdown's `DeleteNamespacedLeaseAsync`). Triggers immediate re-election, preferred pod wins. |
| Fair fallback when preferred pod is absent | If the preferred pod is down or not stamping, non-preferred pods must be able to lead normally. The preference mechanism must degrade gracefully: no preferred stamp = fair election with no backoff. Operators expect HA to be maintained regardless of which node is preferred. | LOW | Non-preferred pods skip backoff logic if stamp age exceeds threshold (stamp is stale = preferred pod not healthy). They compete normally. This is the same behavior as today's fair election. |
| Leadership lease identity remains the main election | The preferred heartbeat lease is advisory, not a second election lease. The Kubernetes Lease API must still be the single source of truth for who is leader at any moment. Operators expect that `kubectl get lease snmp-collector-leader` is the authoritative answer to "who is leader?" | LOW | The existing `K8sLeaseElection` is not replaced — it is augmented. Preferred heartbeat lease is a separate resource. `ILeaderElection.IsLeader` still reads from the primary leadership lease only. |
| Node name available as env var in pod spec | The preferred node comparison requires the pod to know which node it is scheduled on. In K8s this is available via the downward API. If `NODE_NAME` is not injected, the preference mechanism silently fails to identify the preferred pod. | LOW | Already a K8s pattern: `env: [{name: NODE_NAME, valueFrom: {fieldRef: {fieldPath: spec.nodeName}}}]` in pod spec. Needs to be wired in deployment manifest. Existing `PodIdentityOptions` pattern can be extended to read this. |
| Graceful shutdown releases both leases | If the preferred pod shuts down, it must release both the leadership lease (already done) and the preferred heartbeat lease. If the heartbeat lease is not released, non-preferred pods will observe a stale stamp for up to `DurationSeconds` and continue backing off, delaying re-election. | MEDIUM | `GracefulShutdownService` already orchestrates lease release in step 1. This step must be extended (or a second step added) to delete the preferred heartbeat lease if this pod is preferred. Budget remains 3s total for both releases. |

### Differentiators (Operational Excellence)

Features that go beyond basic correctness and make the system maintainable and diagnosable in production.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Structured log on each preferred-election state transition | Without logs, operators cannot tell from `kubectl logs` why a non-preferred pod yielded or why the preferred pod delayed its stamp. A structured log line at each decision point ("preferred stamp fresh, backing off", "yielding to preferred pod X", "preferred stamp stale, competing normally") is the minimum observability for this mechanism. | LOW | Log at INFO level: pod identity, preferred node name, observed stamp age, decision taken. These are rare events (only on topology change), so log volume is negligible. Consistent with existing leadership acquisition/loss log lines in K8sLeaseElection. |
| `is_preferred_node` label on leadership metrics | Operators viewing metrics should be able to distinguish "this is the preferred pod" from "this is a fallback leader". A label on the existing `role` telemetry or a new gauge allows alerting on "preferred node is not leader for > N minutes" which is the site-affinity SLO. | LOW | Add `is_preferred` boolean label (or a separate gauge `snmp_leader_is_preferred{value=1/0}`) exported by all pods. Follows existing label taxonomy. MetricRoleGatedExporter gates business metrics; this label is an operational metric, exported by all instances. |
| Configurable stamp freshness threshold | Different environments have different network reliability. A hard-coded freshness threshold (e.g., always 2x renew interval) may be too tight in high-latency management networks or too loose in fast LANs. Making it configurable allows operators to tune without redeployment. | LOW | New config field `Lease:PreferredStaleThresholdSeconds`. Defaults to `DurationSeconds` (same as leadership lease TTL) if not set. Validator enforces > 0 and < some max. |
| Leader role label in log enrichment on preferred transitions | When the preferred pod yields or when non-preferred backs off, the log lines should include the current `role` tag ("leader" / "follower") consistent with existing `SnmpLogEnrichmentProcessor`. Operators filtering logs by `role=leader` should see both the acquisition log and the yield log. | LOW | No new code if `SnmpLogEnrichmentProcessor` already enriches all logs with `CurrentRole`. Yield event happens before `IsLeader` is set to false, so the log line will correctly carry `role=leader`. Verify enrichment order at yield call site. |
| K8s Event emitted on voluntary yield | In addition to pod logs, a K8s Event on the leadership Lease resource gives cluster-level visibility that does not require log access. Operators using `kubectl describe lease snmp-collector-leader` see the voluntary yield as an event alongside normal Kubernetes lease activity. | MEDIUM | Use `IKubernetes.CoreV1.CreateNamespacedEventAsync(...)` with reason="VoluntaryYield" and message including preferred pod identity. This is fire-and-forget; failure to emit the event is not fatal. Pattern used by K8s controllers but not yet by this application. |
| Health check annotation for preferred-node status | The existing `/health/ready` endpoint returns trap listener + scheduler status. Adding a `preferredNodeLeading` field to the readiness data allows monitoring systems that scrape health endpoints to detect the site-affinity SLO without parsing metrics. | LOW | `ReadinessHealthCheck` returns `HealthCheckResult.Healthy(data: ...)` with a dictionary. Add `preferredNodeIsLeading` bool key. Value: `true` if `this pod is preferred AND is leader OR this pod is not preferred`. Always `true` for non-preferred pods since they are not responsible for site-affinity. Only `false` when this pod is preferred but not leader. |

### Anti-Features (Deliberately NOT Built)

Features that seem natural extensions but would create correctness, complexity, or operational problems.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Hard preemption: preferred pod forcibly evicts non-preferred leader | Operators may assume preferred-node leadership means the preferred pod should always win, even if it means evicting the current leader immediately on startup | Hard preemption via deleting the leadership lease from the preferred pod creates a brief leadership gap (the delete + new pod acquiring). During this gap, `IsLeader` transitions: metric export stops, command dispatch stops. For SNMP monitoring, a 2-5s gap in metric export is acceptable, but if the preferred pod has not passed readiness yet, the gap causes a true outage. Hard preemption also requires the preferred pod to authenticate against the Kubernetes API with delete permission on a lease it does not hold, which is a larger RBAC surface. | Voluntary yield from the non-preferred leader is equivalent: the current leader releases the lease, preferred pod acquires it. The gap is identical (one lease TTL or delete latency). Voluntary yield is initiated by the non-preferred pod, which already holds the lease and has delete permission. |
| Weighted election: multiple preferred nodes in priority order | Operators may want site-1-node > site-2-node > site-3-node (a priority chain rather than binary preferred/non-preferred) | A multi-level priority chain requires the heartbeat lease to carry a priority integer, and all pods to compare their own priority against the current leader's priority. This is a significant increase in coordination complexity: three pods each watching a different pod's heartbeat, with potentially simultaneous yields. The current deployment topology (one preferred, two followers) does not require this generality. | Binary preferred/non-preferred covers the stated use case. If future deployments need multi-level priority, this is a v2 consideration. Document the design intent clearly so it can be extended without rework. |
| Automatic preferred-node rotation (leader follows traffic) | Some operators ask for dynamic preferred assignment where the "preferred" node rotates based on which site has the most active devices or most recent traps | Dynamic preference requires a separate traffic-monitoring component that writes the preferred designation into config or a config map, plus the election mechanism reading that config map dynamically. This is a separate system. The latency benefit of site-affinity is only meaningful when the pod is co-located with its monitored devices, which is a static topology in this deployment. | Static `PreferredNode` config is the correct model. If monitoring topology changes, operators update the config and redeploy. This is the same operational model as K8s node affinity rules, which operators already understand. |
| Non-preferred pods become completely passive during backoff | Operators may assume that backing off means non-preferred pods stop all election activity entirely | Complete passivity during backoff means that if the preferred pod fails during the backoff window, no other pod notices until the backoff window expires. This can extend leadership gap from seconds to the full backoff duration. Non-preferred pods must continue to poll the preferred heartbeat lease during backoff so they can detect stale stamps and switch to normal competition. | Backoff applies only to the leadership lease retry delay (extend the wait before the next acquisition attempt). The preferred heartbeat lease check continues at normal cadence during backoff. |
| Preferred pod stamps the heartbeat on every SNMP poll | Some implementations co-locate the liveness signal with existing work (stamp on every poll to prove the pod is active) | SNMP poll frequency is device-configured (15s to 5min). Using poll cadence as the heartbeat timing creates unpredictable stamp intervals. If all devices are configured at 5-minute intervals, the heartbeat stamp is 5 minutes stale before non-preferred pods can detect recovery. The stamp loop must be independent of poll scheduling. | Dedicated stamp loop with a short, configurable interval (e.g., every `RenewIntervalSeconds`, same as leadership renewal cadence). Independent of poll scheduling. Follows the same pattern as leadership lease renewal. |
| Remove fair election entirely; always try to elect preferred pod first | Some operators may prefer a pure "preferred wins" architecture where the preferred pod is the only initial candidate and non-preferred pods never initiate election unless preferred is absent | Removing fair election removes the existing HA guarantee. If the preferred pod is absent at startup (e.g., scheduled on a different node due to node failure), no pod would lead. The two-lease design preserves fair HA election as the foundation; preference is layered on top as a bias, not a replacement. | Two-lease design: fair election always runs as the correctness foundation, preferred heartbeat adds a topological bias. HA is preserved even when the preferred pod is absent. |

---

## Feature Dependencies

```
existing K8sLeaseElection (leadership lease, fair election)
    |
    +--> PreferredHeartbeatService (new)
    |        |
    |        +--> stamps preferred heartbeat lease (if NODE_NAME == PreferredNode)
    |        +--> starts only after readiness gate passes
    |        +--> releases heartbeat lease on shutdown
    |
    +--> PreferredAwareLeaseElection or augmented K8sLeaseElection (modified)
             |
             +--> reads preferred heartbeat stamp age before retry attempt
             |        |
             |        +--> stamp fresh + not preferred: extend retry delay (back off)
             |        +--> stamp stale or no PreferredNode config: normal retry delay
             |
             +--> polls preferred stamp while holding leadership (if not preferred)
                      |
                      +--> stamp fresh + holder is not self: voluntary yield
                               |
                               +--> DeleteNamespacedLeaseAsync (leadership lease)
                               +--> triggers normal election; preferred pod wins

existing GracefulShutdownService (shutdown orchestrator)
    |
    +--> step 1 (ReleaseLease) extended to also delete preferred heartbeat lease
             |
             +--> only if this pod is the preferred node AND heartbeat lease was stamped

existing PodIdentityOptions
    |
    +--> extended or new PreferredNodeOptions
             |
             +--> PreferredNode string (optional; feature disabled if absent)
             +--> read NODE_NAME env var to determine self-identity as preferred

existing ReadinessHealthCheck (trap listener + scheduler gate)
    |
    +--> PreferredHeartbeatService waits for readiness confirmation before first stamp
             |
             +--> dependency on IHealthCheck or IHostedService startup ordering
```

### Dependency Notes

- **PreferredHeartbeatService requires readiness gate before stamping:** The stability gate is the
  critical correctness property. If the stamp loop starts before readiness, non-preferred pods back
  off while the preferred pod is not actually serving. The easiest implementation is to inject
  `IHealthCheckService` and poll until readiness returns Healthy before entering the stamp loop.

- **Voluntary yield requires leadership lease delete permission:** `K8sLeaseElection.StopAsync` already
  calls `DeleteNamespacedLeaseAsync`. Voluntary yield uses the same API call from a different trigger
  (preferred recovery detected, not shutdown). RBAC already grants this permission to the service
  account.

- **Backoff requires watching preferred heartbeat lease, not just self:** Non-preferred pods must read
  the preferred heartbeat lease from the Kubernetes API. This is a new Kubernetes API call pattern
  (get lease by name, check renewTime). It does not require a watch; a periodic get is sufficient
  given the stamp intervals involved.

- **Shutdown sequence ordering:** Preferred heartbeat release must happen before or concurrently with
  leadership lease release. If leadership is released first, the preferred pod's heartbeat lease
  remains stamped, causing non-preferred pods to back off from acquiring the now-vacant leadership
  lease for the duration of the heartbeat staleness threshold. Release heartbeat first (or both
  concurrently), then leadership.

- **NODE_NAME env var requires deployment manifest change:** The pod spec must include the downward API
  env injection. This is a deployment concern, not purely a C# concern. The feature is inert if
  `PreferredNode` is absent from config, so the deployment manifest change can be coordinated
  separately.

---

## MVP Definition

### Launch With (v1 — this milestone)

Minimum viable preferred-leader election that achieves site-affinity at steady state and preserves HA
during preferred-node failure.

- [ ] `PreferredNode` configuration field (extend `LeaseOptions` or new `PreferredElectionOptions`) —
  feature is inert if field is absent; backward compatible with existing deployments
- [ ] `NODE_NAME` env var injection in pod spec — required for preferred pod self-identification
- [ ] `PreferredHeartbeatService` — stamps preferred heartbeat lease after readiness passes; releases
  on shutdown
- [ ] Backoff in non-preferred pods — poll heartbeat stamp before retry; extend delay if stamp is fresh
- [ ] Voluntary yield in non-preferred leader — poll heartbeat stamp while holding lease; release on
  preferred recovery
- [ ] Graceful shutdown of heartbeat lease — `GracefulShutdownService` extends step 1 to delete
  heartbeat lease before leadership lease
- [ ] Structured log lines at each preferred-election decision point — minimum observability for
  diagnosing topology behavior in production

### Add After Validation (v1.x)

- [ ] `is_preferred_node` metric label — once operators confirm the mechanism works and want to alert
  on site-affinity SLO violations
- [ ] Configurable `PreferredStaleThresholdSeconds` — after operators observe actual stamp timing in
  production and identify whether the default (= `DurationSeconds`) is appropriate
- [ ] Health check `preferredNodeIsLeading` field — once ops team confirms they use health endpoint
  data in monitoring dashboards

### Future Consideration (v2+)

- [ ] K8s Event on voluntary yield — useful for cluster-level audit trails; low priority if log lines
  provide sufficient observability for the team's current tooling
- [ ] Multi-level priority chain (site-1 > site-2 > site-3) — only if deployment topology evolves
  beyond binary preferred/non-preferred
- [ ] Dynamic preferred-node based on traffic topology — only if monitored device assignment changes
  frequently enough to justify the added complexity

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| `PreferredNode` config field | HIGH | LOW | P1 |
| `NODE_NAME` env var in pod spec | HIGH | LOW | P1 |
| Preferred heartbeat stamp loop (with readiness gate) | HIGH | MEDIUM | P1 |
| Non-preferred backoff when stamp is fresh | HIGH | MEDIUM | P1 |
| Voluntary yield when preferred recovers | HIGH | HIGH | P1 |
| Graceful shutdown of heartbeat lease | HIGH | LOW | P1 |
| Structured log on each election decision | MEDIUM | LOW | P1 |
| `is_preferred_node` metric label | MEDIUM | LOW | P2 |
| Configurable `PreferredStaleThresholdSeconds` | LOW | LOW | P2 |
| Health check `preferredNodeIsLeading` field | LOW | LOW | P2 |
| K8s Event on voluntary yield | LOW | MEDIUM | P3 |
| Multi-level priority chain | LOW | HIGH | P3 |

**Priority key:**
- P1: Must have for launch — site-affinity guarantee cannot be validated without these
- P2: Should have, adds operational visibility, not blocking correctness
- P3: Nice to have, future consideration or v2+

---

## Observable Behaviors Operators Should Expect

Operators administering this system should be able to verify site-affinity is working using only
`kubectl` and the existing log/metrics stack. The expected steady-state and failure behaviors are:

**Steady state (preferred pod healthy):**
- `kubectl get lease snmp-collector-leader -n <ns>` — `holderIdentity` matches the preferred pod name
- `kubectl get lease snmp-collector-preferred -n <ns>` — `holderIdentity` matches same preferred pod;
  `renewTime` is recent (< DurationSeconds old)
- Logs from non-preferred pods: "preferred stamp fresh, delaying retry" at INFO level
- MetricRoleGatedExporter on preferred pod: `IsLeader=true`, business metrics exported
- MetricRoleGatedExporter on non-preferred pods: `IsLeader=false`, only operational metrics exported

**Preferred pod fails (planned shutdown or crash):**
- Graceful: heartbeat lease deleted before leadership lease; non-preferred pods detect stale stamp
  within `DurationSeconds`; fair election proceeds; one non-preferred pod becomes leader within
  `DurationSeconds` (same as current HA behavior)
- Crash: heartbeat lease expires after `DurationSeconds` (not deleted); non-preferred pods detect
  stale stamp at TTL; same outcome, slightly longer gap (up to `DurationSeconds` × 2 in worst case)

**Preferred pod recovers:**
- Preferred pod passes readiness, begins stamping heartbeat lease
- Non-preferred leader observes fresh stamp within its poll interval (same as `RenewIntervalSeconds`)
- Non-preferred leader logs "preferred pod recovered, yielding leadership" at INFO level
- Non-preferred leader releases leadership lease (same `DeleteNamespacedLeaseAsync` as graceful shutdown)
- Preferred pod acquires leadership lease within `RetryPeriod`
- Total re-affinity time after preferred pod becomes ready: `RenewIntervalSeconds` (stamp poll) +
  `RetryPeriod` (election retry) — typically < 30 seconds

**`PreferredNode` config absent or empty:**
- Feature is completely inert; behavior is identical to today's fair election
- No heartbeat lease created; no backoff; no voluntary yield
- Backward compatible with deployments that do not need site-affinity

---

## Implementation Notes Specific to This Domain

### Two-Lease Naming Conventions

The leadership lease name is already `snmp-collector-leader` (from `LeaseOptions.Name`). The preferred
heartbeat lease should be `snmp-collector-preferred` (or configurable via `PreferredElectionOptions`).
Both leases exist in the same namespace. The preferred heartbeat lease is created by the preferred pod
using `CreateOrReplaceNamespacedLeaseAsync` (not via `LeaderElector` — it does not need the election
machinery).

### Stamp Freshness vs. Lease TTL

The preferred heartbeat lease has its own TTL (`DurationSeconds` from its own config or shared with
leadership lease). Non-preferred pods compute stamp age as `now - renewTime`. The freshness threshold
should be at least `DurationSeconds` (= one missed renewal before declaring stale). If set too low
(e.g., `RenewIntervalSeconds`), a brief API server hiccup causes false "stale" detection and disrupts
the affinity. Default: `DurationSeconds` of the preferred heartbeat lease (pessimistic, avoids flapping).

### Voluntary Yield Timing

The non-preferred leader's yield detection runs inside the same `ExecuteAsync` loop as leadership renewal.
The simplest implementation: after every successful leadership renewal, check the preferred heartbeat
lease. If fresh and not self: call `StopAsync` on itself (or directly call
`DeleteNamespacedLeaseAsync`). This piggybacks on existing renewal cadence (`RenewIntervalSeconds`)
without adding a separate background loop.

### Backoff Duration

Non-preferred pods currently retry leadership acquisition at `RetryPeriod` (from `LeaderElectionConfig`).
When backing off, extend the delay to a configurable multiple (e.g., `RetryPeriod × 3` or
`DurationSeconds`). This ensures preferred pod has sufficient time to acquire the lease without
competition from non-preferred pods, while still allowing non-preferred pods to detect stale stamp and
switch to normal competition within a bounded time.

### Correctness Invariant

The two-lease design must preserve the existing correctness invariant: `IsLeader` is true on exactly
one pod at a time (from the perspective of the leadership lease). The preferred heartbeat lease does not
affect this invariant; it only affects which pod acquires the leadership lease over time. Amazon Builders'
Library guidance applies: plan for zero or two concurrent leaders during failure windows (not a new risk;
exists today with the fair election).

---

## Sources

- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — existing leadership lease mechanism, graceful
  release pattern, `DeleteNamespacedLeaseAsync` usage (HIGH confidence — direct read)
- `src/SnmpCollector/Lifecycle/GracefulShutdownService.cs` — shutdown step 1 budget, lease release
  orchestration pattern (HIGH confidence — direct read)
- `src/SnmpCollector/Configuration/LeaseOptions.cs` — existing config fields, validation ranges
  (HIGH confidence — direct read)
- `src/SnmpCollector/Configuration/PodIdentityOptions.cs` — pod identity pattern for lease holder
  field (HIGH confidence — direct read)
- `src/SnmpCollector/HealthChecks/ReadinessHealthCheck.cs` — readiness gate conditions (trap listener
  + scheduler), dictionary data pattern (HIGH confidence — direct read)
- Amazon Builders' Library: Leader Election in Distributed Systems
  (https://aws.amazon.com/builders-library/leader-election-in-distributed-systems/) — lease semantics,
  correctness considerations, anti-patterns (MEDIUM confidence — authoritative source, verified via fetch)
- Microsoft Azure Architecture Center: Leader Election pattern
  (https://learn.microsoft.com/en-us/azure/architecture/patterns/leader-election) — graceful handoff,
  lease-based mutex, nondeterministic election (MEDIUM confidence — authoritative source, verified via fetch)
- Kubernetes Leases documentation (https://kubernetes.io/docs/concepts/architecture/leases/) — lease
  API, coordination.k8s.io, renewTime field semantics (MEDIUM confidence — official Kubernetes docs)
- Building Bulletproof Leader Election in Kubernetes Operators
  (https://medium.com/@ishaish103/building-bulletproof-leader-election-in-kubernetes-operators-a-deep-dive-4c82879d9d37)
  — structured logging requirements, stability vs. responsiveness tradeoffs, pod existence verification
  pattern (MEDIUM confidence — single source, consistent with authoritative guidance)
- Topology Aware Leader Election for Dynamic Networks (IEEE, 2021) — closeness centrality as leader
  election criterion, locality-aware selection (LOW confidence — academic, not directly applied)

---

*Feature research for: Preferred Leader Election with Site-Affinity*
*Researched: 2026-03-25*
