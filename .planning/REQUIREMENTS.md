# Requirements: SNMP Monitoring System — v2.5 Tenant Metrics Approach Modification

**Defined:** 2026-03-23
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.5 Requirements

### Evaluation Flow Refactor

- [ ] **EFR-01**: EvaluateTenant gathers all tier results (stale count, resolved violation count, evaluate violation count) before making any state decision — no early returns except NotReady
- [ ] **EFR-02**: After gathering all results, EvaluateTenant records all metrics together at exit point
- [ ] **EFR-03**: tenant_state derived from gathered percentages and tier results (not tier short-circuits)

### Percentage Gauges

- [ ] **PGA-01**: tenant_stale_percent gauge — 100 * stale_count / total_tenant_metrics (from config)
- [ ] **PGA-02**: tenant_resolved_percent gauge — 100 * violated_resolved / total_resolved_metrics (from config)
- [ ] **PGA-03**: tenant_evaluate_percent gauge — 100 * violated_evaluate / total_evaluate_metrics (from config)
- [ ] **PGA-04**: tenant_dispatched_percent gauge — 100 * dispatched / total_tenant_commands (from config)
- [ ] **PGA-05**: tenant_failed_percent gauge — 100 * failed / total_tenant_commands (from config)
- [ ] **PGA-06**: tenant_suppressed_percent gauge — 100 * suppressed / total_tenant_commands (from config)

### Resolved Metric Direction

- [ ] **RMD-01**: Resolved percentage measures violated holders (not non-violated) — consistent direction with evaluate (higher % = worse)

### Counter Removal & Cleanup

- [ ] **CLN-01**: Remove all 6 counter instruments from TenantMetricService (tier1_stale, tier2_resolved, tier3_evaluate, command_dispatched, command_failed, command_suppressed)
- [ ] **CLN-02**: Remove counting helper methods and replace with percentage calculation logic
- [ ] **CLN-03**: Clean up old counter references, comments, and dead code across SnapshotJob and CommandWorkerService

### Unchanged Instruments

- [ ] **UCH-01**: tenant_state gauge unchanged (enum 0-3: NotReady, Healthy, Resolved, Unresolved)
- [ ] **UCH-02**: tenant_evaluation_duration_milliseconds histogram unchanged (Stopwatch entrance to exit)

### Dashboard Update

- [ ] **DSH-01**: Operations dashboard Tenant Status table columns show percentage values instead of raw counts
- [ ] **DSH-02**: Dashboard PromQL queries updated from increase() counter queries to gauge queries

### E2E Scenario Update

- [ ] **E2E-01**: Scenarios 107-112 updated to assert on percentage gauge values instead of counter deltas
- [ ] **E2E-02**: Smoke test verifies 6 percentage gauge instruments present (replacing 6 counters)

### Unit Test Update

- [ ] **UTT-01**: SnapshotJobTests updated for percentage gauge assertions
- [ ] **UTT-02**: TenantMetricService unit tests updated for gauge API

## Out of Scope

| Feature | Reason |
|---------|--------|
| Per-holder breakdown labels | Label cardinality cost; percentages give sufficient visibility |
| Configurable threshold alerts on percentages | Alerting is a separate concern; gauges enable it later |
| Historical percentage trends (recording rules) | Prometheus recording rules are ops config, not application code |
| Adding new evaluation tiers | Refactoring existing 4-tier flow only |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| EFR-01 | TBD | Pending |
| EFR-02 | TBD | Pending |
| EFR-03 | TBD | Pending |
| PGA-01 | TBD | Pending |
| PGA-02 | TBD | Pending |
| PGA-03 | TBD | Pending |
| PGA-04 | TBD | Pending |
| PGA-05 | TBD | Pending |
| PGA-06 | TBD | Pending |
| RMD-01 | TBD | Pending |
| CLN-01 | TBD | Pending |
| CLN-02 | TBD | Pending |
| CLN-03 | TBD | Pending |
| UCH-01 | TBD | Pending |
| UCH-02 | TBD | Pending |
| DSH-01 | TBD | Pending |
| DSH-02 | TBD | Pending |
| E2E-01 | TBD | Pending |
| E2E-02 | TBD | Pending |
| UTT-01 | TBD | Pending |
| UTT-02 | TBD | Pending |

**Coverage:**
- v2.5 requirements: 21 total
- Mapped to phases: 0 (pending roadmap)
- Unmapped: 21

---
*Requirements defined: 2026-03-23*
*Last updated: 2026-03-23 — initial definition*
