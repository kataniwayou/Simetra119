# Requirements: SNMP Monitoring System

**Defined:** 2026-03-08
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.3 Requirements

Requirements for Grafana Dashboards milestone. Each maps to roadmap phases.

### Dashboard Provisioning

- [ ] **DASH-01**: Grafana dashboard provisioning via JSON ConfigMaps with automatic loading
- [ ] **DASH-02**: Prometheus datasource provisioned automatically (no manual Grafana UI setup)

### Operations Dashboard

- [ ] **OPS-01**: Pod identity/role lookup table showing service_instance_id, pod name, and leader/follower role
- [ ] **OPS-02**: Time series panels for pipeline counters (snmp_event_published_total, snmp_poll_executed_total, snmp_oid_resolved_total, etc.) with per-pod breakdown
- [ ] **OPS-03**: Time series panels for .NET runtime metrics (GC collections, memory, thread pool) with per-pod breakdown
- [ ] **OPS-04**: Dashboard auto-refresh at configurable interval

### Business Dashboard

- [ ] **BIZ-01**: Gauge metrics table with label columns (service_instance_id, device_name, metric_name, oid, snmp_type, value) — device-agnostic, no hardcoded device names
- [ ] **BIZ-02**: Info metrics table with label columns (service_instance_id, device_name, metric_name, oid, value) — device-agnostic, no hardcoded device names
- [ ] **BIZ-03**: Tables auto-refresh to show live current values dynamically
- [ ] **BIZ-04**: Tables automatically include any device present in metrics (no hardcoded names)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Alerting rules | Separate concern, defer to future milestone |
| Per-device dedicated dashboards | Device-agnostic tables cover this |
| Dashboard editing via Grafana UI | Provisioned as code via ConfigMaps |
| Graph panels for business metrics | Tables requested, not time series |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| DASH-01 | TBD | Pending |
| DASH-02 | TBD | Pending |
| OPS-01 | TBD | Pending |
| OPS-02 | TBD | Pending |
| OPS-03 | TBD | Pending |
| OPS-04 | TBD | Pending |
| BIZ-01 | TBD | Pending |
| BIZ-02 | TBD | Pending |
| BIZ-03 | TBD | Pending |
| BIZ-04 | TBD | Pending |

**Coverage:**
- v1.3 requirements: 10 total
- Mapped to phases: 0
- Unmapped: 10 ⚠️

---
*Requirements defined: 2026-03-08*
*Last updated: 2026-03-08 after initial definition*
