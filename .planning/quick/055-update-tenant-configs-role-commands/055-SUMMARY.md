---
phase: quick-055
plan: 055
subsystem: config
tags: [tenant-config, role, commands, TEN-12, TEN-13, k8s, json, yaml]

# Dependency graph
requires:
  - phase: quick-053
    provides: MetricSlotOptions.Role field and TEN-12/TEN-13 validation rules in TenantVectorWatcherService
  - phase: 32-command-map-infrastructure
    provides: CommandSlotOptions shape (Ip, Port, CommandName, Value, ValueType)
provides:
  - Role field on every metric entry in all four tenant config locations
  - Commands array on every tenant in all four tenant config locations
  - TEN-12 (Role required) and TEN-13 (both Roles + commands per tenant) satisfied in all configs
affects: [e2e scenario 28, local dev, k8s deployment, production deployment]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Tenant configs: every metric has Role (Evaluate or Resolved); every tenant has Commands array"
    - "Status/state metrics: Role=Resolved (obp_link_state_*, obp_channel_*, npb_port_status_*)"
    - "Performance/traffic metrics: Role=Evaluate (npb_cpu_util, npb_mem_util, npb_port_rx/tx_*, obp_r*_power_*)"

key-files:
  created: []
  modified:
    - src/SnmpCollector/config/tenants.json
    - deploy/k8s/snmp-collector/simetra-tenants.yaml
    - deploy/k8s/production/configmap.yaml
    - tests/e2e/scenarios/28-tenantvector-routing.sh

key-decisions:
  - "Local dev P1 tenant expanded: added obp_r1_power_L1 (Evaluate) to satisfy TEN-13 both-role requirement"
  - "Local dev P2 tenant expanded: added npb_port_status_P1 (Resolved) to satisfy TEN-13 both-role requirement"
  - "K8s/production P2 tenant expanded: added npb_port_status_P2 (Resolved) — all-Evaluate tenant needs one Resolved"
  - "E2E scenario 28 hot-reload P4 tenant expanded: added obp_link_state_L1 (Resolved) for TEN-13 compliance"

patterns-established:
  - "Role assignment: obp_channel_*, obp_link_state_*, npb_port_status_* are Resolved; all performance/traffic metrics are Evaluate"
  - "Command names chosen by device type: obp tenants use obp_set_bypass_LN; npb tenants use npb_reset_counters_PN"

# Metrics
duration: 10min
completed: 2026-03-15
---

# Quick 055: Update Tenant Configs — Role and Commands Summary

**Role field (Evaluate/Resolved) added to every metric and Commands array added to every tenant across all four tenant config files, satisfying TEN-12 and TEN-13 validation rules.**

## Performance

- **Duration:** ~10 min
- **Started:** 2026-03-15T04:45Z
- **Completed:** 2026-03-15T04:55Z
- **Tasks:** 1
- **Files modified:** 4

## Accomplishments

- All metric entries across four config locations now carry `"Role": "Evaluate"` or `"Role": "Resolved"`
- All tenants now have a `"Commands"` array with at least one entry using valid CommandName values from oid_command_map.json
- TEN-13 gate satisfied in all locations: every tenant has at least one Resolved and at least one Evaluate metric
- 286 unit tests continue to pass; build clean

## Task Commits

1. **Task 1: Add Role and Commands to all tenant config files** - `5660176` (feat)

## Files Created/Modified

- `src/SnmpCollector/config/tenants.json` - Added Role to 4 metrics, Commands to 2 tenants; added obp_r1_power_L1 (P1) and npb_port_status_P1 (P2) for TEN-13 compliance
- `deploy/k8s/snmp-collector/simetra-tenants.yaml` - Added Role to 13 metrics, Commands to 3 tenants; added npb_port_status_P2 (Resolved) to P2 for TEN-13 compliance
- `deploy/k8s/production/configmap.yaml` - Same changes as simetra-tenants.yaml in the simetra-tenants section only
- `tests/e2e/scenarios/28-tenantvector-routing.sh` - Updated hot-reload heredoc: 16 Role fields, 4 Commands blocks; added obp_link_state_L1 (Resolved) to P4 tenant for TEN-13 compliance

## Decisions Made

- Local dev Tenant P1 (obp) originally had one metric (obp_link_state_L1 = Resolved only). Added `obp_r1_power_L1` with `Role: Evaluate` to satisfy TEN-13.
- Local dev Tenant P2 (npb) originally had one metric (npb_cpu_util = Evaluate only). Added `npb_port_status_P1` with `Role: Resolved` to satisfy TEN-13.
- K8s/production Tenant P2 originally had four all-Evaluate metrics. Added `npb_port_status_P2` with `Role: Resolved` to satisfy TEN-13.
- E2E hot-reload Tenant P4 originally had two Evaluate-only metrics (obp_r3/r4_power_L1). Added `obp_link_state_L1` with `Role: Resolved` and command `obp_set_bypass_L3` for TEN-13.
- Production configmap.yaml: only the `simetra-tenants` section was modified; all other ConfigMaps (snmp-collector-config, simetra-oid-metric-map, simetra-devices, simetra-oid-command-map) left untouched.

## Deviations from Plan

None — plan executed exactly as written. All tenant additions and Role assignments were specified in the plan action block.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All tenant config files satisfy TEN-12 and TEN-13 validation — the TenantVectorWatcherService will accept them without dropping tenants on reload
- E2E scenario 28 hot-reload test remains valid: the `tenants=4` assertion checks tenant count, which is unchanged by adding metrics/commands to existing tenants

---
*Phase: quick-055*
*Completed: 2026-03-15*
