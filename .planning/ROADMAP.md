# Milestone v1.4: E2E System Verification

**Status:** In Progress
**Phases:** 20-24
**Total Plans:** TBD

## Overview

This milestone builds an E2E test harness that proves the full SNMP-to-Prometheus pipeline works correctly under normal operation, configuration mutations, and edge cases. A dedicated test simulator covers scenarios that existing OBP/NPB simulators cannot. A bash test runner orchestrates 42 test scenarios sequentially, querying Prometheus HTTP API and kubectl logs for pass/fail evidence. No SnmpCollector code is modified -- findings are documented only.

## Phases

- [x] **Phase 20: Test Simulator** - Dedicated pysnmp simulator for E2E edge cases with mapped + unmapped OIDs and configurable traps
- [x] **Phase 21: Test Harness and Pipeline Counter Verification** - Bash test runner with polling utilities and delta assertions, proving all 10 pipeline counters
- [ ] **Phase 22: Business Metric and Unknown OID Verification** - Verify snmp_gauge/snmp_info correctness and unknown OID classification with ConfigMap snapshot/restore
- [ ] **Phase 23: OID Map Mutation and Device Lifecycle Verification** - Verify runtime configuration changes propagate correctly to Prometheus
- [ ] **Phase 24: Watcher Resilience and Comprehensive Report** - Verify ConfigMap watcher error handling and generate final pass/fail report

## Phase Details

### Phase 20: Test Simulator

**Goal**: A controllable SNMP test device is deployed in K8s that serves known OIDs (gauge + info types), deliberately unmapped OIDs, and sends traps on demand
**Depends on**: Nothing (first phase of v1.4)
**Requirements**: SIM-01, SIM-02, SIM-03
**Success Criteria** (what must be TRUE):
  1. Test simulator pod is running in namespace simetra and responds to SNMP GET requests for mapped OIDs with deterministic values
  2. Test simulator exposes unmapped OIDs (outside oidmaps.json) that the collector will classify as "Unknown"
  3. Test simulator sends SNMP traps with community string Simetra.E2E-SIM at configurable intervals
  4. OID map fixture entries and device config for E2E-SIM exist as ConfigMap merge fixtures
**Plans:** 2 plans
Plans:
- [x] 20-01-PLAN.md — Create E2E simulator Python application (e2e_simulator.py, Dockerfile)
- [x] 20-02-PLAN.md — Deploy to K8s and configure collector ConfigMaps

### Phase 21: Test Harness and Pipeline Counter Verification

**Goal**: A reusable test runner with poll-until-satisfied utilities proves all 10 pipeline counters increment correctly from existing simulator activity
**Depends on**: Phase 20 (simulator must be deployable, but pipeline counter tests use existing OBP/NPB simulators)
**Requirements**: INFRA-01, PIPE-01, PIPE-02, PIPE-03
**Success Criteria** (what must be TRUE):
  1. Test runner executes scenarios sequentially with pre-flight checks (Prometheus reachable, pods running, port-forwards active)
  2. Poll-until-satisfied utility queries Prometheus with 30s timeout and 3s interval (no fixed sleeps)
  3. Counter assertions use delta patterns (before/after values) filtered by device_name to exclude heartbeat noise
  4. All 10 pipeline counters show non-zero deltas during test window: trap_received, trap_auth_failed, trap_dropped, poll_executed, poll_unreachable, poll_recovered, event_handled, event_validation_failed, oid_resolved, oid_unresolved
  5. Trap-specific and poll-specific counters verified with dedicated scenarios (auth failure, unreachability)
**Plans:** 2 plans
Plans:
- [x] 21-01-PLAN.md — E2E test runner framework (run-all.sh, lib/ utilities, fixture ConfigMap)
- [x] 21-02-PLAN.md — All 10 pipeline counter scenario scripts

### Phase 22: Business Metric and Unknown OID Verification

**Goal**: The full SNMP-to-Prometheus data path is verified: gauge and info metrics carry correct labels and values, and unmapped OIDs are classified as "Unknown"
**Depends on**: Phase 20 (test simulator for unmapped OID scenarios), Phase 21 (test harness framework)
**Requirements**: BIZ-01, BIZ-02, BIZ-03, BIZ-04, INFRA-02
**Success Criteria** (what must be TRUE):
  1. snmp_gauge metrics from poll and trap sources carry correct metric_name, device_name, oid, and snmp_type labels with numeric values
  2. snmp_info metrics carry correct labels including string value label
  3. Unmapped OIDs from test simulator appear in Prometheus with metric_name="Unknown"
  4. Trap-originated metrics from test simulator appear with correct device_name derived from community string
  5. ConfigMap snapshot/restore utility safely backs up and restores oidmaps and devices ConfigMaps before/after mutation tests
**Plans:** 2 plans
Plans:
- [ ] 22-01-PLAN.md — ConfigMap snapshot/restore infrastructure + gauge/info label verification scenarios
- [ ] 22-02-PLAN.md — Unknown OID mutation test + trap-originated metric verification

### Phase 23: OID Map Mutation and Device Lifecycle Verification

**Goal**: Runtime configuration changes (OID rename/remove/add, device add/remove/modify) propagate correctly to Prometheus metrics without pod restarts
**Depends on**: Phase 22 (ConfigMap snapshot/restore infrastructure, business metric verification patterns)
**Requirements**: MUT-01, MUT-02, MUT-03, DEV-01, DEV-02, DEV-03
**Success Criteria** (what must be TRUE):
  1. Renaming an OID in oidmaps ConfigMap causes new metric_name to appear in Prometheus (old name persists until 5-min staleness)
  2. Removing an OID from oidmaps ConfigMap causes that metric to be classified as metric_name="Unknown"
  3. Adding an OID to oidmaps ConfigMap causes a previously unknown OID to get the correct metric_name
  4. Adding a new device to devices ConfigMap results in new poll metrics appearing in Prometheus within 30s
  5. Removing a device from devices ConfigMap stops new poll metrics (verified via counter delta stagnation, not metric absence)
**Plans**: TBD

### Phase 24: Watcher Resilience and Comprehensive Report

**Goal**: ConfigMap watchers handle error conditions gracefully, and a comprehensive report documents pass/fail status with evidence for all test scenarios
**Depends on**: Phase 23 (all test scenarios must be implemented before final report)
**Requirements**: WATCH-01, WATCH-02, WATCH-03, WATCH-04, INFRA-03, RPT-01
**Success Criteria** (what must be TRUE):
  1. OID map ConfigMap change is detected by watcher within seconds (verified via pod log evidence showing reload)
  2. Device ConfigMap change triggers DynamicPollScheduler reconciliation (verified via pod log evidence)
  3. Invalid JSON in a ConfigMap does not crash any pod (all pods remain Running, error logged)
  4. Watcher reconnection after disruption is verified via log observation
  5. Final report shows pass/fail status for every test scenario with Prometheus query results and log excerpts as evidence

## Progress

**Execution Order:** 20 -> 21 -> 22 -> 23 -> 24

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 20. Test Simulator | 2/2 | Complete | 2026-03-09 |
| 21. Test Harness + Pipeline Counters | 2/2 | Complete | 2026-03-09 |
| 22. Business Metrics + Unknown OIDs | 0/TBD | Not started | - |
| 23. OID Mutations + Device Lifecycle | 0/TBD | Not started | - |
| 24. Watcher Resilience + Report | 0/TBD | Not started | - |

---

_For current project status, see .planning/STATE.md_
