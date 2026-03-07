---
phase: 15-k8s-configmap-watch-and-unified-config
plan: 04
subsystem: infra
tags: [kubernetes, configmap, rbac, yaml, oid-map]

# Dependency graph
requires:
  - phase: 15-01
    provides: ConfigMapWatcherService that watches simetra-config ConfigMap
provides:
  - Unified simetra-config.json ConfigMap key with 92 OIDs and 2 devices
  - RBAC configmaps get/list/watch permissions for simetra-sa
  - JSONC-documented device configuration as single source of truth
affects: [15-05, deployment, kubectl-apply]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Single ConfigMap key (simetra-config.json) for all device config"
    - "RBAC Role covers both leases and configmaps"

key-files:
  created: []
  modified:
    - deploy/k8s/configmap.yaml
    - deploy/k8s/rbac.yaml
    - deploy/k8s/production/rbac.yaml

key-decisions:
  - "Renamed Role from simetra-lease-role to simetra-role to reflect broader scope"
  - "Renamed RoleBinding from simetra-lease-binding to simetra-binding"
  - "JSONC comments inline with OID entries for documentation"

patterns-established:
  - "Single unified JSON key in ConfigMap: simetra-config.json contains OidMap + Devices"
  - "RBAC Role named simetra-role covers all non-default API permissions"

# Metrics
duration: 3min
completed: 2026-03-07
---

# Phase 15 Plan 04: K8s Manifests Summary

**Unified simetra-config.json ConfigMap key with 92 JSONC-documented OIDs, RBAC configmaps watch permissions, and legacy oidmap file cleanup**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-07T20:47:48Z
- **Completed:** 2026-03-07T20:51:12Z
- **Tasks:** 2
- **Files modified:** 3 (+ 2 deleted)

## Accomplishments
- Consolidated three separate ConfigMap keys (oidmap-obp.json, oidmap-npb.json, devices.json) into single simetra-config.json key
- All 92 OIDs (24 OBP + 68 NPB) with JSONC inline comments documenting types, units, and value ranges
- RBAC Role extended with configmaps get/list/watch for ConfigMapWatcherService
- Removed empty OidMap from appsettings.k8s.json (OID maps now in unified key)
- Legacy oidmap-obp.json and oidmap-npb.json source files deleted

## Task Commits

Each task was committed atomically:

1. **Task 1: Replace ConfigMap with unified simetra-config.json key** - `1d827ce` (feat)
2. **Task 2: Update RBAC for ConfigMap watch permissions and delete legacy config files** - `758b6af` (feat)

## Files Created/Modified
- `deploy/k8s/configmap.yaml` - Unified ConfigMap with simetra-config.json key (92 OIDs, 2 devices, JSONC docs)
- `deploy/k8s/rbac.yaml` - RBAC Role with leases + configmaps rules
- `deploy/k8s/production/rbac.yaml` - Production RBAC matching base
- `src/SnmpCollector/config/oidmap-obp.json` - DELETED (replaced by ConfigMap)
- `src/SnmpCollector/config/oidmap-npb.json` - DELETED (replaced by ConfigMap)

## Decisions Made
- Renamed Role from `simetra-lease-role` to `simetra-role` since it now covers leases and configmaps
- Renamed RoleBinding from `simetra-lease-binding` to `simetra-binding` for consistency
- JSONC comments include OID tree structure documentation, type info, and value ranges

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- ConfigMap manifest ready for `kubectl apply`
- RBAC permits ConfigMapWatcherService to watch configmaps
- simetra-config.json key matches the ConfigKey constant expected by ConfigMapWatcherService
- All legacy config files removed from source tree

---
*Phase: 15-k8s-configmap-watch-and-unified-config*
*Completed: 2026-03-07*
