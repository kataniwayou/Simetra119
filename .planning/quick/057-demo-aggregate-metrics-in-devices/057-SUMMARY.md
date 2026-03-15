---
phase: quick/057-demo-aggregate-metrics-in-devices
plan: 01
subsystem: config
tags: [aggregate-metrics, devices-json, tenants-json, combined-metrics, v1.8, k8s, configmap]

# Dependency graph
requires:
  - phase: 40-metricpollob-aggregate-dispatch
    provides: AggregatedMetricName/Aggregator poll group fields fully wired in pipeline
provides:
  - 3 aggregate poll groups in all 3 devices.json locations (local, K8s standalone, production)
  - 3 aggregate tenant metric entries in both tenants.json locations
affects: [ops-dashboard, demo, end-to-end-validation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Aggregate poll groups as separate entries in Polls[] array (one AggregatedMetricName per group)"
    - "Aggregate metric names use Evaluate role in tenant Metrics[] for synthetic metric routing"

key-files:
  created: []
  modified:
    - src/SnmpCollector/config/devices.json
    - src/SnmpCollector/config/tenants.json
    - deploy/k8s/snmp-collector/simetra-devices.yaml
    - deploy/k8s/snmp-collector/simetra-tenants.yaml
    - deploy/k8s/production/configmap.yaml

key-decisions:
  - "Aggregate poll groups added as separate Polls[] entries (not fields on existing groups) — one AggregatedMetricName per poll group per v1.8 design"
  - "obp_mean_power_L1 placed in Priority 1 tenant (OBP) locally, Priority 3 tenant (OBP) in K8s — matches existing tenant assignment for OBP device"
  - "npb_total_rx_octets / npb_total_tx_octets placed in Priority 2 locally (NPB), Priority 1 in K8s — matches existing tenant assignment for NPB device"

patterns-established:
  - "Aggregate names never appear in oid_metric_map.json — synthetic metrics bypass OID resolution via Source == SnmpSource.Synthetic guard"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Quick Task 057: Demo Aggregate Metrics in Devices Summary

**NPB sum-of-octets and OBP mean-power aggregate poll groups added to all 3 devices.json locations and routed through tenant Metrics[] arrays in both tenants.json locations**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-15T10:49:49Z
- **Completed:** 2026-03-15T10:51:28Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Added 3 new aggregate poll groups (2 NPB sum, 1 OBP mean) to local dev, K8s standalone, and production devices config
- Added `obp_mean_power_L1`, `npb_total_rx_octets`, `npb_total_tx_octets` tenant metric entries to both local and K8s tenant configs
- Confirmed zero OID map collisions — all 3 aggregate names are synthetic-only identifiers
- 326 tests still passing, build clean

## Task Commits

Each task was committed atomically:

1. **Task 1: Add aggregate poll groups to devices.json (all 3 locations)** - `181cb0d` (feat)
2. **Task 2: Register aggregate metrics in tenant Metrics[] arrays (2 locations)** - `bc35f6a` (feat)

## Files Created/Modified

- `src/SnmpCollector/config/devices.json` - 3 new aggregate poll groups (local dev, 127.0.0.1 ports 10161/10162)
- `src/SnmpCollector/config/tenants.json` - 3 aggregate metric entries across Priority 1 (OBP) and Priority 2 (NPB) tenants
- `deploy/k8s/snmp-collector/simetra-devices.yaml` - same 3 aggregate poll groups (K8s DNS names, port 161)
- `deploy/k8s/snmp-collector/simetra-tenants.yaml` - 3 aggregate metric entries across Priority 1 (NPB) and Priority 3 (OBP) tenants
- `deploy/k8s/production/configmap.yaml` - same 3 aggregate poll groups in simetra-devices section

## Decisions Made

- Aggregate poll groups added as separate `Polls[]` entries, not as fields on existing groups. Each poll group supports exactly one `AggregatedMetricName`, so 3 new groups are the correct approach.
- Aggregate metric names (`npb_total_rx_octets`, `npb_total_tx_octets`, `obp_mean_power_L1`) are synthetic identifiers — they must NOT appear in `oid_metric_map.json`. OID resolution bypass guard (`Source == SnmpSource.Synthetic`) handles this at pipeline time.
- Production configmap.yaml was updated with identical aggregate groups but the simetra-tenants section in that file was not edited (the plan's Task 2 specified only the standalone `simetra-tenants.yaml`, not the production configmap tenants section).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 3 aggregate metrics will be computed and exported on next deploy/restart
- Grafana dashboard can now add panels for `snmp_gauge{metric_name="npb_total_rx_octets"}`, `snmp_gauge{metric_name="npb_total_tx_octets"}`, `snmp_gauge{metric_name="obp_mean_power_L1"}`
- No blockers

---
*Phase: quick/057-demo-aggregate-metrics-in-devices*
*Completed: 2026-03-15*
