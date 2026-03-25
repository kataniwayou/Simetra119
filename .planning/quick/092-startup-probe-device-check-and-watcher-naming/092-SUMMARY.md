---
phase: quick-092
plan: 01
subsystem: startup
tags: [health-check, startup-probe, dns, device-registry, logging]
dependency_graph:
  requires: []
  provides:
    - "Startup probe gates on device count > 0"
    - "DNS failure during InitialLoadAsync crashes pod for K8s restart"
    - "Consistent *Watcher naming in startup log"
  affects: []
tech_stack:
  added: []
  patterns:
    - "isInitialLoad parameter for crash-the-pod semantics on initial load exceptions"
file_tracking:
  key_files:
    modified:
      - src/SnmpCollector/Services/DeviceWatcherService.cs
      - src/SnmpCollector/HealthChecks/StartupHealthCheck.cs
      - src/SnmpCollector/Program.cs
decisions: []
metrics:
  completed: "2026-03-25"
  duration: "~5 min"
---

# Quick 092: Startup Probe Device Check and Watcher Naming Summary

Startup probe now gates on DeviceRegistry.Count > 0 in addition to correlation job registration; DNS exceptions propagate during InitialLoadAsync to crash-restart the pod; startup log uses consistent *Watcher naming.

## What Changed

### Task 1: Propagate DNS exceptions and add device count to startup probe

**DeviceWatcherService.cs:**
- Added `bool isInitialLoad = false` parameter to `HandleConfigMapChangedAsync`
- In the catch block, after logging the error, added `if (isInitialLoad) throw;` to re-throw exceptions during initial load
- `LoadFromConfigMapAsync` passes `isInitialLoad: true` when calling `HandleConfigMapChangedAsync`
- Result: DNS SocketException during pod startup now propagates up and crashes the pod, letting K8s restart it

**StartupHealthCheck.cs:**
- Injected `IDeviceRegistry` as second constructor dependency
- Added `hasDevices = _devices.AllDevices.Count > 0` check
- Added `devicesLoaded` to health check data dictionary
- Healthy condition now requires `hasJobs && hasDevices`
- Unhealthy message differentiates which condition(s) failed using pattern match

### Task 2: Normalize startup log to use consistent *Watcher naming

**Program.cs:**
- Updated log template: `OidMap` -> `OidMapWatcher`, `Devices` -> `DeviceWatcher`, `CommandMap` -> `CommandMapWatcher`, `Tenants` -> `TenantWatcher`
- Updated comment on line 60 to match
- No variable or parameter renames -- template labels only

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | ff3c9aa | fix(quick-092): propagate DNS exceptions on initial load and gate startup probe on device count |
| 2 | 96faff7 | style(quick-092): normalize startup log to use consistent *Watcher naming |

## Verification

- Build: 0 errors, 0 warnings
- Tests: 479 passed, 0 failed, 0 skipped
- Grep confirmed: `IDeviceRegistry` injected in StartupHealthCheck, `isInitialLoad` + `throw` in DeviceWatcherService, `OidMapWatcher` in Program.cs startup log

## Deviations from Plan

None -- plan executed exactly as written.
