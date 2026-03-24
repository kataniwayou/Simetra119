---
status: complete
phase: 82-fixture-and-oid-mapping
source: [82-01-SUMMARY.md, 82-02-SUMMARY.md]
started: 2026-03-24T14:45:00Z
updated: 2026-03-24T15:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Four tenants visible in Grafana
expected: Query `tenant_evaluation_state` shows T1_P1, T2_P1, T1_P2, T2_P2 with correct priority labels (1 or 2)
result: pass

### 2. All tenants Healthy after grace window
expected: All 4 v2.6 tenants show state=1 (Healthy) in `tenant_evaluation_state` metric
result: pass

### 3. OID metric names resolve correctly
expected: Querying `snmp_gauge{resolved_name=~"e2e_T.*"}` in Prometheus returns metrics for all 4 tenants — no "Unknown" resolved names
result: pass

### 4. OID mapping file sources and lookups work
expected: Running `bash -c 'source tests/e2e/lib/oid_map.sh && echo ${OID_MAP[T1_P1.E.1.oid]}'` returns `8.1`
result: pass

### 5. Simulator responds to per-OID value changes
expected: Calling `sim_set_oid 8.1 0` via simulator HTTP API changes the T1_P1 eval1 OID value, visible in Prometheus as value 0
result: pass

## Summary

total: 5
passed: 5
issues: 0
pending: 0
skipped: 0

## Gaps

[none]
