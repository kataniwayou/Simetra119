# Requirements: SNMP Monitoring System

**Defined:** 2026-03-07
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly (gauge/info), and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1.1 Requirements

Requirements for v1.1 Device Simulation milestone. Each maps to roadmap phases.

### OID Map Structure

- [x] **OIDM-01**: OID map naming convention uses device-type prefix + metric + index suffix (e.g., `obp_link_state_L1`, `npb_port_rx_octets_P1`)
- [x] **OIDM-02**: OID map structure decision — single shared list vs per-device-type separation (to be decided during phase discussion)
- [x] **OIDM-03**: OID map populated for OBP device — 4 links with realistic OID coverage (state, channel, optical power R1-R4)
- [ ] **OIDM-04**: OID map populated for NPB device — 8 ports with realistic OID coverage (system health, per-port traffic, port status)

### Simulator

- [ ] **SIM-01**: OBP simulator updated to 4 links with realistic OID subset (not exhaustive MIB)
- [ ] **SIM-02**: OBP simulator sends StateChange traps for all 4 links
- [ ] **SIM-03**: NPB simulator updated to realistic OID subset across 8 ports (core health + per-port traffic)
- [ ] **SIM-04**: NPB simulator sends realistic trap types (link up/down)
- [ ] **SIM-05**: Both simulators use `Simetra.{DeviceName}` community string convention
- [ ] **SIM-06**: Simulator K8s deployments updated with snmp-collector integration

### Device Documentation

- [x] **DOC-01**: OBP OID documentation — each polled OID with value meaning, units, expected ranges
- [ ] **DOC-02**: NPB OID documentation — each polled OID with value meaning, units, expected ranges

### MetricPoll Configuration

- [ ] **POLL-01**: snmp-collector ConfigMap updated with OBP MetricPoll groups matching OID map
- [ ] **POLL-02**: snmp-collector ConfigMap updated with NPB MetricPoll groups matching OID map

## v2 Requirements

### Advanced Collection

- **ADV-01**: SNMP table walk (GETBULK) for dynamic OID discovery
- **ADV-02**: SNMPv3 support if future devices require it

### Operational Enhancements

- **OPS-01**: Hot-reloadable device configuration (add/remove devices, change OIDs/intervals without restart)
- **OPS-02**: Grafana dashboard templates for NPB and OBP devices
- **OPS-03**: Prometheus alerting rules for common failure conditions

## Out of Scope

| Feature | Reason |
|---------|--------|
| Exhaustive MIB simulation | Realistic subset sufficient for proof of concept |
| Device management / SNMP SET | Monitor only |
| SNMPv3 auth / USM security | Target devices use v2c |
| Full 8-link OBP simulation | 4 links sufficient for v1.1 proof of concept |
| Grafana dashboards | Deferred to v2 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| OIDM-01 | Phase 11 | Complete |
| OIDM-02 | Phase 11 | Complete |
| OIDM-03 | Phase 11 | Complete |
| OIDM-04 | Phase 12 | Pending |
| SIM-01 | Phase 13 | Pending |
| SIM-02 | Phase 13 | Pending |
| SIM-03 | Phase 13 | Pending |
| SIM-04 | Phase 13 | Pending |
| SIM-05 | Phase 13 | Pending |
| SIM-06 | Phase 14 | Pending |
| DOC-01 | Phase 11 | Complete |
| DOC-02 | Phase 12 | Pending |
| POLL-01 | Phase 14 | Pending |
| POLL-02 | Phase 14 | Pending |

**Coverage:**
- v1.1 requirements: 14 total
- Mapped to phases: 14
- Unmapped: 0

---
*Requirements defined: 2026-03-07*
