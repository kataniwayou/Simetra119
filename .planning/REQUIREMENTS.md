# Requirements: SNMP Monitoring System

**Defined:** 2026-03-09
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.4 Requirements

Requirements for E2E System Verification milestone. Each maps to roadmap phases.

### Test Simulator

- [ ] **SIM-01**: Dedicated E2E test simulator built with pysnmp, deployed in K8s namespace simetra
- [ ] **SIM-02**: Test simulator exposes mapped OIDs (gauge, info types) and deliberately unmapped OIDs for "Unknown" testing
- [ ] **SIM-03**: Test simulator sends SNMP traps on configurable intervals with known community string

### Pipeline Counter Verification

- [ ] **PIPE-01**: All 10 pipeline counters verified via Prometheus delta queries showing trend changes from simulator activity
- [ ] **PIPE-02**: Trap-specific counters (trap_received, trap_auth_failed, trap_dropped) verified with dedicated trap scenarios
- [ ] **PIPE-03**: Poll-specific counters (poll_executed, poll_unreachable, poll_recovered) verified with device reachability scenarios

### Business Metric Verification

- [ ] **BIZ-01**: snmp_gauge metrics verified with correct labels (metric_name, device_name, oid, snmp_type) and numeric values
- [ ] **BIZ-02**: snmp_info metrics verified with correct labels including string value label
- [ ] **BIZ-03**: Unmapped OIDs classified as metric_name="Unknown" in Prometheus
- [ ] **BIZ-04**: Trap-originated metrics appear in Prometheus with correct device_name and labels

### OID Map Mutation Verification

- [ ] **MUT-01**: OID map metric rename reflected in Prometheus (new metric_name appears, old persists until stale)
- [ ] **MUT-02**: OID removal causes metric to be classified as "Unknown"
- [ ] **MUT-03**: OID addition causes previously unknown OID to get correct metric_name

### Device Lifecycle Verification

- [ ] **DEV-01**: Adding a new device to devices ConfigMap results in new poll metrics appearing in Prometheus
- [ ] **DEV-02**: Removing a device stops new poll metrics (verified via counter delta = 0)
- [ ] **DEV-03**: Modifying device poll interval changes metric collection frequency

### ConfigMap Watcher Verification

- [ ] **WATCH-01**: OID map ConfigMap change detected by watcher within seconds (verified via pod logs)
- [ ] **WATCH-02**: Device ConfigMap change triggers DynamicPollScheduler reconciliation (verified via pod logs)
- [ ] **WATCH-03**: Invalid JSON in ConfigMap does not crash pods (verified via pod status and logs)
- [ ] **WATCH-04**: Watcher reconnects after K8s API disruption (verified via logs after recovery)

### Test Infrastructure

- [ ] **INFRA-01**: Test runner with poll-until-satisfied utilities and delta-based counter assertions
- [ ] **INFRA-02**: ConfigMap snapshot/restore for safe mutation testing
- [ ] **INFRA-03**: Single comprehensive report with pass/fail evidence from logs and Prometheus queries

### Reporting

- [ ] **RPT-01**: Comprehensive E2E report with pass/fail status, evidence (Prometheus query results, log excerpts), and findings

## Out of Scope

| Feature | Reason |
|---------|--------|
| SnmpCollector code modifications | Findings-only milestone — document issues, don't fix them |
| Existing simulator modifications | OBP/NPB simulators are untouched; new test simulator handles edge cases |
| Grafana verification | Tests verify Prometheus data only, not dashboard rendering |
| Performance/load testing | Not a load test — functional verification only |
| Chaos testing (pod kills, network partitions) | Too complex for v1.4; leader failover deferred |
| Automated CI pipeline | Tests run manually; CI integration is future work |
| pytest framework | Bash test runner sufficient for sequential E2E scenarios |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| SIM-01 | TBD | Pending |
| SIM-02 | TBD | Pending |
| SIM-03 | TBD | Pending |
| PIPE-01 | TBD | Pending |
| PIPE-02 | TBD | Pending |
| PIPE-03 | TBD | Pending |
| BIZ-01 | TBD | Pending |
| BIZ-02 | TBD | Pending |
| BIZ-03 | TBD | Pending |
| BIZ-04 | TBD | Pending |
| MUT-01 | TBD | Pending |
| MUT-02 | TBD | Pending |
| MUT-03 | TBD | Pending |
| DEV-01 | TBD | Pending |
| DEV-02 | TBD | Pending |
| DEV-03 | TBD | Pending |
| WATCH-01 | TBD | Pending |
| WATCH-02 | TBD | Pending |
| WATCH-03 | TBD | Pending |
| WATCH-04 | TBD | Pending |
| INFRA-01 | TBD | Pending |
| INFRA-02 | TBD | Pending |
| INFRA-03 | TBD | Pending |
| RPT-01 | TBD | Pending |

**Coverage:**
- v1.4 requirements: 24 total
- Mapped to phases: 0 (pending roadmap)
- Unmapped: 24

---
*Requirements defined: 2026-03-09*
*Last updated: 2026-03-09 after initial definition*
