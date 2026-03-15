---
phase: 36-config-file-renames
plan: 02
subsystem: infra
tags: [k8s, configmap, oid-metric-map, oid-command-map, rename, configuration]

# Dependency graph
requires:
  - phase: 36-01
    provides: tenantvector->tenants rename as prior step in same phase

provides:
  - OidMapWatcherService.ConfigMapName = "simetra-oid-metric-map"
  - OidMapWatcherService.ConfigKey = "oid_metric_map.json"
  - CommandMapWatcherService.ConfigMapName = "simetra-oid-command-map"
  - CommandMapWatcherService.ConfigKey = "oid_command_map.json"
  - config/oid_metric_map.json and config/oid_command_map.json as local dev files
  - simetra-oid-metric-map.yaml and simetra-oid-command-map.yaml K8s ConfigMap manifests
  - Both deployment.yamls reference simetra-oid-metric-map volume projection
  - production/configmap.yaml has correct names and keys for both
  - E2E kubectl.sh + fixtures fully updated to new oidmap names
  - All 286 tests pass

affects:
  - any future phases referencing OID map or command map ConfigMap names
  - K8s deployment operators applying manifests
  - E2E tests that apply/restore oid-metric-map ConfigMap

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Mechanical rename pattern: git mv for file history, grep verification for zero old refs"
    - "Config naming convention complete: simetra-{noun} ConfigMap, {noun}.json key"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Services/OidMapWatcherService.cs
    - src/SnmpCollector/Services/CommandMapWatcherService.cs
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/config/oid_metric_map.json (renamed from oidmaps.json)
    - src/SnmpCollector/config/oid_command_map.json (renamed from commandmaps.json)
    - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
    - deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml (renamed from simetra-oidmaps.yaml)
    - deploy/k8s/snmp-collector/simetra-oid-command-map.yaml (renamed from simetra-commandmaps.yaml)
    - deploy/k8s/snmp-collector/deployment.yaml
    - deploy/k8s/snmp-collector/DEPLOY.md
    - deploy/k8s/production/deployment.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/e2e/lib/kubectl.sh
    - tests/e2e/fixtures/oid-renamed-configmap.yaml
    - tests/e2e/fixtures/oid-removed-configmap.yaml
    - tests/e2e/fixtures/oid-added-configmap.yaml
    - tests/e2e/fixtures/invalid-json-oidmaps-syntax-configmap.yaml
    - tests/e2e/fixtures/invalid-json-oidmaps-schema-configmap.yaml

key-decisions:
  - "Fixture filenames (invalid-json-oidmaps-*.yaml) not renamed — they describe test scenario, not config name"
  - ".original-oidmaps-configmap.yaml not git-tracked (runtime-generated snapshot); only script references updated"
  - "commandmaps not in volume projections (watched via K8s API only) — deployment.yamls unchanged for commandmaps"

patterns-established:
  - "Config naming convention: simetra-{noun} ConfigMap, {noun}.json key, '{Noun}' IConfiguration section"

# Metrics
duration: 5min
completed: 2026-03-15
---

# Phase 36 Plan 02: Rename oidmaps -> oid_metric_map and commandmaps -> oid_command_map Summary

**Mechanical rename of all oidmaps.json/simetra-oidmaps and commandmaps.json/simetra-commandmaps references to oid_metric_map.json/simetra-oid-metric-map and oid_command_map.json/simetra-oid-command-map across C# constants, local dev config, K8s manifests, and E2E fixtures**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-15T04:33:21Z
- **Completed:** 2026-03-15T04:38:31Z
- **Tasks:** 2
- **Files modified:** 22 (16 content updates + 4 git-mv renames + 2 git-mv renames)

## Accomplishments
- All config-level references to `simetra-oidmaps` and `oidmaps.json` eliminated from all tracked source/config/manifest/script files
- All config-level references to `simetra-commandmaps` and `commandmaps.json` eliminated from all tracked files
- K8s ConfigMap names, data keys, volume projections, DEPLOY.md, and E2E fixtures consistently reference new names
- All 286 tests pass after rename; builds succeed with zero errors

## Task Commits

Each task was committed atomically:

1. **Task 1: oidmaps -> oid_metric_map (C#, config, K8s, E2E)** - `13a7539` (refactor)
2. **Task 2: commandmaps -> oid_command_map (C#, config, K8s)** - `1ba884d` (refactor)

**Plan metadata:** (docs commit below)

## Files Created/Modified
- `src/SnmpCollector/Services/OidMapWatcherService.cs` - ConfigMapName + ConfigKey + doc comment updated
- `src/SnmpCollector/Services/CommandMapWatcherService.cs` - ConfigMapName + ConfigKey + doc comment updated
- `src/SnmpCollector/Program.cs` - oidMetricMapPath + oidCommandMapPath vars; file paths updated
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` - two comments updated
- `src/SnmpCollector/config/oid_metric_map.json` - renamed from oidmaps.json; internal comment updated
- `src/SnmpCollector/config/oid_command_map.json` - renamed from commandmaps.json; internal comments updated
- `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs` - path + comments updated
- `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` - renamed from simetra-oidmaps.yaml; name + key updated
- `deploy/k8s/snmp-collector/simetra-oid-command-map.yaml` - renamed from simetra-commandmaps.yaml; name + key updated
- `deploy/k8s/snmp-collector/deployment.yaml` - volume projection simetra-oid-metric-map
- `deploy/k8s/snmp-collector/DEPLOY.md` - kubectl apply reference updated
- `deploy/k8s/production/deployment.yaml` - volume projection simetra-oid-metric-map
- `deploy/k8s/production/configmap.yaml` - both ConfigMap names + keys + comment updated
- `tests/e2e/lib/kubectl.sh` - snapshot/restore uses simetra-oid-metric-map + .original-oid-metric-map-configmap.yaml
- `tests/e2e/fixtures/oid-renamed-configmap.yaml` - name + key updated
- `tests/e2e/fixtures/oid-removed-configmap.yaml` - name + key updated
- `tests/e2e/fixtures/oid-added-configmap.yaml` - name + key updated
- `tests/e2e/fixtures/invalid-json-oidmaps-syntax-configmap.yaml` - name + key updated
- `tests/e2e/fixtures/invalid-json-oidmaps-schema-configmap.yaml` - name + key updated

## Decisions Made
- E2E fixture filenames `invalid-json-oidmaps-syntax-configmap.yaml` and `invalid-json-oidmaps-schema-configmap.yaml` were NOT renamed — they describe the test scenario (testing invalid oidmaps JSON), not the ConfigMap name. The internal ConfigMap name and key inside were updated.
- `.original-oidmaps-configmap.yaml` is a runtime-generated snapshot not tracked in git — only script references in kubectl.sh were updated. Same pattern as 36-01 used for `.original-tenantvector-configmap.yaml`.
- `commandmaps` has no volume projection in either deployment.yaml (watched via K8s API only). Confirmed unchanged.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- `.original-oidmaps-configmap.yaml` fixture is runtime-generated, not git-tracked — `git mv` returned fatal error. Handled identically to 36-01: only script references updated, file will be regenerated with new name by E2E tests.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 36 complete (both plans done)
- v1.7 config naming convention fully aligned: simetra-tenants/tenants.json, simetra-oid-metric-map/oid_metric_map.json, simetra-oid-command-map/oid_command_map.json
- K8s operators must apply updated manifests: rename/recreate ConfigMaps as simetra-oid-metric-map and simetra-oid-command-map
- Any existing K8s cluster with old ConfigMap names must have them recreated

---
*Phase: 36-config-file-renames*
*Completed: 2026-03-15*
