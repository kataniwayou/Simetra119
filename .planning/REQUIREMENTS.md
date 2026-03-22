# Requirements: SNMP Monitoring System — v2.4 Tenant Vector Metrics

**Defined:** 2026-03-23
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.4 Requirements

### Tenant Metric Instruments

- [ ] **TMET-01**: TenantMetricService singleton with separate "SnmpCollector.Tenant" meter that exports on all instances (not leader-gated)
- [ ] **TMET-02**: tenant_tier1_stale counter with tenant_id and priority labels — increments by N stale holders per SnapshotJob cycle per tenant
- [ ] **TMET-03**: tenant_tier2_resolved counter with tenant_id and priority labels — increments by N resolved-role metrics not violated per cycle per tenant
- [ ] **TMET-04**: tenant_tier3_evaluate counter with tenant_id and priority labels — increments by N evaluate-role metrics violated per cycle per tenant
- [ ] **TMET-05**: tenant_command_dispatched counter with tenant_id and priority labels — increments by N commands sent per cycle per tenant
- [ ] **TMET-06**: tenant_command_failed counter with tenant_id and priority labels — increments by N commands failed per cycle per tenant
- [ ] **TMET-07**: tenant_command_suppressed counter with tenant_id and priority labels — increments by N commands suppressed per cycle per tenant
- [ ] **TMET-08**: tenant_state gauge with tenant_id and priority labels — enum: NotReady=0, Healthy=1, Resolved=2, Unresolved=3
- [ ] **TMET-09**: tenant_gauge_duration_milliseconds histogram with tenant_id and priority labels — per-tenant evaluation duration in SnapshotJob

### SnapshotJob Integration

- [ ] **TSJI-01**: All tier counters incremented at correct exit points inside EvaluateTenant (tier-1 stale, tier-2 resolved gate, tier-3 evaluate check, tier-4 command dispatch)
- [ ] **TSJI-02**: tenant_state gauge recorded after each evaluation cycle with correct enum value
- [ ] **TSJI-03**: Stopwatch per-tenant inside EvaluateTenant records histogram duration (inside parallel group, not outside)
- [ ] **TSJI-04**: Command outcome counters (dispatched, failed, suppressed) incremented per-tenant inside SnapshotJob evaluation flow

### Operations Dashboard

- [ ] **TDSH-01**: Tenant metrics table panel added after existing commands panels in operations dashboard
- [ ] **TDSH-02**: Table columns: Host, Pod, Tenant, Priority, State, Dispatched, Failed, Suppressed, Stale, Resolved, Evaluate, P99 (ms), Trend, PromQL
- [ ] **TDSH-03**: State column with color-mapped enum values (green=Healthy, red=Unresolved, yellow=Resolved, grey=NotReady)
- [ ] **TDSH-04**: Cascading Host/Pod filters applied to tenant table (existing dashboard filter variables)
- [ ] **TDSH-05**: Trend column with delta arrows (based on business dashboard gauge table pattern)
- [ ] **TDSH-06**: PromQL column with copyable query strings per row

### E2E Validation

- [ ] **TE2E-01**: All 8 tenant metric instruments appear in Prometheus with correct tenant_id and priority labels
- [ ] **TE2E-02**: Tier counter increments verified against known evaluation state transitions (stale, resolved, evaluate, commanded)
- [ ] **TE2E-03**: All-instances export verified — follower pods export tenant metrics while snmp_* metrics remain leader-gated
- [ ] **TE2E-04**: tenant_state gauge values verified against expected evaluation outcomes (NotReady=0, Healthy=1, Resolved=2, Unresolved=3)
- [ ] **TE2E-05**: tenant_gauge_duration_milliseconds histogram P99 verified present and > 0

## Out of Scope

| Feature | Reason |
|---------|--------|
| Per-tenant alerting rules | Alerting is a separate concern; metrics must exist first |
| Dedicated tenant dashboard | Operations dashboard table is sufficient for v2.4 |
| device_name/resolved_name/oid/ip/source/snmp_type labels on tenant metrics | Tenant metrics use tenant_id and priority only — device-level labels belong to snmp_gauge/snmp_info |
| Removing existing command/duration/routed panels | Not redundant — existing panels show pod-level aggregates, new table shows per-tenant breakdown |
| sum by aggregation in table queries | All 3 replicas evaluate independently; table shows per-pod per-tenant values |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| TMET-01 | Phase 72 | Pending |
| TMET-02 | Phase 72 | Pending |
| TMET-03 | Phase 72 | Pending |
| TMET-04 | Phase 72 | Pending |
| TMET-05 | Phase 72 | Pending |
| TMET-06 | Phase 72 | Pending |
| TMET-07 | Phase 72 | Pending |
| TMET-08 | Phase 72 | Pending |
| TMET-09 | Phase 72 | Pending |
| TSJI-01 | Phase 73 | Pending |
| TSJI-02 | Phase 73 | Pending |
| TSJI-03 | Phase 73 | Pending |
| TSJI-04 | Phase 73 | Pending |
| TDSH-01 | Phase 74 | Pending |
| TDSH-02 | Phase 74 | Pending |
| TDSH-03 | Phase 74 | Pending |
| TDSH-04 | Phase 74 | Pending |
| TDSH-05 | Phase 74 | Pending |
| TDSH-06 | Phase 74 | Pending |
| TE2E-01 | Phase 75 | Pending |
| TE2E-02 | Phase 75 | Pending |
| TE2E-03 | Phase 75 | Pending |
| TE2E-04 | Phase 75 | Pending |
| TE2E-05 | Phase 75 | Pending |

**Coverage:**
- v2.4 requirements: 24 total
- Mapped to phases: 24
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-23*
*Last updated: 2026-03-23 — traceability mapped to phases 72-75*
