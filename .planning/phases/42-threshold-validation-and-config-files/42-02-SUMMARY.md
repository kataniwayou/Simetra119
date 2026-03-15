---
phase: 42-threshold-validation-and-config-files
plan: 02
subsystem: config
tags: [threshold, tenant-config, k8s, configmap, json, yaml]

# Dependency graph
requires:
  - phase: 42-01
    provides: Threshold Min > Max validation in ValidateAndBuildTenants (check 7)
  - phase: 41-threshold-model-and-holder-storage
    provides: ThresholdOptions class, MetricSlotOptions.Threshold, MetricSlotHolder.Threshold
provides:
  - Example Threshold entries in all three tenant config file locations (local dev, K8s standalone, production)
  - Operators can see real Threshold objects in every config template (satisfies THR-07)
affects:
  - runtime threshold evaluation (future phases)
  - operator config templates

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Threshold in double-wrapped local dev format: added inline after Role field"
    - "Threshold in single-wrapped K8s YAML literal block: appended to end of inline JSON object"

key-files:
  created: []
  modified:
    - src/SnmpCollector/config/tenants.json
    - deploy/k8s/snmp-collector/simetra-tenants.yaml
    - deploy/k8s/production/configmap.yaml

key-decisions:
  - "One Threshold per tenant (not all metrics) — keeps diff minimal and demonstrates the feature clearly"
  - "Optical power metric uses Min=-10.0/Max=3.0 (dBm range); utilization metrics use Min=0.0/Max=95.0 (percent range)"

patterns-established:
  - "Threshold placement: after Role field, inline on same JSON object line for K8s files"

# Metrics
duration: 2min
completed: 2026-03-15
---

# Phase 42 Plan 02: Threshold Config Examples Summary

**Threshold examples added to all three tenant config locations: local dev double-wrapped (2 entries), K8s single-wrapped (3 entries), production configmap (3 entries), satisfying THR-07**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-15T12:42:04Z
- **Completed:** 2026-03-15T12:43:50Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Added Threshold to `obp_r1_power_L1` (T1) and `npb_cpu_util` (T2) in local dev `tenants.json` (double-wrapped format)
- Added Threshold to `npb_cpu_util` (T1), `npb_mem_util` (T2), and `obp_r1_power_L1` (T3) in K8s `simetra-tenants.yaml` (single-wrapped YAML literal block)
- Added identical three Threshold entries in production `configmap.yaml` simetra-tenants section
- All JSON blocks validate correctly; dotnet build passes with zero warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Threshold examples to local dev tenants.json** - `7f1f7aa` (feat)
2. **Task 2: Add Threshold examples to K8s standalone simetra-tenants.yaml and production configmap.yaml** - `0475aa7` (feat)

**Plan metadata:** (to follow in final commit)

## Files Created/Modified
- `src/SnmpCollector/config/tenants.json` - 2 Threshold entries added (obp_r1_power_L1, npb_cpu_util)
- `deploy/k8s/snmp-collector/simetra-tenants.yaml` - 3 Threshold entries added (npb_cpu_util, npb_mem_util, obp_r1_power_L1)
- `deploy/k8s/production/configmap.yaml` - 3 Threshold entries added (npb_cpu_util, npb_mem_util, obp_r1_power_L1)

## Decisions Made
- One Threshold example per tenant rather than one per Evaluate metric — minimises diff noise while demonstrating the feature
- Optical power metric (`obp_r1_power_L1`): `Min=-10.0, Max=3.0` (valid dBm range for optical amplifiers)
- Utilization metrics (`npb_cpu_util`, `npb_mem_util`): `Min=0.0, Max=95.0` (percent range with 5% headroom)
- Threshold appended after existing fields (no field reordering) to preserve reviewability

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- THR-07 satisfied: all three config locations have valid Threshold examples
- Phase 42 complete: validation (42-01) + config examples (42-02) delivered
- v1.9 Metric Threshold Structure & Validation is done
- 332 tests pass; build clean

---
*Phase: 42-threshold-validation-and-config-files*
*Completed: 2026-03-15*
