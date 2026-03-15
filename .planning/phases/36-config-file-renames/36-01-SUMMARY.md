---
phase: 36-config-file-renames
plan: 01
subsystem: infra
tags: [k8s, configmap, tenant-vector, rename, configuration]

# Dependency graph
requires:
  - phase: 35-tenantvector-registry-refactor
    provides: TenantVectorWatcherService + TenantVectorRegistry + TenantVectorOptions as stable base for rename

provides:
  - TenantVectorWatcherService.ConfigMapName = "simetra-tenants"
  - TenantVectorWatcherService.ConfigKey = "tenants.json"
  - TenantVectorOptions.SectionName = "Tenants"
  - config/tenants.json with root key "Tenants"
  - simetra-tenants.yaml K8s ConfigMap manifest
  - Both deployment.yamls reference simetra-tenants volume projection
  - production/configmap.yaml has name: simetra-tenants and key tenants.json
  - E2E kubectl.sh + scenario 28 fully updated to new names

affects:
  - 36-02 (second rename plan in phase)
  - any future phases referencing tenant ConfigMap names
  - K8s deployment operators applying manifests

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Mechanical rename pattern: git mv for file history, grep verification for zero old refs"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Services/TenantVectorWatcherService.cs
    - src/SnmpCollector/Configuration/TenantVectorOptions.cs
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/config/tenants.json (renamed from tenantvector.json)
    - deploy/k8s/snmp-collector/simetra-tenants.yaml (renamed from simetra-tenantvector.yaml)
    - deploy/k8s/snmp-collector/deployment.yaml
    - deploy/k8s/production/deployment.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/e2e/lib/kubectl.sh
    - tests/e2e/scenarios/28-tenantvector-routing.sh

key-decisions:
  - "TenantVectorOptions.SectionName renamed from 'TenantVector' to 'Tenants' (v1.7 pre-phase decision resolved)"
  - "Class/type names (TenantVectorOptions, TenantVectorWatcherService) retained unchanged — only config/file/K8s references renamed"
  - ".original-tenantvector-configmap.yaml fixture not tracked in git; only script references updated"

patterns-established:
  - "Config naming convention: simetra-{noun} ConfigMap, {noun}.json key, '{Noun}' IConfiguration section"

# Metrics
duration: 3min
completed: 2026-03-15
---

# Phase 36 Plan 01: Rename tenantvector -> tenants Summary

**Mechanical rename of all tenantvector.json/simetra-tenantvector references to tenants.json/simetra-tenants across C# constants, IConfiguration section name, local dev config, K8s manifests, and E2E scripts**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-15T04:27:34Z
- **Completed:** 2026-03-15T04:30:55Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments
- All config-level references to `simetra-tenantvector` and `tenantvector.json` eliminated from the entire codebase
- TenantVectorOptions.SectionName changed from `"TenantVector"` to `"Tenants"` — resolves the v1.7 pre-phase decision
- K8s ConfigMap name, data key, volume projections, and E2E fixtures all consistently reference `simetra-tenants` / `tenants.json`
- All 286 tests pass after rename; builds succeed with zero warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: C# constants + SectionName + Program.cs** - `b70ee4a` (refactor)
2. **Task 2: Config file + K8s manifests + deployment projections** - `5a69781` (refactor)
3. **Task 3: E2E fixture references + kubectl.sh + scenario 28** - `be06a87` (refactor)

**Plan metadata:** (docs commit below)

## Files Created/Modified
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` - ConfigMapName + ConfigKey + doc comment updated
- `src/SnmpCollector/Configuration/TenantVectorOptions.cs` - SectionName "TenantVector" -> "Tenants"
- `src/SnmpCollector/Program.cs` - tenantsConfig/tenantsPath vars; TryGetProperty("Tenants"); file path "tenants.json"
- `src/SnmpCollector/config/tenants.json` - renamed from tenantvector.json; root key "Tenants"
- `deploy/k8s/snmp-collector/simetra-tenants.yaml` - renamed from simetra-tenantvector.yaml; name + key updated
- `deploy/k8s/snmp-collector/deployment.yaml` - volume projection simetra-tenants
- `deploy/k8s/production/deployment.yaml` - volume projection simetra-tenants
- `deploy/k8s/production/configmap.yaml` - name simetra-tenants; key tenants.json; comment updated
- `tests/e2e/lib/kubectl.sh` - snapshot/restore uses simetra-tenants + .original-tenants-configmap.yaml
- `tests/e2e/scenarios/28-tenantvector-routing.sh` - all old refs replaced with new names

## Decisions Made
- TenantVectorOptions.SectionName renamed from `"TenantVector"` to `"Tenants"` — this was the named decision required in Phase 36 (v1.7 pre-phase decision). Chosen for consistency with the file naming convention.
- Class and type names (`TenantVectorOptions`, `TenantVectorWatcherService`, `TenantVectorRegistry`) were NOT renamed — only config/file/K8s string references changed.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- `.original-tenantvector-configmap.yaml` is a runtime-generated fixture, not tracked in git — no `git mv` needed; only script references updated.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 36-02 (if it covers additional renames) can proceed immediately
- K8s operators must apply updated manifests: `simetra-tenants.yaml` is the new ConfigMap name
- Any existing K8s cluster with `simetra-tenantvector` ConfigMap must have it renamed/recreated as `simetra-tenants`

---
*Phase: 36-config-file-renames*
*Completed: 2026-03-15*
