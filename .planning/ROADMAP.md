# Roadmap: v1.3 Grafana Dashboards

## Overview

This milestone delivers two purpose-built Grafana dashboard JSON files for the SNMP monitoring system. Claude creates the dashboard JSON files; the user manually imports them into Grafana via the UI. Phase 18 builds the operations dashboard for pipeline health and pod observability. Phase 19 builds the business dashboard with device-agnostic metric tables.

## Milestones

- [x] **v1.0 Foundation** - Phases 1-10 (shipped 2026-03-07)
- [x] **v1.1 Device Simulation** - Phases 11-14 (shipped 2026-03-08)
- [x] **v1.2 Operational Enhancements** - Phases 15-16 (shipped 2026-03-08)
- [ ] **v1.3 Grafana Dashboards** - Phases 18-19 (in progress)

## Phases

- [ ] **Phase 18: Operations Dashboard** - Pod identity, pipeline counters, and .NET runtime observability
- [ ] **Phase 19: Business Dashboard** - Device-agnostic gauge and info metric tables

## Phase Details

### Phase 18: Operations Dashboard
**Goal**: Operators can monitor pipeline health, pod roles, and .NET runtime metrics across all replicas from a single Grafana dashboard JSON file
**Depends on**: Nothing (first phase of v1.3)
**Requirements**: DASH-01, DASH-02, OPS-01, OPS-02, OPS-03, OPS-04
**Success Criteria** (what must be TRUE):
  1. Operations dashboard JSON file exists in deploy/grafana/dashboards/ ready for manual Grafana import
  2. Stale reference project files under deploy/grafana/ are deleted and replaced
  3. Operator sees a table listing each pod's service_instance_id, pod name, and current leader/follower role
  4. Operator sees time series graphs for all 11 pipeline counters (snmp_event_published_total, snmp_poll_executed_total, etc.) broken down by pod
  5. Operator sees time series graphs for .NET runtime metrics (GC collections, memory, thread pool) broken down by pod
  6. Dashboard refreshes automatically at a configurable interval showing live data
**Plans:** 1 plan

Plans:
- [ ] 18-01-PLAN.md -- Delete stale files, create operations dashboard JSON (pod table, 11 pipeline panels, 6 runtime panels)

### Phase 19: Business Dashboard
**Goal**: Users can view current SNMP gauge and info metric values for any device in dynamically-populated tables without hardcoded device names
**Depends on**: Nothing (can run in parallel with Phase 18)
**Requirements**: BIZ-01, BIZ-02, BIZ-03, BIZ-04
**Success Criteria** (what must be TRUE):
  1. Business dashboard JSON file exists in deploy/grafana/dashboards/ ready for manual Grafana import
  2. User sees a gauge metrics table with columns: service_instance_id, device_name, metric_name, oid, snmp_type, and value
  3. User sees an info metrics table with columns: service_instance_id, device_name, metric_name, oid, and value
  4. Tables auto-refresh to show live current values without manual page reload
  5. Adding a new device to the system automatically populates it in the tables (no dashboard edits needed)
**Plans**: TBD

Plans:
- [ ] 19-01: Business dashboard JSON (gauge metrics table, info metrics table, auto-refresh, device-agnostic queries)

## Progress

**Execution Order:**
Phases 18 and 19 can execute in parallel (no dependencies between them).

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 18. Operations Dashboard | 0/1 | Not started | - |
| 19. Business Dashboard | 0/1 | Not started | - |
