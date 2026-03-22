---
status: complete
phase: 71-negative-proofs
source: 71-01-SUMMARY.md
started: 2026-03-22T23:00:00Z
updated: 2026-03-22T23:05:00Z
---

## Current Test

[testing complete]

## Tests

### 1. MNP-01: heartbeat not in snmp_info + no Unknown resolved_name
expected: snmp_info{device_name="Simetra"} count == 0 AND snmp_gauge{device_name="Simetra",resolved_name="Unknown"} count == 0
result: pass

### 2. MNP-02: unmapped OIDs absent from Prometheus
expected: snmp_gauge/snmp_info for OIDs .999.2.1 and .999.2.2 count == 0
result: pass

### 3. MNP-03: bad-community traps produce no business metrics
expected: snmp_gauge/snmp_info{device_name="unknown"} count == 0 after auth_failed confirms bad trap arrived
result: pass

### 4. MNP-04: trap.dropped stays 0
expected: snmp_trap_dropped_total delta == 0 during normal operation
result: pass

### 5. MNP-05: follower pod exports no snmp_gauge/snmp_info
expected: follower pod identified via k8s_pod_name, no snmp_gauge/snmp_info for that pod
result: pass

## Summary

total: 5
passed: 5
issues: 0
pending: 0
skipped: 0

## Gaps

[none]
