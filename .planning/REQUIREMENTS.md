# Requirements: SNMP Monitoring System

**Defined:** 2026-03-09
**Core Value:** Every SNMP OID -- from a trap or a poll -- gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.4 Requirements

Requirements for E2E System Verification milestone. Each maps to roadmap phases.

### Test Simulator

- [x] **SIM-01**: Dedicated E2E test simulator built with pysnmp, deployed in K8s namespace simetra
- [x] **SIM-02**: Test simulator exposes mapped OIDs (gauge, info types) and deliberately unmapped OIDs for "Unknown" testing
- [x] **SIM-03**: Test simulator sends SNMP traps on configurable intervals with known community string

### Pipeline Counter Verification

- [x] **PIPE-01**: All 10 pipeline counters verified via Prometheus delta queries showing trend changes from simulator activity
- [x] **PIPE-02**: Trap-specific counters (trap_received, trap_auth_failed, trap_dropped) verified with dedicated trap scenarios
- [x] **PIPE-03**: Poll-specific counters (poll_executed, poll_unreachable, poll_recovered) verified with device reachability scenarios

### Business Metric Verification

- [x] **BIZ-01**: snmp_gauge metrics verified with correct labels (metric_name, device_name, oid, snmp_type) and numeric values
- [x] **BIZ-02**: snmp_info metrics verified with correct labels including string value label
- [x] **BIZ-03**: Unmapped OIDs classified as metric_name="Unknown" in Prometheus
- [x] **BIZ-04**: Trap-originated metrics appear in Prometheus with correct device_name and labels

### OID Map Mutation Verification

- [x] **MUT-01**: OID map metric rename reflected in Prometheus (new metric_name appears, old persists until stale)
- [x] **MUT-02**: OID removal causes metric to be classified as "Unknown"
- [x] **MUT-03**: OID addition causes previously unknown OID to get correct metric_name

### Device Lifecycle Verification

- [x] **DEV-01**: Adding a new device to devices ConfigMap results in new poll metrics appearing in Prometheus
- [x] **DEV-02**: Removing a device stops new poll metrics (verified via counter delta = 0)
- [x] **DEV-03**: Modifying device poll interval changes metric collection frequency

### ConfigMap Watcher Verification

- [ ] **WATCH-01**: OID map ConfigMap change detected by watcher within seconds (verified via pod logs)
- [ ] **WATCH-02**: Device ConfigMap change triggers DynamicPollScheduler reconciliation (verified via pod logs)
- [ ] **WATCH-03**: Invalid JSON in ConfigMap does not crash pods (verified via pod status and logs)
- [ ] **WATCH-04**: Watcher reconnects after K8s API disruption (verified via logs after recovery)

### Test Infrastructure

- [x] **INFRA-01**: Test runner with poll-until-satisfied utilities and delta-based counter assertions
- [x] **INFRA-02**: ConfigMap snapshot/restore for safe mutation testing
- [ ] **INFRA-03**: Single comprehensive report with pass/fail evidence from logs and Prometheus queries

### Reporting

- [ ] **RPT-01**: Comprehensive E2E report with pass/fail status, evidence (Prometheus query results, log excerpts), and findings

## Out of Scope

| Feature | Reason |
|---------|--------|
| SnmpCollector code modifications | Findings-only milestone -- document issues, don't fix them |
| Existing simulator modifications | OBP/NPB simulators are untouched; new test simulator handles edge cases |
| Grafana verification | Tests verify Prometheus data only, not dashboard rendering |
| Performance/load testing | Not a load test -- functional verification only |
| Chaos testing (pod kills, network partitions) | Too complex for v1.4; leader failover deferred |
| Automated CI pipeline | Tests run manually; CI integration is future work |
| pytest framework | Bash test runner sufficient for sequential E2E scenarios |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| SIM-01 | Phase 20 | Complete |
| SIM-02 | Phase 20 | Complete |
| SIM-03 | Phase 20 | Complete |
| PIPE-01 | Phase 21 | Complete |
| PIPE-02 | Phase 21 | Complete |
| PIPE-03 | Phase 21 | Complete |
| BIZ-01 | Phase 22 | Complete |
| BIZ-02 | Phase 22 | Complete |
| BIZ-03 | Phase 22 | Complete |
| BIZ-04 | Phase 22 | Complete |
| MUT-01 | Phase 23 | Complete |
| MUT-02 | Phase 23 | Complete |
| MUT-03 | Phase 23 | Complete |
| DEV-01 | Phase 23 | Complete |
| DEV-02 | Phase 23 | Complete |
| DEV-03 | Phase 23 | Complete |
| WATCH-01 | Phase 24 | Pending |
| WATCH-02 | Phase 24 | Pending |
| WATCH-03 | Phase 24 | Pending |
| WATCH-04 | Phase 24 | Pending |
| INFRA-01 | Phase 21 | Complete |
| INFRA-02 | Phase 22 | Complete |
| INFRA-03 | Phase 24 | Pending |
| RPT-01 | Phase 24 | Pending |

**Coverage:**
- v1.4 requirements: 24 total
- Mapped to phases: 24/24
- Unmapped: 0

---
*Requirements defined: 2026-03-09*
*Last updated: 2026-03-09 after roadmap creation*
