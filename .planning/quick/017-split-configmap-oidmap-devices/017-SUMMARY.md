---
phase: quick-017
plan: 01
subsystem: config-reload
tags: [k8s, configmap, watcher, separation-of-concerns]
dependency-graph:
  requires: [phase-15]
  provides: [independent-oidmap-watcher, independent-device-watcher, split-configmaps]
  affects: []
tech-stack:
  added: []
  patterns: [independent-configmap-watchers, projected-volume-mounts, bare-json-config-files]
key-files:
  created:
    - src/SnmpCollector/Services/OidMapWatcherService.cs
    - src/SnmpCollector/Services/DeviceWatcherService.cs
    - src/SnmpCollector/config/oidmaps.json
    - src/SnmpCollector/config/devices.json
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/Services/DynamicPollScheduler.cs
    - deploy/k8s/configmap.yaml
    - deploy/k8s/production/configmap.yaml
    - deploy/k8s/deployment.yaml
    - deploy/k8s/production/deployment.yaml
    - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
  deleted:
    - src/SnmpCollector/Services/ConfigMapWatcherService.cs
    - src/SnmpCollector/Configuration/SimetraConfigModel.cs
    - src/SnmpCollector/config/simetra-config.json
decisions:
  - id: Q017-D1
    choice: "Bare JSON format for config files (dict for oidmaps, array for devices)"
    reason: "Simpler deserialization -- no wrapper POCO needed, each watcher deserializes its own type directly"
  - id: Q017-D2
    choice: "Projected volume combining three ConfigMaps into /app/config"
    reason: "K8s configMap volumes mount all keys as files; projected volume merges multiple ConfigMaps into single mountPath"
metrics:
  duration: ~4 minutes
  completed: 2026-03-08
---

# Quick Task 017: Split ConfigMap Watcher Summary

Split unified ConfigMapWatcherService into independent OidMapWatcherService and DeviceWatcherService, eliminating cascading reloads when only one concern changes.

## What Changed

### Task 1: Split watcher services and local dev config
- Created `OidMapWatcherService` watching `simetra-oidmaps` ConfigMap, calling only `IOidMapService.UpdateMap`
- Created `DeviceWatcherService` watching `simetra-devices` ConfigMap, calling only `IDeviceRegistry.ReloadAsync` + `DynamicPollScheduler.ReconcileAsync`
- Deleted `ConfigMapWatcherService` (replaced by two independent watchers)
- Deleted `SimetraConfigModel` POCO (no longer needed -- each watcher deserializes its own type)
- Split `simetra-config.json` into `oidmaps.json` (bare dictionary) and `devices.json` (bare array)
- Updated `Program.cs` local dev block to load two separate files
- Updated `ServiceCollectionExtensions.cs` to register both new watchers

### Task 2: K8s manifests and tests
- Split `configmap.yaml` into three ConfigMap objects: `simetra-config` (appsettings only), `simetra-oidmaps`, `simetra-devices`
- Production configmap: merged `oidmap-obp.json` + `oidmap-npb.json` into single `oidmaps.json` key, removed `OidMap`/`Devices` from `appsettings.k8s.json`
- Updated both deployment yamls to use `projected` volume combining three ConfigMaps
- Updated `OidMapAutoScanTests` to read from `oidmaps.json` directly (bare dict deserialization)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing using directive in OidMapWatcherService**
- **Found during:** Task 1 build verification
- **Issue:** `IOidMapService` is in `SnmpCollector.Pipeline` namespace, but OidMapWatcherService only had `SnmpCollector.Services` namespace imports
- **Fix:** Added `using SnmpCollector.Pipeline;` to OidMapWatcherService.cs
- **Commit:** 7e9ad7f

**2. [Rule 1 - Bug] Stale doc comment in DynamicPollScheduler**
- **Found during:** Task 1
- **Issue:** XML doc referenced deleted `ConfigMapWatcherService`
- **Fix:** Updated to reference `DeviceWatcherService`
- **Commit:** 7e9ad7f

## Verification Results

- Build: 0 errors, 2 pre-existing warnings (K8s WatchAsync deprecation)
- Tests: 136/136 passed
- No references to `SimetraConfigModel`, `ConfigMapWatcherService`, or `simetra-config.json` in active source
- `OidMapWatcherService` has zero references to `IDeviceRegistry` or `DynamicPollScheduler` (only doc comment describing what it does NOT do)
- `DeviceWatcherService` has zero references to `IOidMapService`
- K8s configmap.yaml has three `---`-separated ConfigMap objects
- Deployment yamls use `projected` volumes with three ConfigMap sources
