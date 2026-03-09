---
phase: 20-test-simulator
verified: 2026-03-09T18:00:00Z
status: passed
score: 4/4 must-haves verified
---

# Phase 20: Test Simulator Verification Report

**Phase Goal:** A controllable SNMP test device is deployed in K8s that serves known OIDs (gauge + info types), deliberately unmapped OIDs, and sends traps on demand
**Verified:** 2026-03-09
**Status:** passed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Test simulator pod responds to SNMP GET for mapped OIDs with deterministic values | VERIFIED | `e2e_simulator.py` registers 7 mapped OIDs under `.999.1.x` with static values (42, 100, 5000, etc.) via `DynamicInstance` class; SNMP engine listens on 0.0.0.0:161; K8s Deployment + Service + liveness/readiness probes exist |
| 2 | Test simulator exposes unmapped OIDs outside oidmaps.json | VERIFIED | 2 unmapped OIDs under `.999.2.x` (Gauge32=99, OctetString="UNMAPPED") registered in simulator; `.999.2` OIDs confirmed absent from `simetra-oidmaps.yaml` (0 matches); not polled by device config |
| 3 | Test simulator sends SNMP traps with community Simetra.E2E-SIM at configurable intervals | VERIFIED | Dual trap loops: `valid_trap_loop` sends with community `Simetra.E2E-SIM` every 30s, `bad_community_trap_loop` sends with `BadCommunity` every 45s; target is `simetra-pods.simetra.svc.cluster.local:10162`; both wrapped in `supervised_task` for crash recovery |
| 4 | OID map fixture entries and device config for E2E-SIM exist as ConfigMap merge fixtures | VERIFIED | `simetra-oidmaps.yaml` has 7 e2e_ entries (e2e_gauge_test through e2e_ip_test); `simetra-devices.yaml` has E2E-SIM device with 7 poll OIDs at 10s interval pointing to `e2e-simulator.simetra.svc.cluster.local:161` |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `simulators/e2e-sim/e2e_simulator.py` | SNMP simulator source | VERIFIED (277 lines, no stubs) | Full pysnmp implementation with 9 OIDs, dual trap loops, graceful shutdown, supervised tasks |
| `simulators/e2e-sim/requirements.txt` | Python dependencies | VERIFIED | pysnmp==7.1.22 |
| `simulators/e2e-sim/Dockerfile` | Container build | VERIFIED (12 lines) | python:3.12-slim, EXPOSE 161/udp |
| `deploy/k8s/simulators/e2e-sim-deployment.yaml` | K8s Deployment + Service | VERIFIED (114 lines) | Deployment with env vars, resource limits, liveness + readiness probes (SNMP GET check), ClusterIP Service on UDP/161 |
| `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` | 7 e2e_ OID map entries | VERIFIED | 7 entries covering .999.1.1-7.0; no .999.2.x entries (unmapped OIDs correctly excluded) |
| `deploy/k8s/snmp-collector/simetra-devices.yaml` | E2E-SIM device config | VERIFIED | Device name E2E-SIM, address e2e-simulator.simetra.svc.cluster.local:161, 7 poll OIDs at 10s |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| Simulator OIDs (.999.1.x) | oidmaps.json entries | OID string match | WIRED | All 7 OIDs match exactly: .999.1.{1-7}.0 in both simulator MAPPED_OIDS and oidmaps ConfigMap |
| Simulator OIDs (.999.1.x) | devices.json poll list | OID string match | WIRED | All 7 OIDs in E2E-SIM device MetricPolls match simulator MAPPED_OIDS |
| Unmapped OIDs (.999.2.x) | oidmaps.json | Absence check | WIRED | .999.2.x OIDs correctly absent from oidmaps (will be classified as Unknown by collector) |
| Trap target | Collector service | DNS hostname | WIRED | `simetra-pods.simetra.svc.cluster.local:10162` matches collector headless service trap port |
| K8s Service | Deployment | Label selector | WIRED | Both use `app: e2e-simulator` selector |
| Health probes | Simulator agent | SNMP GET to .999.1.1.0 | WIRED | Liveness/readiness probes issue SNMP GET with correct community and OID |

### SNMP Type Coverage

| SNMP Type | OID Suffix | Metric Name | Static Value |
|-----------|-----------|-------------|--------------|
| Gauge32 | .999.1.1.0 | e2e_gauge_test | 42 |
| Integer32 | .999.1.2.0 | e2e_integer_test | 100 |
| Counter32 | .999.1.3.0 | e2e_counter32_test | 5000 |
| Counter64 | .999.1.4.0 | e2e_counter64_test | 1000000 |
| TimeTicks | .999.1.5.0 | e2e_timeticks_test | 360000 |
| OctetString | .999.1.6.0 | e2e_info_test | E2E-TEST-VALUE |
| IpAddress | .999.1.7.0 | e2e_ip_test | 10.0.0.1 |

All 7 SNMP types covered with deterministic static values.

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| SIM-01: Dedicated E2E test simulator built with pysnmp, deployed in K8s namespace simetra | SATISFIED | Simulator in `simulators/e2e-sim/`, K8s manifests deploy to namespace simetra |
| SIM-02: Test simulator exposes mapped OIDs and deliberately unmapped OIDs | SATISFIED | 7 mapped (.999.1.x) + 2 unmapped (.999.2.x) OIDs |
| SIM-03: Test simulator sends SNMP traps on configurable intervals with known community string | SATISFIED | Dual trap loops with env-var intervals (TRAP_INTERVAL, BAD_TRAP_INTERVAL) |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None | - | - | - | No anti-patterns detected |

### Human Verification Required

### 1. Simulator Responds to SNMP GET
**Test:** Build image (`docker build -t e2e-simulator:local -f simulators/e2e-sim/Dockerfile simulators/e2e-sim/`), deploy to K8s, exec into a pod and run `snmpget -v2c -c Simetra.E2E-SIM e2e-simulator.simetra.svc.cluster.local 1.3.6.1.4.1.47477.999.1.1.0`
**Expected:** Returns Gauge32: 42
**Why human:** Requires running K8s cluster and SNMP tools

### 2. Traps Arrive at Collector
**Test:** Check collector pod logs after simulator has been running for >30 seconds
**Expected:** Trap received logs with community Simetra.E2E-SIM and auth-failed logs for BadCommunity traps
**Why human:** Requires live cluster observation

### 3. Unmapped OIDs Classified as Unknown
**Test:** Query Prometheus for `snmp_gauge{device_name="E2E-SIM",metric_name="Unknown"}`
**Expected:** Metrics appear with the unmapped OIDs after adding .999.2.x OIDs to E2E-SIM device poll list
**Why human:** Requires Prometheus query on live cluster

---

_Verified: 2026-03-09_
_Verifier: Claude (gsd-verifier)_
