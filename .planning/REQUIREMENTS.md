# Requirements: SNMP Monitoring System

**Defined:** 2026-03-22
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.3 Requirements

Requirements for Metric Validity & Correctness milestone. Each maps to roadmap phases.

### Pipeline Counter Validity

- [ ] **MCV-01**: `snmp.event.published` increments exactly once per OID entering the pipeline (poll path)
- [ ] **MCV-02**: `snmp.event.published` increments exactly once per OID entering the pipeline (trap path)
- [ ] **MCV-03**: `snmp.event.handled` increments only for mapped OIDs that reach the terminal handler
- [ ] **MCV-04**: `snmp.event.handled` does NOT increment for unmapped/rejected OIDs
- [ ] **MCV-05**: `snmp.event.rejected` increments only for unmapped OIDs (not in oidmaps.json)
- [ ] **MCV-06**: `snmp.event.rejected` does NOT increment for mapped OIDs
- [ ] **MCV-07**: `snmp.event.errors` stays 0 during a full normal E2E run (safety-net counter)
- [ ] **MCV-08**: `snmp.poll.executed` increments once per poll cycle regardless of success/failure
- [ ] **MCV-09**: `snmp.trap.received` increments for valid community traps only
- [ ] **MCV-10**: `snmp.trap.auth_failed` increments for bad community traps and does NOT increment `trap.received`
- [ ] **MCV-11**: `snmp.poll.unreachable` increments after 3 consecutive poll failures to unreachable device
- [ ] **MCV-12**: `snmp.poll.recovered` increments when previously unreachable device succeeds
- [ ] **MCV-13**: `snmp.tenantvector.routed` increments when tenant vector fan-out write succeeds

### Command Counter Validity

- [ ] **CCV-01**: `snmp.command.dispatched` increments when SnapshotJob enqueues a command at tier=4
- [ ] **CCV-02**: `snmp.command.suppressed` increments on repeated tier=4 within suppression window
- [ ] **CCV-03**: `snmp.command.dispatched` does NOT increment when command is suppressed
- [ ] **CCV-04**: `snmp.command.failed` increments on SET timeout or OID-not-found

### Business Metric Value Correctness

- [ ] **MVC-01**: `snmp_gauge` value matches simulator Gauge32 value (set 42, Prometheus shows 42)
- [ ] **MVC-02**: `snmp_gauge` value matches simulator Integer32 value
- [ ] **MVC-03**: `snmp_gauge` value matches simulator Counter32 raw value
- [ ] **MVC-04**: `snmp_gauge` value matches simulator Counter64 raw value
- [ ] **MVC-05**: `snmp_gauge` value matches simulator TimeTicks value
- [ ] **MVC-06**: `snmp_info` value label matches simulator OctetString value
- [ ] **MVC-07**: `snmp_info` value label matches simulator IpAddress value
- [ ] **MVC-08**: `snmp_gauge` value updates when simulator value changes (set 42→99, Prometheus shows 99)

### Label Correctness

- [ ] **MLC-01**: `snmp_gauge` carries correct `source="poll"` label for polled OIDs
- [ ] **MLC-02**: `snmp_gauge` carries correct `source="trap"` label for trap-originated OIDs
- [ ] **MLC-03**: `snmp_gauge` carries correct `source="command"` label for SET response OIDs
- [ ] **MLC-04**: `snmp_gauge` carries correct `source="synthetic"` label for aggregated metrics
- [ ] **MLC-05**: `snmp_gauge` `snmp_type` label matches actual SNMP type (gauge32, integer32, counter32, counter64, timeticks)
- [ ] **MLC-06**: `snmp_info` `snmp_type` label matches actual SNMP type (octetstring, ipaddress)
- [ ] **MLC-07**: `resolved_name` label matches oidmaps.json mapping for the OID
- [ ] **MLC-08**: `device_name` label matches the device's community-derived name

### Negative Proofs

- [ ] **MNP-01**: Heartbeat OID does NOT produce `snmp_gauge` or `snmp_info` (rejected as unmapped)
- [ ] **MNP-02**: Unmapped OID does NOT produce `snmp_gauge` or `snmp_info`
- [ ] **MNP-03**: Bad community trap does NOT increment `snmp.trap.received`
- [ ] **MNP-04**: `snmp.trap.dropped` stays 0 during normal E2E run (safety-net counter)
- [ ] **MNP-05**: Follower pod does NOT export `snmp_gauge`/`snmp_info` to Prometheus

## Out of Scope

| Feature | Reason |
|---------|--------|
| `snmp.event.errors` fault injection | Requires C# code modification, not E2E testable |
| `snmp.trap.dropped` flood test | Requires channel backpressure, not achievable via simulator |
| Duration histogram bucket validation | Bucket boundaries are OTel defaults, not application logic |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| MCV-01 | Pending | Pending |
| MCV-02 | Pending | Pending |
| MCV-03 | Pending | Pending |
| MCV-04 | Pending | Pending |
| MCV-05 | Pending | Pending |
| MCV-06 | Pending | Pending |
| MCV-07 | Pending | Pending |
| MCV-08 | Pending | Pending |
| MCV-09 | Pending | Pending |
| MCV-10 | Pending | Pending |
| MCV-11 | Pending | Pending |
| MCV-12 | Pending | Pending |
| MCV-13 | Pending | Pending |
| CCV-01 | Pending | Pending |
| CCV-02 | Pending | Pending |
| CCV-03 | Pending | Pending |
| CCV-04 | Pending | Pending |
| MVC-01 | Pending | Pending |
| MVC-02 | Pending | Pending |
| MVC-03 | Pending | Pending |
| MVC-04 | Pending | Pending |
| MVC-05 | Pending | Pending |
| MVC-06 | Pending | Pending |
| MVC-07 | Pending | Pending |
| MVC-08 | Pending | Pending |
| MLC-01 | Pending | Pending |
| MLC-02 | Pending | Pending |
| MLC-03 | Pending | Pending |
| MLC-04 | Pending | Pending |
| MLC-05 | Pending | Pending |
| MLC-06 | Pending | Pending |
| MLC-07 | Pending | Pending |
| MLC-08 | Pending | Pending |
| MNP-01 | Pending | Pending |
| MNP-02 | Pending | Pending |
| MNP-03 | Pending | Pending |
| MNP-04 | Pending | Pending |
| MNP-05 | Pending | Pending |

**Coverage:**
- v2.3 requirements: 38 total
- Mapped to phases: 0
- Unmapped: 38

---
*Requirements defined: 2026-03-22*
