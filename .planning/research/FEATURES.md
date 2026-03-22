# Feature Research

**Domain:** Tenant-level observability metrics — real-time per-tenant state monitoring in a
multi-tier SNMP evaluation system
**Researched:** 2026-03-22
**Confidence:** HIGH — derived from full source reads of SnapshotJob.cs, PipelineMetricService.cs,
simetra-business.json, simetra-operations.json, PROJECT.md (v2.4 milestone requirements), and the
existing label taxonomy established across v1.0 through v2.3.

---

## Scope

This research defines the feature landscape for v2.4: adding 6 counters, 1 gauge, and 1 histogram
that expose per-tenant internal evaluation state, plus a new Grafana dashboard table showing
real-time per-tenant status across all pods.

The system under test is a 3-replica K8s deployment where SnapshotJob evaluates every tenant every
15 seconds through a 4-tier logic tree. The existing `snmp.command.*` counters are keyed by
`device_name`, which is the SNMP device name — not the tenant. Operators cannot currently answer
"which tenants are stale right now?" or "which tenant triggered commands last cycle?" without
grepping pod logs. The v2.4 instruments close that gap.

**What already exists (features the new milestone depends on):**

- 14 pipeline counters on `snmp.*` instruments with `device_name` label — all leader-gated
- `snmp.snapshot.cycle_duration_ms` histogram (no labels) — per-job total duration
- `MetricRoleGatedExporter` — gates `snmp_gauge`/`snmp_info` to leader only; pipeline counters are
  NOT gated (all instances export)
- Operations dashboard: time-series panels for all pipeline counters; command panels at row 5
- Business dashboard: instant-query table with Host/Pod/Device cascading filter, Trend (delta arrow)
  column, PromQL copyable-query column
- `TelemetryConstants.MeterName` — the single meter name all existing instruments share
- `PipelineMetricService` — singleton that owns all instruments; inject as single point

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features that an operator administering a multi-tenant SNMP monitoring system will expect. Missing
any of these leaves the tenant dashboard half-functional or misleading.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| tenant_state gauge (enum) | Without a state indicator the table has no single answer to "is this tenant OK?" — operators need one column that tells them NotReady/Healthy/Resolved/Unresolved at a glance | LOW | One gauge per tenantId+priority+pod combination. Values: NotReady=0, Healthy=1, Resolved=2, Unresolved=3. Set inside EvaluateTenant after TierResult is known. |
| tenantId + priority labels | Tenant metrics without the tenant identifier and its priority are unqueryable — every filter and every dashboard join requires these two labels | LOW | Labels: `tenantId`, `priority`. No device_name (tenant metrics are not device metrics). Consistent with PROJECT.md v2.4 requirement. |
| All-instances export (not leader-gated) | Each pod runs SnapshotJob independently. Each pod evaluates every tenant. If only the leader exports, 2 of 3 pod perspectives are invisible. Operators need per-pod tenant state to diagnose split-brain or follower evaluation divergence | MEDIUM | Requires separate Meter (or same meter but NOT passed through MetricRoleGatedExporter). The snmp_gauge/snmp_info instruments are leader-gated because duplicate export causes double-counting. Tenant counters are per-pod-per-cycle totals, so all-instances export is correct and does not double-count. |
| tenant_tier1_stale counter | Operators need to know which tenants are staleness-blocked this cycle and how many holders are stale. Without this they must infer staleness from the absence of command activity. | LOW | Increment by count of stale holders (not 1 per tenant). Incremented only when HasStaleness returns true. Reflects tier-1 path. |
| tenant_tier2_resolved counter | Operators need visibility into the resolved-gate outcome — how many resolved-role metrics are confirmed bad. Resolved state means "device is known-bad, no commands". Without this metric, Resolved state looks identical to Healthy from the outside. | LOW | Increment by count of resolved holders checked. Incremented when AreAllResolvedViolated returns true (tier-2 stop path). |
| tenant_tier3_evaluate counter | Operators need to see how many evaluate-role metrics are currently violated per tenant per cycle. This is the decision signal for command dispatch. | LOW | Increment by count of violated evaluate holders. Incremented on tier-3 path (both Healthy exit and Unresolved/proceed-to-tier-4 exit). |
| tenant_command_dispatched counter | Already exists as snmp.command.dispatched with device_name label. The new tenant-labeled version enables per-tenant command dispatch tracking without joining on device names. This is the tenant-centric view of what the command counters already show. | LOW | Increment by 1 per command successfully enqueued in tier-4. Labels: tenantId, priority. Parallels existing IncrementCommandDispatched. |
| tenant_command_suppressed counter | Same rationale as dispatched — operators need to see which tenants are suppression-heavy. High suppression on a specific tenant indicates the evaluation fires faster than the device can be corrected, which is a configuration signal. | LOW | Increment by 1 per suppressed command. Parallels existing IncrementCommandSuppressed. |
| tenant_command_failed counter | Failed commands (channel full) are rare but critical. Without a tenant-labeled failed counter, operators cannot tell which tenant is experiencing the channel-full condition. | LOW | Increment by 1 per failed enqueue (channel full). Parallels existing IncrementCommandFailed. |
| tenant_gauge_duration_milliseconds histogram | SnapshotJob already records cycle duration for the whole job. Per-tenant duration is necessary to diagnose which specific tenant is slow (blocking on holders or causing evaluation divergence). | LOW | Stopwatch wrapping EvaluateTenant. Per-tenant duration is a subset of the existing cycle_duration_ms which covers the full group traversal. |
| Dashboard table with tenantId + priority + state columns | Without a table panel that shows all tenants, the metrics are only queryable via raw PromQL. Operators need the "current state at a glance" view that the business dashboard provides for SNMP metrics. | MEDIUM | One row per tenantId+priority+pod combination. State column from tenant_state gauge value (0-3 mapped to labels). Depends on instant-query table pattern from simetra-business.json. |
| Per-pod rows in dashboard (not aggregated) | Tenant evaluation runs independently on each pod. Aggregating across pods (sum/max) would hide per-pod disagreements. Operators need to see that pod-A thinks a tenant is Healthy while pod-B thinks it is Unresolved. | LOW | Filter by k8s_pod_name via $pod template variable, same as operations dashboard. Do NOT use sum() for state columns. |

### Differentiators (Competitive Advantage)

Features that go beyond basic visibility and enable faster diagnosis or proactive monitoring.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| State enum value mapped to text in dashboard | tenant_state emits 0/1/2/3 as raw numbers. Mapping these to "NotReady", "Healthy", "Resolved", "Unresolved" in the dashboard cell lets operators read the table without memorizing the encoding. Grafana value mappings accomplish this as a field override on the state column. | LOW | Grafana "Value mappings" field override: 0->NotReady, 1->Healthy, 2->Resolved, 3->Unresolved. Same mechanism as existing snmp_type and source label display in business dashboard. |
| P99 duration column (histogram_quantile from tenant_gauge_duration_ms) | The Dispatched/Failed/Suppressed columns show what happened. The P99 column shows how long each tenant took to evaluate. A tenant with P99 > 500ms is a candidate for threshold misconfiguration or SNMP device latency investigation. | LOW | PromQL: `histogram_quantile(0.99, sum by (le, tenantId, k8s_pod_name) (rate(tenant_gauge_duration_milliseconds_bucket[5m])))`. Column header: "P99 (ms)". Same as the existing snapshot cycle histogram but per-tenant. |
| Trend column (delta direction arrow) | Same pattern as the existing business dashboard gauge table Trend column. Shows whether the tenant's dispatched command count is rising, flat, or falling over the last 30s. A rising dispatch trend with flat-or-falling suppressed means commands are actually getting through — healthy remediation. A rising suppression trend means the suppression window is too short or the device is not responding. | MEDIUM | Requires a second query (delta over 30s) joined to the primary instant query, using Grafana "Join by field" transformation keyed on tenantId+pod. This is the same technique used in simetra-business.json for the Trend column on snmp_gauge. Complexity is the join transformation, not the PromQL. |
| PromQL column (copyable query per row) | Operators can click the PromQL column and copy a query to PromQL explorer for deep-dive on a specific tenant+pod combination. Same pattern as the business dashboard. | MEDIUM | Uses `label_replace(label_join(...), "promql", ...)` to construct a per-row PromQL string in the metric itself. Exact pattern from lines 458 and 864 of simetra-business.json. Requires knowing the right join labels (tenantId, k8s_pod_name, service_instance_id). |
| NotReady state filtering | Tenants in the readiness window (grace period at startup) are not evaluating yet. Showing them as Unresolved would be misleading. The NotReady=0 enum value lets operators filter them out of alerting and distinguish startup noise from real evaluation problems. | LOW | Feature is free once the state gauge emits NotReady=0. Dashboard can add a filter row, or the operator can use `tenant_state != 0` in alert rules. No extra work beyond correct state assignment. |
| Alert rule templates in documentation | Alert rule templates (not in-dashboard alerts, but external Prometheus rules) for "any tenant Unresolved for > 3 cycles" or "tenant_command_failed_total rising" give operators a starting point for productionizing the observability. | LOW | Not code — documentation-only. Produces no dashboard JSON or C# changes. Worth noting in implementation summary as a "next step" for operators. |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Leader-gate the tenant metrics | Someone might suggest tenant metrics should follow the same leader-gate pattern as snmp_gauge/snmp_info to "avoid duplicate data" | Tenant counters are per-pod per-cycle. Gating to leader hides 2/3 of the evaluation picture. Unlike snmp_gauge (which has the same value on all pods), tenant_tier1_stale on pod-A may be 3 while on pod-B it is 0 due to a stale holder that hasn't propagated. All-instances export is the correct model for job-generated per-pod metrics. | All-instances export via a meter that is NOT passed through MetricRoleGatedExporter. Create a second Meter for tenant metrics, or register tenant instruments on the same meter but ensure the exporter does not filter them. |
| Aggregate tenant state across pods (single-row-per-tenant view) | Operators may ask for "one row per tenant" instead of "one row per tenant+pod" to reduce table verbosity | Aggregating state across pods (e.g., max() across pods) hides per-pod divergence. If pod-A evaluates a tenant as Unresolved because its holder is stale and pod-B evaluates it as Healthy, the max() would show Healthy and mask the staleness issue on pod-A. | Keep per-pod rows. Use the $pod template variable to filter down to a specific pod when the operator wants a single-pod view. Document this in the dashboard description. |
| Histogram buckets for tier counts (instead of counters) | Some observability systems use histograms for "how many X per cycle" distribution tracking | For this system, tier counts per cycle are small integers (0-10 typically). Histogram bucket cardinality would be high relative to value. A counter that increments by N per cycle already provides rate() and increase() query capability, which is all operators need. | Use Counter<long> incrementing by the count. Operators can use `rate()` for per-second rate and `increase()` for per-window total. |
| Per-holder labels on tenant metrics (adding metric_name/oid to tenant counters) | More granular labeling would let operators see exactly which OID caused tier-1 stale | Label cardinality explosion. A tenant with 10 holders would produce 10 time series per counter per pod. At 3 pods, 6 counters, 10 holders, this is 180 series per tenant just for the tier counters. That cardinality is unjustified for a 15s cycle metric. | The counter value already tells operators "5 stale holders this cycle". If they need to know which OIDs are stale, the tier=1 pod log line names the tenant — operators can then check the tenant's MetricSlotHolder configuration. |
| Adding device_name label to tenant metrics | tenant_command_dispatched already sounds similar to snmp.command.dispatched which has device_name | Tenant metrics are tenant-scoped, not device-scoped. A tenant can span multiple devices (multiple holders from different devices). Adding device_name would require either duplicating the counter per device (incorrect) or picking one device name arbitrarily (misleading). | Keep tenant metrics keyed only by tenantId+priority. The join between tenant activity and device activity is done in the dashboard or in PromQL when needed (cross-join on pod name). |
| Real-time state changes via Grafana alerts at sub-15s granularity | SnapshotJob runs every 15s. Operators may ask for state-change alerts with < 15s latency | OTel export interval is 15s. Prometheus scrape interval is typically 15-30s. The metric will be stale for up to 30s after a state change. Sub-15s alerting would require streaming/events which is out of scope (project uses metrics + logs, no traces, no event streams). | For sub-15s latency, direct pod log tailing via `kubectl logs -f` is the correct tool. The dashboard provides current state within one OTel export cycle (15-30s lag), which is appropriate for operational monitoring rather than real-time incident alerting. |

---

## Feature Dependencies

```
existing PipelineMetricService singleton
    |
    +--> new TenantMetricService (or extended PipelineMetricService)
             |
             +--> tenant_state gauge
             +--> tenant_tier1_stale counter
             +--> tenant_tier2_resolved counter
             +--> tenant_tier3_evaluate counter
             +--> tenant_command_dispatched counter
             +--> tenant_command_suppressed counter
             +--> tenant_command_failed counter
             +--> tenant_gauge_duration_milliseconds histogram

SnapshotJob.EvaluateTenant (already calls PipelineMetricService)
    |
    +--> must call TenantMetricService at each tier exit point
    |        |
    |        +--> Pre-tier: SetState(NotReady) on readiness-fail exit
    |        +--> Tier 1: IncrementTier1Stale(count) on stale exit
    |        +--> Tier 2: IncrementTier2Resolved(count) on resolved-gate exit + SetState(Resolved)
    |        +--> Tier 3: IncrementTier3Evaluate(count) on both Healthy and Unresolved exits + SetState(Healthy)
    |        +--> Tier 4: IncrementCommandDispatched/Suppressed/Failed per command + SetState(Unresolved)
    |
    +--> Stopwatch wrapping EvaluateTenant for tenant_gauge_duration_milliseconds
             |
             +--> Record after TierResult returned, before returning to caller

All-instances export (NOT leader-gated)
    |
    +--> Requires tenant meter to NOT be passed through MetricRoleGatedExporter
    +--> Depends on MetricRoleGatedExporter architecture (already built in Phase 07)

Operations dashboard (existing simetra-operations.json)
    |
    +--> New tenant metrics table is an ADDITION, not a replacement
    +--> Follows instant-query table pattern from simetra-business.json
    +--> Trend column requires delta(tenant_command_dispatched[30s]) second query
    +--> PromQL column requires label_replace(label_join(...)) same pattern as simetra-business.json

Dashboard Trend column
    +--> requires: tenant_command_dispatched counter (something to trend)
    +--> requires: Grafana "Join by field" transformation (established in simetra-business.json)

Dashboard PromQL column
    +--> requires: tenantId and k8s_pod_name labels (the join key labels)
    +--> requires: label_replace/label_join pattern (established in simetra-business.json)

State enum value mapping in dashboard
    +--> requires: tenant_state gauge (the source instrument)
    +--> requires: Grafana "Value mappings" field override (established in project)
```

### Dependency Notes

- **TenantMetricService requires EvaluateTenant call sites:** The tier counters and state gauge must be set at every tier exit point inside EvaluateTenant. The method currently returns TierResult but does not record per-tenant metric breakdowns. Call sites must be added at each early-return location and at the tier-4 loop.

- **All-instances export requires meter isolation:** MetricRoleGatedExporter was designed to gate snmp_gauge/snmp_info to the leader. If tenant instruments share the same meter, they may inadvertently be gated. The safe approach is to register tenant instruments on a separate named meter (`TenantMetrics` or `SnmpCollector.Tenants`) that is not passed through the role-gated exporter. Confirm the exporter's meter filter logic before implementation.

- **Dashboard PromQL column requires label availability:** The `label_replace(label_join(...))` pattern in simetra-business.json joins on `resolved_name`, `device_name`, `k8s_pod_name`, and `service_instance_id`. For tenant metrics, the join labels are `tenantId`, `priority`, `k8s_pod_name`, and `service_instance_id`. The tenant metric labels must be confirmed in Prometheus before authoring the dashboard JSON, or the PromQL column will silently produce empty strings.

- **Trend column requires delta query alignment:** The Trend column uses a second Prometheus query (`delta(...)`) that must match the same label set as the primary instant query. Both queries must include `tenantId` and `k8s_pod_name` for the Grafana "Join by field" transformation to correlate rows correctly.

---

## MVP Definition

### Launch With (v1 — this milestone)

The minimum viable tenant observability capability that answers "what state are my tenants in right now, and what caused it?"

- [x] tenant_state gauge — answers "what is each tenant's current state per pod?" without any counter math
- [x] tenant_tier1_stale counter — answers "which tenants are staleness-blocked and how many holders?"
- [x] tenant_tier2_resolved counter — answers "which tenants hit the resolved gate?"
- [x] tenant_tier3_evaluate counter — answers "which tenants have violated evaluate metrics?"
- [x] tenant_command_dispatched counter — answers "which tenants triggered commands this cycle?"
- [x] tenant_command_failed counter — answers "which tenants are experiencing command failures?"
- [x] tenant_command_suppressed counter — answers "which tenants are in suppression?"
- [x] tenant_gauge_duration_milliseconds histogram — answers "which tenant is slow to evaluate?"
- [x] Dashboard table with State, Dispatched, Failed, Suppressed, Stale, Resolved, Evaluate, P99 (ms) columns — the primary operator view
- [x] State-to-text value mapping in dashboard — NotReady/Healthy/Resolved/Unresolved readable labels
- [x] Trend column for dispatched commands — rising/flat/falling dispatch direction
- [x] PromQL copyable-query column — deep-dive link per tenant+pod row

### Add After Validation (v1.x)

- [ ] Prometheus alerting rule templates — once operators have used the dashboard in production and identified their threshold preferences
- [ ] NotReady state filter UI (dashboard variable to exclude NotReady rows) — if operators report startup noise is distracting; trivial to add as a variable after validation

### Future Consideration (v2+)

- [ ] Per-holder staleness breakdown (which specific OID is stale) — high cardinality cost outweighs benefit for current scale; pod logs provide this detail today
- [ ] Cross-pod tenant state comparison panel — operators can manually filter by pod today; a dedicated "pod disagreement" panel requires further dashboard complexity beyond scope

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| tenant_state gauge | HIGH | LOW | P1 |
| tenant_tier1_stale counter | HIGH | LOW | P1 |
| tenant_tier3_evaluate counter | HIGH | LOW | P1 |
| tenant_command_dispatched/suppressed/failed counters | HIGH | LOW | P1 |
| tenant_gauge_duration_milliseconds histogram | MEDIUM | LOW | P1 |
| tenant_tier2_resolved counter | MEDIUM | LOW | P1 |
| All-instances export (meter isolation) | HIGH | MEDIUM | P1 |
| Dashboard table (State + counter columns) | HIGH | MEDIUM | P1 |
| State-to-text value mapping | MEDIUM | LOW | P1 |
| P99 column in dashboard | MEDIUM | LOW | P1 |
| Trend column in dashboard | MEDIUM | MEDIUM | P2 |
| PromQL column in dashboard | LOW | MEDIUM | P2 |

**Priority key:**
- P1: Must have for launch — dashboard is incomplete without these
- P2: Should have, adds real operator value, not blocking
- P3: Nice to have, future consideration

---

## Implementation Notes Specific to This Domain

### Counter Semantics: "by N" vs "by 1"

The existing pipeline counters (snmp.event.published, snmp.command.dispatched) all increment by 1
per event. The new tenant counters increment by N (the count of stale holders, violated metrics,
etc.) per cycle. This is intentional and documented in PROJECT.md v2.4 requirements:

> "Per-cycle counters increment by actual metric counts (e.g., 5 stale holders = +5 to
> tenant_tier1_stale)"

This means `rate(tenant_tier1_stale_total[5m])` yields "average stale holders per second across
all cycles", not "average stale events per second". Dashboard panels displaying these counters
should use `increase(...)` over a time window to show "total stale holder instances in last N
minutes" which is more intuitive than a per-second rate for a per-cycle accumulator.

### State Gauge vs Counter for State

`tenant_state` is a Gauge, not a Counter. It is overwritten every cycle with the current state
value. This means:

- `tenant_state` is safe for `instant: true` table queries — it always reflects the most recent
  evaluation cycle.
- It should NOT be used with `rate()` — state is not monotonically increasing.
- It CAN be used with `changes()` to count how many times a tenant changed state in a window.

The gauge approach is correct because state is current-value semantics (what is the state NOW),
not accumulation semantics.

### Export Architecture Decision

The comment in `PipelineMetricService.cs` says it owns "all 14 pipeline counter instruments and 1
histogram on the SnmpCollector meter." The tenant instruments are a different concern (tenant
evaluation, not pipeline throughput). Using a separate `TenantMetricService` with a separate Meter
name (`SnmpCollector.Tenants`) is cleaner than expanding PipelineMetricService further. This also
makes the meter-filter decision explicit: the new meter is simply not registered with
MetricRoleGatedExporter.

---

## Sources

- `src/SnmpCollector/Jobs/SnapshotJob.cs` — full 4-tier logic, tier exit points, TierResult enum,
  EvaluateTenant return conditions (HIGH confidence — direct read)
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — existing instrument naming conventions,
  TagList usage, Meter ownership pattern (HIGH confidence — direct read)
- `deploy/grafana/dashboards/simetra-business.json` — Trend column delta() pattern, PromQL
  label_replace/label_join column, instant table query with format:table, field overrides for
  hiding/renaming columns, value mappings, Join by field transformation (HIGH confidence — direct read)
- `deploy/grafana/dashboards/simetra-operations.json` — command panel positions, template variable
  patterns, row panel structure (HIGH confidence — direct read)
- `.planning/PROJECT.md` v2.4 milestone requirements — exact instrument names, label names (tenantId,
  priority), dashboard column list, all-instances export requirement (HIGH confidence — direct read)
- `.planning/phases/07-leader-election-and-role-gated-export/` — MetricRoleGatedExporter architecture
  and meter filter behavior (MEDIUM confidence — summary files, not source code read directly)

---

*Feature research for: Tenant Vector Metrics (v2.4)*
*Researched: 2026-03-22*
