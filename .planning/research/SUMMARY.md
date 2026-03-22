# Project Research Summary

**Project:** Simetra119 SNMP Collector — v2.4 Tenant Vector Metrics
**Domain:** Per-tenant observability — OTel SDK instruments + Grafana dashboard for multi-tier SNMP evaluation system
**Researched:** 2026-03-22
**Confidence:** HIGH

## Executive Summary

The v2.4 milestone adds 8 OTel instruments (6 counters, 1 gauge, 1 histogram) that expose per-tenant internal evaluation state from `SnapshotJob`, plus a new Grafana table panel showing real-time per-tenant status across all pods. The project already runs OTel .NET SDK 1.15, OTLP gRPC export, Prometheus, and Grafana — no new dependencies are required. The core implementation is a new `TenantMetricService` singleton registered on a third meter (`"SnmpCollector.Tenant"`), injected into `SnapshotJob` alongside the existing `PipelineMetricService`. This approach follows the established meter-per-export-category pattern and requires zero changes to `MetricRoleGatedExporter`.

The recommended architecture is cleanly bounded: instruments on `"SnmpCollector.Tenant"` pass through `MetricRoleGatedExporter` unchanged on all instances (only `"SnmpCollector.Leader"` is gated), giving operators per-pod tenant visibility essential for diagnosing evaluation divergence across the 3-replica deployment. The four critical design constraints are: (1) use the `"SnmpCollector.Tenant"` meter name, not `"SnmpCollector.Leader"`; (2) increment counters at the decision site in `EvaluateTenant`, not in `CommandWorkerService` where tenant identity is unavailable; (3) record per-tenant duration with a stopwatch inside `EvaluateTenant`, not wrapped around the group's `Task.WhenAll`; (4) confirm label key casing (`tenant_id` vs `tenantId`) in the `TagList` before authoring dashboard PromQL.

The main execution risk is not architectural but operational: PITFALLS research (originally for v2.1 E2E test infrastructure) documents 11+ timing and state-management hazards that apply directly to E2E tests validating the new tenant metrics. Test scripts must account for `(TimeSeriesSize * IntervalSeconds) + 2 * OTel_export_interval` minimum wait windows, per-pod vs cluster-total counter semantics, and unique tenant IDs per scenario to avoid suppression cache bleed.

---

## Key Findings

### Recommended Stack

All technologies are already in the project. No additions required. OTel .NET SDK 1.15 ships `Gauge<T>` (added in 1.10, PR #5867) — `SnmpMetricFactory` already calls `CreateGauge<double>()` in production, confirming no compatibility risk. The `"SnmpCollector.Tenant"` meter registers alongside existing meters in `ServiceCollectionExtensions.AddSnmpTelemetry` with a single `AddMeter` call.

**Core technologies:**
- `System.Diagnostics.Metrics` (.NET 9 BCL): `Counter<long>`, `Gauge<int>`, `Histogram<double>` instruments — built into runtime, OTel SDK maps to OTLP
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.0: OTLP gRPC export, already in csproj, `Gauge<T>` available since 1.10
- `OpenTelemetry.Extensions.Hosting` 1.15.0: `AddMeter`, `AddView` for optional histogram bucket customization
- Prometheus + Grafana (existing): metric storage and instant-query table dashboard panels; no version changes

### Expected Features

All features are P1 or P2 for this milestone. Nothing is deferred to v2+.

**Must have (P1 — table stakes):**
- `tenant_state` gauge (enum 0-3: NotReady/Healthy/Resolved/Unresolved) — single-column current state per pod; set at every tier exit in `EvaluateTenant`
- `tenant_tier1_stale`, `tenant_tier2_resolved`, `tenant_tier3_evaluate` counters — tier-path visibility, incremented by holder count per cycle (not by 1)
- `tenant_command_dispatched`, `tenant_command_failed`, `tenant_command_suppressed` counters — tenant-scoped command activity at dispatch decision site
- `tenant_gauge_duration_milliseconds` histogram — per-tenant evaluation duration; stopwatch inside `EvaluateTenant` recorded before each return
- All-instances export via `"SnmpCollector.Tenant"` meter — per-pod rows essential for divergence detection across replicas
- Dashboard table: State (text-mapped), tier counter rates, P99 column, per-pod rows via `$pod` variable

**Should have (P2 — differentiators):**
- Trend column: `delta(tenant_command_dispatched[30s])` with Grafana Join by field transformation
- PromQL copyable-query column: `label_replace(label_join(...))` pattern from `simetra-business.json`

**Defer (v1.x — after production validation):**
- Prometheus alerting rule templates — once operators establish threshold preferences in production
- NotReady state filter dashboard variable — if startup noise is reported as distracting

**Future consideration (v2+):**
- Per-holder staleness breakdown — label cardinality cost outweighs benefit at current scale; pod logs provide this today
- Cross-pod tenant state comparison panel — dedicated "pod disagreement" view is beyond current scope

### Architecture Approach

The architecture extends the existing meter-per-export-category pattern with a third meter. `TenantMetricService` mirrors `PipelineMetricService` exactly in structure: `IMeterFactory` injection, all instruments created in the constructor, public named increment/record methods. The strict 4-step build order is: (1) add `TenantMeterName` constant to `TelemetryConstants`, (2) create `TenantMetricService` with 8 instruments, (3) register meter and singleton in `ServiceCollectionExtensions`, (4) inject into `SnapshotJob` and add increment calls at each tier exit point.

**Major components:**
1. `TenantMetricService` (NEW, `src/SnmpCollector/Telemetry/TenantMetricService.cs`) — 8 instruments on `"SnmpCollector.Tenant"` meter; all-instances export
2. `SnapshotJob` (MODIFIED) — inject `TenantMetricService`; add tier-exit increment calls at all 4 return points; add stopwatch inside `EvaluateTenant`
3. `ServiceCollectionExtensions` (MODIFIED) — `AddMeter(TelemetryConstants.TenantMeterName)` in `AddSnmpTelemetry`; `AddSingleton<TenantMetricService>()` in `AddSnmpPipeline`
4. `TelemetryConstants` (MODIFIED) — add `TenantMeterName = "SnmpCollector.Tenant"` constant
5. Grafana `simetra-operations.json` (MODIFIED) — new tenant table panel; instant-query format modeled on `simetra-business.json`

**Files confirmed NOT modified:** `MetricRoleGatedExporter.cs`, `PipelineMetricService.cs`, `CommandWorkerService.cs`, `TenantVectorRegistry.cs`, `SnmpMetricFactory.cs`

### Critical Pitfalls

The PITFALLS file covers the v2.1 E2E test infrastructure and identifies hazards that remain applicable to any scenario scripts validating v2.4 tenant metrics.

1. **OTel cumulative temporality lag** — baseline counter snapshot taken before the export cycle flushes produces a stale baseline; prevention: wait `2 * OTel_export_interval` (30s) after any ConfigMap apply before taking the baseline snapshot; the 30s settle pattern in scenario 28 is the correct model
2. **Time series fill requirement** — `AreAllEvaluateViolated()` requires all slots filled; minimum wait = `(TimeSeriesSize * IntervalSeconds) + OTel_export_interval`; a 30s `poll_until` timeout is only sufficient for `TimeSeriesSize=1` tenants
3. **Suppression cache bleeds between scenarios** — `SuppressionCache` is not cleared between test runs; prevention: use distinct tenant IDs per scenario so suppression keys differ; preferred over spacing scenarios 75s+ apart
4. **Multi-replica counter semantics** — `snmp_command_sent_total` only increments on leader; use `sum()` without pod filter for sent assertions; use `max() > 0` for any-pod semantics
5. **Priority group advance gate** — a stale higher-priority tenant blocks all lower-priority tenant evaluations in the same cycle; prevention: ensure all higher-priority tenants are in Healthy state before running lower-priority scenario tests
6. **asyncio event loop blocking (simulator)** — still relevant if the HTTP simulator endpoint is used for scenario setup; async coroutine handler only, no `time.sleep()` inside handlers

---

## Implications for Roadmap

The build order has clear dependency constraints with no parallel phasing possible between the first three implementation phases. Suggested phase structure:

### Phase 1: TenantMetricService + Meter Registration

**Rationale:** All downstream work depends on the instruments existing. This phase produces no behavioral change until `SnapshotJob` calls the methods — safe to land, test in isolation, and verify via a unit test (construct, verify no exceptions thrown).

**Delivers:** `TenantMetricService` singleton with 8 instruments; `TelemetryConstants.TenantMeterName`; meter registered in OTel SDK; metrics appear in OTLP export on both leader and follower instances immediately after Phase 4 adds call sites

**Addresses:** All-instances export (P1), meter isolation from leader gate, instrument naming conventions

**Avoids:**
- Anti-pattern of adding instruments to `PipelineMetricService` (conflates pipeline health and tenant evaluation concerns)
- Using `"SnmpCollector.Leader"` meter (would be filtered on all follower instances)
- `ObservableGauge<T>` with external state dictionary (unnecessary indirection when state is computed synchronously in `EvaluateTenant`)

**Research flag:** Standard patterns — `PipelineMetricService` is a direct template. Skip research-phase.

---

### Phase 2: SnapshotJob Instrumentation

**Rationale:** Depends on Phase 1. Adds increment calls at each of the 4 tier exit points in `EvaluateTenant` and wraps it with a `Stopwatch` for histogram recording. This is the only phase with observable behavioral change — metrics begin flowing to Prometheus.

**Delivers:** Live per-tenant counter and gauge data in Prometheus; per-tenant P99 histogram queryable; state gauge reflects actual evaluation outcomes per pod

**Addresses:** All 6 counters (P1), state gauge (P1), duration histogram (P1), all tier-exit instrumentation per the call-site map in ARCHITECTURE.md

**Avoids:**
- Recording duration from the `Task.WhenAll` group wrapper (inflates all tenant durations to group wall-clock time; per-tenant `tenant_id` tag becomes meaningless)
- Threading `tenant_id` through `CommandRequest` to `CommandWorkerService` (propagates concern across boundary for metrics only; dispatch decision site already has tenant identity)
- Incrementing tier counters by 1 per cycle instead of by holder count (PROJECT.md v2.4 requires "increment by actual metric counts")

**Research flag:** Standard patterns — all call sites mapped in ARCHITECTURE.md from direct code inspection. Skip research-phase.

---

### Phase 3: Grafana Dashboard Panel

**Rationale:** Depends on Phase 2 (metrics must exist in Prometheus before dashboard queries can be validated). Dashboard pattern is fully documented in STACK.md with exact PromQL and JSON structure derived from `simetra-business.json`. P1 columns (State, tier counters, P99) should land before P2 columns (Trend, PromQL) to allow incremental review.

**Delivers:** Operations dashboard tenant table with State (text-mapped 0-3), dispatch/suppressed/failed counter rates, P99 duration column, per-pod rows filtered by `$pod` variable; state column with `color-background` cell formatting

**Addresses:** Dashboard table (P1), state-to-text value mapping (P1), P99 column (P1), Trend column (P2), PromQL copyable-query column (P2)

**Avoids:**
- Aggregating state across pods with `sum()` or `max()` (hides per-pod evaluation divergence — the primary diagnostic value of this dashboard)
- Using `delta()` instead of `rate()` for counter columns (delta is for gauges; counters require `rate()` or `increase()`)
- Querying counter metrics with `instant: false` (table panel requires `instant: true` format with `format: "table"`)

**Research flag:** Standard patterns — STACK.md provides exact PromQL, JSON transformation structure, and field override patterns from existing dashboards. Verify Prometheus label names with a live `curl` after Phase 2 lands. Skip research-phase for planning.

---

### Phase 4: E2E Validation Scenarios

**Rationale:** Last phase; validates the end-to-end path from SnapshotJob evaluation through OTel export to Prometheus. Must apply all timing discipline from PITFALLS.md. Each scenario should use a distinct tenant ID.

**Delivers:** Scenario scripts confirming: (a) each TierResult value appears in `snmp_tenant_state`, (b) each counter increments on the correct tier path, (c) P99 histogram is queryable, (d) all-instances export confirmed on follower pods, (e) existing scenarios 01-28 continue to pass

**Addresses:** MVP validation, surfacing any label-cardinality or export issues before v2.4 release

**Avoids:**
- Pitfall 3: `poll_until` timeouts must be `(TimeSeriesSize * IntervalSeconds) + 30s` minimum
- Pitfall 4: distinct tenant IDs per scenario (unique suppression keys)
- Pitfall 5: `sum(snmp_tenant_command_dispatched_total)` without pod filter for command-sent assertions
- Pitfall 6: mandatory 30s settle wait after ConfigMap apply before baseline snapshot
- Pitfall 7: ensure higher-priority tenants are Healthy before running lower-priority scenario tests
- Pitfall 10: mandatory 15s sleep after scenario switch before entering `poll_until` loop

**Research flag:** Review existing `28-tenantvector-routing.sh` timing patterns and the PITFALLS.md timing formulas before scripting. No external research needed.

---

### Phase Ordering Rationale

- Phases 1 → 2 → 3 → 4 are strictly sequential: instruments must exist before `SnapshotJob` uses them; metrics must be in Prometheus before dashboard is validated; dashboard and metrics must exist before E2E scenarios are meaningful.
- No parallel phasing is possible without validation gaps — each phase's verification depends on the prior phase's observable output.
- P1 features land in Phases 1-3. P2 features (Trend, PromQL columns) can be appended to Phase 3 or shipped as a follow-up PR without blocking v2.4 launch.
- Phase 4 scenario depth scales with team capacity: a single scenario per TierResult value satisfies the MVP; the full TS-SC-* matrix from FEATURES.md can be spread across multiple PRs.

### Research Flags

Phases with standard patterns (skip research-phase):
- **Phase 1:** Direct analog to `PipelineMetricService` — fully documented pattern
- **Phase 2:** All call sites explicitly mapped in ARCHITECTURE.md — no unknowns
- **Phase 3:** Dashboard JSON structure documented in STACK.md from codebase inspection — verify label names post-Phase-2

Phases that may benefit from a brief reference check:
- **Phase 4:** Review `28-tenantvector-routing.sh` for timing constants and ConfigMap save/restore pattern before scripting; PITFALLS.md covers all known hazards

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All findings from direct codebase inspection; OTel 1.15 confirmed in csproj; `CreateGauge<double>()` confirmed working in `SnmpMetricFactory` production code |
| Features | HIGH | Full source reads of `SnapshotJob.cs`, `PipelineMetricService.cs`, both dashboard JSONs, and PROJECT.md v2.4 milestone requirements |
| Architecture | HIGH | All integration points mapped from direct code inspection of every modified file; build order validated by dependency tracing; no inference |
| Pitfalls | HIGH | Derived from source reads of `SnapshotJob.cs`, `MetricSlotHolder.cs`, `SuppressionCache.cs`, `CommandWorkerService.cs`, existing E2E scenario scripts; all pitfalls tied to specific file behavior |

**Overall confidence:** HIGH

### Gaps to Address

- **Prometheus label name casing:** FEATURES.md uses `tenantId` (camelCase) while ARCHITECTURE.md instrument table uses `tenant_id` (snake_case). The `TagList` key string in the `TenantMetricService` constructor determines the final Prometheus label name. Confirm the actual string before authoring dashboard PromQL — a mismatch causes dashboard queries to return no data silently.

- **Prometheus metric name confirmation:** OTel-to-Prometheus name translation rules (`.` → `_`, `_total` suffix for counters) are deterministic, but a quick `curl` to the metrics endpoint after Phase 2 lands is fast insurance before authoring Phase 3 dashboard JSON.

- **P2 dashboard complexity (Trend + PromQL columns):** The Trend column requires a `Join by field` transformation across two instant queries. FEATURES.md identifies this as the highest-complexity dashboard element. If schedule pressure exists, these two columns can ship as a follow-up without blocking the P1 MVP.

- **Staleness scenario mechanics for E2E (Phase 4):** The v2.1 PITFALLS.md notes that staleness testing requires the simulator to stop returning valid SNMP GET responses. The exact mechanism for Phase 4 staleness scenarios (delayed response via `asyncio.sleep()` in OID getter) should be confirmed against the current simulator implementation before scripting.

---

## Sources

### Primary (HIGH confidence — direct codebase inspection)

- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — instrument patterns, `TagList` usage, meter ownership, increment method signatures
- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — gate logic; `_gatedMeterName` field; follower filter behavior
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — meter name constants
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — `CreateGauge<double>()` in production use; `IMeterFactory` pattern
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — meter registration, singleton wiring
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — 4-tier evaluation flow, all return sites, existing `_pipelineMetrics` call sites, constructor parameter list
- `src/SnmpCollector/Services/CommandWorkerService.cs` — `CommandRequest` structure, no `tenant_id` in channel items
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `TenantCount` property, `Groups` iteration
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — data model confirming no tenant identity on individual holders
- `deploy/grafana/dashboards/simetra-business.json` — instant table, `label_join`/`label_replace` PromQL column, `delta()` Trend column, `histogram_quantile` P99 pattern
- `deploy/grafana/dashboards/simetra-operations.json` — value mappings, `color-background` cell option
- `.planning/PROJECT.md` v2.4 milestone — exact instrument names, label names, dashboard column requirements, all-instances export requirement

### Secondary (MEDIUM confidence)

- GitHub issue open-telemetry/opentelemetry-dotnet #4805 — synchronous `Gauge<T>` added in 1.10, PR #5867 merged
- OTel official docs (instruments reference) — `ObservableGauge` callback API, for contrast with `Gauge<T>.Record()`
- OTel SDK customizing README — `AddView` + `ExplicitBucketHistogramConfiguration` API for optional bucket override

---
*Research completed: 2026-03-22*
*Ready for roadmap: yes*
