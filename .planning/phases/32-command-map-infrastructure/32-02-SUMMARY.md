---
phase: 32-command-map-infrastructure
plan: 02
subsystem: infra
tags: [kubernetes, configmap, yaml, command-map, snmp-set]

# Dependency graph
requires:
  - phase: 32-01
    provides: CommandMap domain model (CommandEntry, ICommandMapService) that references simetra-commandmaps by name
provides:
  - K8s ConfigMap manifest simetra-commandmaps.yaml (standalone, snmp-collector deploy dir)
  - simetra-commandmaps section appended to production multi-document configmap.yaml (5th document)
  - 12 seed entries: OBP bypass SET OIDs (L1-L4) and NPB reset-counters SET OIDs (P1-P8)
affects:
  - 32-03 CommandMapWatcherService (watches simetra-commandmaps ConfigMap by name)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "simetra-commandmaps ConfigMap mirrors simetra-oidmaps naming convention"
    - "Production configmap.yaml is a multi-document YAML (5 documents separated by ---)"
    - "commandmaps.json data key contains array-of-objects [{Oid, CommandName}]"

key-files:
  created:
    - deploy/k8s/snmp-collector/simetra-commandmaps.yaml
  modified:
    - deploy/k8s/production/configmap.yaml

key-decisions:
  - "Standalone simetra-commandmaps.yaml mirrors simetra-oidmaps.yaml structure exactly"
  - "Production configmap.yaml extended as 5th document; existing sections untouched"
  - "Data key is commandmaps.json; JSON field is CommandName (not MetricName)"

patterns-established:
  - "ConfigMap data key naming: {resourcetype}.json (commandmaps.json, oidmaps.json, devices.json)"

# Metrics
duration: 1min
completed: 2026-03-13
---

# Phase 32 Plan 02: Command Map ConfigMap Manifests Summary

**simetra-commandmaps K8s ConfigMap with 12 SET OID entries (4 OBP bypass + 8 NPB reset-counters), deployed as standalone YAML and as the 5th document in production configmap.yaml**

## Performance

- **Duration:** 1 min
- **Started:** 2026-03-13T10:03:18Z
- **Completed:** 2026-03-13T10:04:37Z
- **Tasks:** 1/1
- **Files modified:** 2

## Accomplishments

- Created `deploy/k8s/snmp-collector/simetra-commandmaps.yaml` standalone ConfigMap manifest with 12 seed command map entries
- Appended `simetra-commandmaps` as the 5th document in `deploy/k8s/production/configmap.yaml` without touching any existing sections
- All 12 entries use correct OIDs and `CommandName` field name matching the Phase 32-01 domain model

## Task Commits

Each task was committed atomically:

1. **Task 1: Create simetra-commandmaps.yaml and add section to production configmap.yaml** - `6ba5a72` (feat)

**Plan metadata:** (see docs commit below)

## Files Created/Modified

- `deploy/k8s/snmp-collector/simetra-commandmaps.yaml` - Standalone ConfigMap manifest; mirrors simetra-oidmaps.yaml structure with 12 SET OID entries
- `deploy/k8s/production/configmap.yaml` - Production multi-document ConfigMap extended with simetra-commandmaps as 5th document (4 `---` separators total)

## Decisions Made

- Data key is `commandmaps.json` (not `oidmaps.json`) and JSON field is `CommandName` (not `MetricName`) — distinct from oidmap convention to reflect write-path semantics
- Production configmap.yaml append-only strategy: existing snmp-collector-config, simetra-oidmaps, simetra-devices, simetra-tenantvector sections unchanged

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- simetra-commandmaps ConfigMap manifests are ready for `kubectl apply`
- CommandMapWatcherService (Plan 03) can reference ConfigMap name `simetra-commandmaps` as a constant
- No blockers for Phase 32-03

---
*Phase: 32-command-map-infrastructure*
*Completed: 2026-03-13*
