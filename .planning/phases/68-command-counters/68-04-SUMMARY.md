---
phase: 68-command-counters
plan: "04"
subsystem: testing
tags: [e2e, snmp-set, command-failed, timeout, fake-unreachable, k8s-configmap]

# Dependency graph
requires:
  - phase: 68-command-counters
    provides: CCV-01/02/03 scenarios 83-84 covering command.dispatched and command.suppressed counters
provides:
  - CCV-04 scenario 85 covering command.failed counter via SET timeout to unreachable device
  - tenant-cfg10-ccv-timeout.yaml fixture with valid CommandName + unreachable command IP
affects:
  - E2E runner (phases 65+): scenario 85 is now self-contained with device registry setup and dual configmap restore

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Timeout-triggered command.failed: valid CommandName + unreachable IP causes CommandWorkerService OperationCanceledException, uses device.Name label (not IP:port)"
    - "Device registry pre-registration: add FAKE-UNREACHABLE to simetra-devices ConfigMap before applying tenant fixture so TryGetByIpPort succeeds at validation time"
    - "Dual ConfigMap save/restore: both simetra-devices and simetra-tenants saved before modification, restored in cleanup"

key-files:
  created:
    - tests/e2e/fixtures/tenant-cfg10-ccv-timeout.yaml
  modified:
    - tests/e2e/scenarios/85-ccv04-command-failed.sh
  deleted:
    - tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml

key-decisions:
  - "Use SET timeout (unreachable IP) not unmapped CommandName to trigger command.failed -- unmapped CommandName causes TEN-13 to skip the entire tenant at load time, never reaching runtime"
  - "device_name label for failed counter = FAKE-UNREACHABLE (device.Name from DeviceRegistry at CommandWorkerService line 159), not IP:port which is used only for OID-not-found and device-not-found paths"
  - "FAKE-UNREACHABLE device must be added to DeviceRegistry before tenant fixture is applied so TryGetByIpPort(10.255.255.254, 161) succeeds"

patterns-established:
  - "CCV-04 pattern: add device to registry -> apply tenant -> prime OIDs -> baseline counters -> violate OID -> poll dispatched -> wait for timeout -> assert both dispatched and failed"

# Metrics
duration: 2min
completed: 2026-03-22
---

# Phase 68 Plan 04: Command Counters CCV-04 Summary

**CCV-04 rewritten to trigger command.failed via 0.8s SET timeout to FAKE-UNREACHABLE (10.255.255.254), replacing unmapped CommandName approach that caused TEN-13 to skip the tenant at load time**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-22T17:13:47Z
- **Completed:** 2026-03-22T17:15:42Z
- **Tasks:** 2
- **Files modified:** 3 (1 created, 1 rewritten, 1 deleted)

## Accomplishments

- Created `tenant-cfg10-ccv-timeout.yaml` with valid CommandName `e2e_set_bypass` pointing to 10.255.255.254:161 (passes TenantVectorWatcher validation, times out at runtime)
- Deleted `tenant-cfg09-ccv-failed.yaml` (unmapped CommandName caused TEN-13 completeness gate to skip tenant entirely, making CCV-04B untestable)
- Rewrote scenario 85 to use timeout path: adds FAKE-UNREACHABLE to DeviceRegistry, applies tenant, asserts both dispatched (CCV-04A) and failed (CCV-04B) increment; restores both ConfigMaps in cleanup

## Task Commits

Each task was committed atomically:

1. **Task 1: Create tenant-cfg10-ccv-timeout.yaml, delete tenant-cfg09-ccv-failed.yaml** - `d916578` (feat)
2. **Task 2: Rewrite scenario 85 to use timeout path with FAKE-UNREACHABLE device** - `8ed58ab` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `tests/e2e/fixtures/tenant-cfg10-ccv-timeout.yaml` - New fixture: e2e-ccv-timeout tenant with e2e_set_bypass command targeting 10.255.255.254:161 (unreachable)
- `tests/e2e/scenarios/85-ccv04-command-failed.sh` - Rewritten scenario 85: timeout-based CCV-04 with dual ConfigMap save/restore and FAKE-UNREACHABLE device registration
- `tests/e2e/fixtures/tenant-cfg09-ccv-failed.yaml` - Deleted: unmapped CommandName approach does not exercise command.failed at runtime

## Decisions Made

- **Use timeout path not unmapped CommandName:** UAT revealed `e2e_set_unknown` causes `cleanCommands.Count == 0` in TenantVectorWatcher, triggering TEN-13 which skips the entire tenant before any command is ever dispatched. The only way to exercise the `IncrementCommandFailed(device.Name)` call at line 159 of CommandWorkerService is a valid command that times out at the SET layer.

- **device_name label is device.Name not IP:port:** The OID-not-found path (line 107) and device-not-found path (line 117) use `$"{req.Ip}:{req.Port}"` as the label. The timeout path (line 159) uses `device.Name`. For `CommunityString: "Simetra.FAKE-UNREACHABLE"`, `device.Name` = "FAKE-UNREACHABLE". Scenario uses `device_name="FAKE-UNREACHABLE"` for the failed counter.

- **Pre-register FAKE-UNREACHABLE in DeviceRegistry before applying tenant:** TenantVectorWatcher calls `TryGetByIpPort(10.255.255.254, 161)` when loading Commands. If the device is not in the registry the Commands entry is silently skipped and no SET is ever attempted. The scenario must add the device to simetra-devices ConfigMap and wait 15s for DeviceWatcher to reload before applying the tenant fixture.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- CCV-01 through CCV-04 are all covered by scenarios 83-85 (sub-assertions 83a, 83b, 84a, 84b, 85a, 85b)
- Phase 68 command counter scenarios are complete
- The timeout-based FAKE-UNREACHABLE pattern is reusable for future SET failure scenarios

---
*Phase: 68-command-counters*
*Completed: 2026-03-22*
