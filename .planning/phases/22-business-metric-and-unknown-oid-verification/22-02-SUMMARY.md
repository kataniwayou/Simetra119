---
phase: 22-business-metric-and-unknown-oid-verification
plan: 02
subsystem: testing
tags: [e2e, prometheus, unknown-oid, trap, configmap-mutation, bash]

requires:
  - phase: 22-01
    provides: "ConfigMap snapshot/restore utilities and label verification patterns"
provides:
  - "Unknown OID classification test proving unmapped OIDs get metric_name=Unknown"
  - "Trap-originated metric verification proving trap pipeline to Prometheus"
  - "Unmapped OID fixture ConfigMap with .999.2.1.0 and .999.2.2.0"
affects: [23-mutation-testing]

tech-stack:
  added: []
  patterns:
    - "ConfigMap mutation testing: snapshot, apply mutated fixture, verify, restore"
    - "jq select filter for specific OID in Prometheus result array"
    - "Deadline-based polling loop for eventually-consistent metric appearance"

key-files:
  created:
    - tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml
    - tests/e2e/scenarios/15-unknown-oid.sh
    - tests/e2e/scenarios/16-trap-originated.sh
  modified: []

decisions: []

metrics:
  duration: "3m"
  completed: "2026-03-09"
---

# Phase 22 Plan 02: Unknown OID and Trap Verification Summary

ConfigMap mutation test and trap-originated metric verification for E2E test suite, proving unmapped OIDs classify as Unknown and trap data flows to Prometheus.

## What Was Done

### Task 1: Create unmapped OID fixture and unknown OID + trap scenarios

**Fixture: e2e-sim-unmapped-configmap.yaml**
- Full simetra-devices ConfigMap with 3 devices (OBP-01, NPB-01, E2E-SIM)
- OBP-01 and NPB-01 entries identical to production simetra-devices.yaml
- E2E-SIM has 9 OIDs: 7 original mapped + 2 unmapped (.999.2.1.0 Gauge32, .999.2.2.0 OctetString)
- No FAKE-UNREACHABLE device (that fixture is separate for scenarios 06-07)

**Scenario 15: Unknown OID classification (15-unknown-oid.sh)**
- Snapshots current ConfigMaps via `snapshot_configmaps`
- Applies mutated fixture with `kubectl apply`
- Polls up to 60s for `snmp_gauge{device_name="E2E-SIM",metric_name="Unknown"}` to appear
- Verifies gauge-type unknown OID (.999.2.1.0) has correct labels (snmp_type=gauge32)
- Verifies info-type unknown OID (.999.2.2.0) via `snmp_info{metric_name="Unknown"}` (snmp_type=octetstring)
- Restores original ConfigMaps via `restore_configmaps`

**Scenario 16: Trap-originated metrics (16-trap-originated.sh)**
- Polls up to 45s for `snmp_gauge{device_name="E2E-SIM",source="trap",metric_name="e2e_gauge_test"}`
- Verifies OID, device_name, source=trap, snmp_type=gauge32 labels
- No ConfigMap mutation needed (traps flow continuously from E2E-SIM)

## Commits

| Hash | Message |
|------|---------|
| 5432480 | feat(22-02): add unknown OID mutation test and trap-originated verification |

## Deviations from Plan

None -- plan executed exactly as written.

## Next Phase Readiness

Phase 22 is now complete (2/2 plans). All E2E scenarios 01-17 cover:
- Pipeline counters (01-10)
- Business metric labels (11-14)
- Unknown OID classification (15)
- Trap-originated metrics (16)
- SNMP type labels (17)

Ready for Phase 23 (mutation testing) or Phase 24 (final verification).
