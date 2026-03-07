---
phase: 11-oid-map-design-and-obp-population
plan: 01
subsystem: config
tags: [snmp, oid-map, obp, optical-bypass, k8s-configmap, jsonc]

# Dependency graph
requires:
  - phase: 04-counter-delta-engine
    provides: OidMap-based metric name resolution in pipeline
provides:
  - OBP device OID-to-metric-name mapping (24 entries, 4 links x 6 metrics)
  - K8s ConfigMap keys for OBP OID map in dev and production
affects: [11-02, 11-03, 12-simulator-configuration, 13-grafana-dashboards]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "JSONC OID map files with inline documentation (type, units, range)"
    - "Separate configmap key per device-type OID map (not inline in appsettings)"
    - "OBP naming convention: obp_{metric}_{L1-L4}"

key-files:
  created:
    - src/SnmpCollector/config/oidmap-obp.json
  modified:
    - deploy/k8s/configmap.yaml
    - deploy/k8s/production/configmap.yaml

key-decisions:
  - "OBP OIDs stored as separate configmap key (oidmap-obp.json), not merged into OidMap in appsettings"
  - "JSONC comments in source file document SNMP type, units, value meaning, and range for each OID"
  - "ConfigMap YAML uses plain JSON (no comments) since YAML literal blocks cannot contain JSONC"

patterns-established:
  - "OID map per device type: src/SnmpCollector/config/oidmap-{device}.json with JSONC docs"
  - "ConfigMap key naming: oidmap-{device}.json"

# Metrics
duration: 3min
completed: 2026-03-07
---

# Phase 11 Plan 01: OBP OID Map Summary

**24-entry OBP OID map covering 4 fiber links x 6 metrics (link_state, channel, r1-r4_power) with JSONC documentation and K8s ConfigMap deployment**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-07T12:34:14Z
- **Completed:** 2026-03-07T12:36:47Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Created oidmap-obp.json with 24 OID entries mapping enterprise OIDs to metric names
- JSONC comments document SNMP type, units, value meaning, and expected range for each OID
- Both dev and production K8s ConfigMaps updated with oidmap-obp.json key
- Existing appsettings.k8s.json OidMap: {} preserved unchanged

## Task Commits

Each task was committed atomically:

1. **Task 1: Create oidmap-obp.json with JSONC documentation** - `05f0ed1` (feat)
2. **Task 2: Update K8s ConfigMaps with oidmap-obp.json key** - `1502cc7` (feat)

**Plan metadata:** [pending] (docs: complete plan)

## Files Created/Modified
- `src/SnmpCollector/config/oidmap-obp.json` - OBP OID-to-metric-name map with JSONC documentation
- `deploy/k8s/configmap.yaml` - Added oidmap-obp.json key with 24 OBP OID entries
- `deploy/k8s/production/configmap.yaml` - Added oidmap-obp.json key with 24 OBP OID entries

## Decisions Made
- OBP OIDs kept as separate configmap key rather than merged into appsettings OidMap -- follows the plan's design for per-device-type OID files
- JSONC source file serves as the "documented truth" while ConfigMap YAML gets plain JSON (YAML literal blocks cannot contain JSONC comments)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- OBP OID map ready for consumption by OID map loader (plan 11-02)
- ConfigMap keys ready for K8s volume mount configuration
- Pattern established for future device-type OID maps

---
*Phase: 11-oid-map-design-and-obp-population*
*Completed: 2026-03-07*
