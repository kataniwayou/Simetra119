# Roadmap: v1.3 Grafana Dashboards

## Overview

This milestone delivers automated Grafana dashboard provisioning and two purpose-built dashboards for the SNMP monitoring system. Phase 17 establishes the provisioning infrastructure so dashboards and datasources load automatically via K8s ConfigMaps. Phase 18 builds the operations dashboard for pipeline health and pod observability. Phase 19 builds the business dashboard with device-agnostic metric tables.

## Milestones

- [x] **v1.0 Foundation** - Phases 1-10 (shipped 2026-03-07)
- [x] **v1.1 Device Simulation** - Phases 11-14 (shipped 2026-03-08)
- [x] **v1.2 Operational Enhancements** - Phases 15-16 (shipped 2026-03-08)
- [ ] **v1.3 Grafana Dashboards** - Phases 17-19 (in progress)

## Phases

- [ ] **Phase 17: Dashboard Provisioning** - Automated datasource and dashboard loading via K8s ConfigMaps
- [ ] **Phase 18: Operations Dashboard** - Pod identity, pipeline counters, and .NET runtime observability
- [ ] **Phase 19: Business Dashboard** - Device-agnostic gauge and info metric tables

## Phase Details

### Phase 17: Dashboard Provisioning
**Goal**: Grafana automatically loads datasource and dashboards from K8s ConfigMaps without manual UI setup
**Depends on**: Nothing (first phase of v1.3)
**Requirements**: DASH-01, DASH-02
**Success Criteria** (what must be TRUE):
  1. Grafana pod starts and Simetra Prometheus datasource appears without any manual configuration
  2. Dashboard JSON files placed in the provisioned directory appear in Grafana automatically
  3. Grafana deployment YAML includes volume mounts for provisioning ConfigMaps
  4. Dashboard provisioning config tells Grafana where to find JSON files and disables UI editing
**Plans**: 1 plan

Plans:
- [ ] 17-01-PLAN.md — Provisioning infrastructure (delete stale files, create ConfigMaps, update Grafana deployment with volume mounts)

### Phase 18: Operations Dashboard
**Goal**: Operators can monitor pipeline health, pod roles, and .NET runtime metrics across all replicas from a single Grafana dashboard
**Depends on**: Phase 17
**Requirements**: OPS-01, OPS-02, OPS-03, OPS-04
**Success Criteria** (what must be TRUE):
  1. Operator sees a table listing each pod's service_instance_id, pod name, and current leader/follower role
  2. Operator sees time series graphs for all 11 pipeline counters (snmp_event_published_total, snmp_poll_executed_total, etc.) broken down by pod
  3. Operator sees time series graphs for .NET runtime metrics (GC collections, memory, thread pool) broken down by pod
  4. Dashboard refreshes automatically at a configurable interval showing live data
**Plans**: TBD

Plans:
- [ ] 18-01: Operations dashboard JSON (pod identity table, pipeline counter panels, .NET runtime panels, auto-refresh)

### Phase 19: Business Dashboard
**Goal**: Users can view current SNMP gauge and info metric values for any device in dynamically-populated tables without hardcoded device names
**Depends on**: Phase 17
**Requirements**: BIZ-01, BIZ-02, BIZ-03, BIZ-04
**Success Criteria** (what must be TRUE):
  1. User sees a gauge metrics table with columns: service_instance_id, device_name, metric_name, oid, snmp_type, and value
  2. User sees an info metrics table with columns: service_instance_id, device_name, metric_name, oid, and value
  3. Tables auto-refresh to show live current values without manual page reload
  4. Adding a new device to the system automatically populates it in the tables (no dashboard edits needed)
**Plans**: TBD

Plans:
- [ ] 19-01: Business dashboard JSON (gauge metrics table, info metrics table, auto-refresh, device-agnostic queries)

## Progress

**Execution Order:**
Phases execute in numeric order: 17 -> 18 -> 19

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 17. Dashboard Provisioning | 0/1 | Not started | - |
| 18. Operations Dashboard | 0/1 | Not started | - |
| 19. Business Dashboard | 0/1 | Not started | - |
