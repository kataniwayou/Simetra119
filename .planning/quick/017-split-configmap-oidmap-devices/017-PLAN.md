---
phase: quick-017
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Services/OidMapWatcherService.cs
  - src/SnmpCollector/Services/DeviceWatcherService.cs
  - src/SnmpCollector/Services/ConfigMapWatcherService.cs
  - src/SnmpCollector/Configuration/SimetraConfigModel.cs
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  - src/SnmpCollector/Program.cs
  - src/SnmpCollector/config/oidmaps.json
  - src/SnmpCollector/config/devices.json
  - src/SnmpCollector/config/simetra-config.json
  - deploy/k8s/configmap.yaml
  - deploy/k8s/production/configmap.yaml
  - deploy/k8s/deployment.yaml
  - deploy/k8s/production/deployment.yaml
  - tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
autonomous: true

must_haves:
  truths:
    - "OID map changes reload only OidMapService, not DeviceRegistry or DynamicPollScheduler"
    - "Device changes reload only DeviceRegistry + DynamicPollScheduler, not OidMapService"
    - "Local dev loads oidmaps.json and devices.json separately on startup"
    - "K8s watchers target two separate ConfigMap objects by name"
    - "Solution builds and all tests pass"
  artifacts:
    - path: "src/SnmpCollector/Services/OidMapWatcherService.cs"
      provides: "K8s watcher for simetra-oidmaps ConfigMap"
      contains: "class OidMapWatcherService"
    - path: "src/SnmpCollector/Services/DeviceWatcherService.cs"
      provides: "K8s watcher for simetra-devices ConfigMap"
      contains: "class DeviceWatcherService"
    - path: "src/SnmpCollector/config/oidmaps.json"
      provides: "Local dev OID map dictionary"
    - path: "src/SnmpCollector/config/devices.json"
      provides: "Local dev devices array"
  key_links:
    - from: "src/SnmpCollector/Services/OidMapWatcherService.cs"
      to: "OidMapService.UpdateMap"
      via: "K8s watch event handler"
      pattern: "_oidMapService\\.UpdateMap"
    - from: "src/SnmpCollector/Services/DeviceWatcherService.cs"
      to: "DeviceRegistry.ReloadAsync + DynamicPollScheduler.ReconcileAsync"
      via: "K8s watch event handler"
      pattern: "_deviceRegistry\\.ReloadAsync"
    - from: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      to: "Both watcher services"
      via: "DI registration in IsInCluster block"
      pattern: "OidMapWatcherService|DeviceWatcherService"
---

<objective>
Split the unified ConfigMapWatcherService into two independent watchers -- OidMapWatcherService (watches `simetra-oidmaps` ConfigMap, calls UpdateMap only) and DeviceWatcherService (watches `simetra-devices` ConfigMap, calls ReloadAsync + ReconcileAsync only). Split K8s ConfigMaps, deployment volumes, local dev config files, and update tests.

Purpose: OID map and device config are independent concerns with different reload chains. Splitting eliminates unnecessary cascading reloads when only one concern changes.
Output: Two independent watcher services, two K8s ConfigMaps, two local dev JSON files, updated deployment manifests, passing tests.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Services/ConfigMapWatcherService.cs
@src/SnmpCollector/Configuration/SimetraConfigModel.cs
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
@src/SnmpCollector/Program.cs
@src/SnmpCollector/config/simetra-config.json
@deploy/k8s/configmap.yaml
@deploy/k8s/production/configmap.yaml
@deploy/k8s/deployment.yaml
@deploy/k8s/production/deployment.yaml
@tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create split watcher services, update DI and Program.cs, create local dev config files</name>
  <files>
    src/SnmpCollector/Services/OidMapWatcherService.cs
    src/SnmpCollector/Services/DeviceWatcherService.cs
    src/SnmpCollector/Services/ConfigMapWatcherService.cs
    src/SnmpCollector/Configuration/SimetraConfigModel.cs
    src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    src/SnmpCollector/Program.cs
    src/SnmpCollector/config/oidmaps.json
    src/SnmpCollector/config/devices.json
    src/SnmpCollector/config/simetra-config.json
  </files>
  <action>
    **Create `OidMapWatcherService.cs`** in `src/SnmpCollector/Services/`:
    - Sealed class extending `BackgroundService`, same namespace `SnmpCollector.Services`
    - `internal const string ConfigMapName = "simetra-oidmaps";`
    - `internal const string ConfigKey = "oidmaps.json";`
    - Constructor takes: `IKubernetes kubeClient`, `IOidMapService oidMapService`, `ILogger<OidMapWatcherService> logger`
    - Does NOT inject IDeviceRegistry or DynamicPollScheduler (that is the whole point)
    - Own `SemaphoreSlim _reloadLock = new(1, 1)` (independent from device watcher)
    - Same `ReadNamespace()` helper (private static, same as existing)
    - Same watch loop pattern as existing ConfigMapWatcherService (initial load + reconnecting watch loop with 5s backoff)
    - `HandleConfigMapChangedAsync`: reads `ConfigKey` from configMap.Data, deserializes as `Dictionary<string, string>` (NOT SimetraConfigModel), calls `_oidMapService.UpdateMap(oidMap)` inside semaphore
    - Log messages should say "OidMapWatcher" not "ConfigMapWatcher"
    - Use same `JsonSerializerOptions` (ReadCommentHandling.Skip, AllowTrailingCommas, PropertyNameCaseInsensitive)

    **Create `DeviceWatcherService.cs`** in `src/SnmpCollector/Services/`:
    - Sealed class extending `BackgroundService`, same namespace
    - `internal const string ConfigMapName = "simetra-devices";`
    - `internal const string ConfigKey = "devices.json";`
    - Constructor takes: `IKubernetes kubeClient`, `IDeviceRegistry deviceRegistry`, `DynamicPollScheduler pollScheduler`, `ILogger<DeviceWatcherService> logger`
    - Does NOT inject IOidMapService (that is the whole point)
    - Own `SemaphoreSlim _reloadLock = new(1, 1)`
    - Same watch loop pattern
    - `HandleConfigMapChangedAsync`: reads `ConfigKey`, deserializes as `List<DeviceOptions>` (the JSON is a bare array `[...]`), calls `_deviceRegistry.ReloadAsync(devices)` then `_pollScheduler.ReconcileAsync(devices, ct)` inside semaphore
    - Log messages should say "DeviceWatcher"

    **Delete `ConfigMapWatcherService.cs`** (replaced by two new services).

    **Delete `SimetraConfigModel.cs`** (no longer needed -- each watcher deserializes its own simple type directly).

    **Update `ServiceCollectionExtensions.cs`** in the `IsInCluster()` block (lines 232-234):
    - Remove `ConfigMapWatcherService` registration
    - Add `OidMapWatcherService` registration (same pattern: AddSingleton + AddHostedService with factory)
    - Add `DeviceWatcherService` registration (same pattern)
    - Remove `using SnmpCollector.Configuration;` if SimetraConfigModel was the only reason for it (check -- it is also used for DeviceOptions, SiteOptions etc., so the using stays)

    **Update `Program.cs`** local dev block (lines 57-83):
    - Replace `simetra-config.json` loading with two separate file loads:
      1. Load `oidmaps.json`: `var oidmapsPath = Path.Combine(configDir, "oidmaps.json");` -- deserialize as `Dictionary<string, string>`, call `oidMapService.UpdateMap(oidMap)`
      2. Load `devices.json`: `var devicesPath = Path.Combine(configDir, "devices.json");` -- deserialize as `List<DeviceOptions>`, call `deviceRegistry.ReloadAsync(devices)` then `pollScheduler.ReconcileAsync(devices, ct)`
    - Remove `using SnmpCollector.Configuration;` reference to SimetraConfigModel (replace with `using SnmpCollector.Configuration;` for DeviceOptions -- already present)
    - Update comments to reference the two new files instead of simetra-config.json

    **Create `src/SnmpCollector/config/oidmaps.json`**:
    - Extract just the OidMap dictionary from simetra-config.json as a bare `Dictionary<string, string>` JSON object (top-level `{ "oid": "name", ... }`)
    - Keep all comments from the original
    - This is a flat dictionary, NOT wrapped in `{"OidMap": {...}}`

    **Create `src/SnmpCollector/config/devices.json`**:
    - Extract just the Devices array from simetra-config.json as a bare JSON array `[{...}, {...}]`
    - Keep device definitions exactly as they are (Name, IpAddress, Port, CommunityString, MetricPolls)
    - This is a bare array, NOT wrapped in `{"Devices": [...]}`

    **Delete `src/SnmpCollector/config/simetra-config.json`** (replaced by the two new files).
  </action>
  <verify>
    Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- must succeed with no errors.
    Grep for `SimetraConfigModel` and `ConfigMapWatcherService` in src/ -- should find zero references (only in git history).
    Verify `OidMapWatcherService.cs` does NOT reference `IDeviceRegistry` or `DynamicPollScheduler`.
    Verify `DeviceWatcherService.cs` does NOT reference `IOidMapService`.
  </verify>
  <done>
    Two new watcher services exist with independent concerns. SimetraConfigModel and ConfigMapWatcherService are deleted. Program.cs loads two separate files for local dev. Solution compiles cleanly.
  </done>
</task>

<task type="auto">
  <name>Task 2: Split K8s ConfigMaps, update deployment volumes, and fix tests</name>
  <files>
    deploy/k8s/configmap.yaml
    deploy/k8s/production/configmap.yaml
    deploy/k8s/deployment.yaml
    deploy/k8s/production/deployment.yaml
    tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs
  </files>
  <action>
    **Update `deploy/k8s/configmap.yaml`**:
    - Keep `simetra-config` ConfigMap but remove the `simetra-config.json` key. It retains only `appsettings.k8s.json`.
    - Add new ConfigMap `simetra-oidmaps` (same namespace `simetra`) with single key `oidmaps.json` containing the flat OID-to-name dictionary (same content as local dev `oidmaps.json` but for K8s lab environment -- use the K8s IP addresses/hostnames from the current configmap.yaml `simetra-config.json` OidMap section). The JSON should be a bare dictionary `{ "oid": "name", ... }`, NOT wrapped.
    - Add new ConfigMap `simetra-devices` (same namespace `simetra`) with single key `devices.json` containing the devices array (use K8s hostnames like `obp-simulator.simetra.svc.cluster.local` from the current configmap.yaml). The JSON should be a bare array `[{...}]`, NOT wrapped.
    - Use `---` YAML document separator between the three ConfigMap objects.

    **Update `deploy/k8s/production/configmap.yaml`**:
    - Same pattern: keep `simetra-config` for `appsettings.k8s.json` only (remove the `simetra-config.json` key if present -- looking at current file, production configmap has `appsettings.k8s.json` + `oidmap-obp.json` + `oidmap-npb.json` keys).
    - Add `simetra-oidmaps` ConfigMap: merge the existing `oidmap-obp.json` and `oidmap-npb.json` content into a single `oidmaps.json` key with all OID entries in one flat dictionary.
    - Add `simetra-devices` ConfigMap with `devices.json` key containing the production device template (same REPLACE_ME pattern from the existing production configmap).
    - Remove the old `oidmap-obp.json` and `oidmap-npb.json` keys from `simetra-config` (they move into `simetra-oidmaps`).
    - Remove `OidMap` and `Devices` sections from the `appsettings.k8s.json` value in production configmap (they now live in separate ConfigMaps, not in appsettings).

    **Update `deploy/k8s/deployment.yaml`**:
    - Add two more volume mounts to the container (all mount to same `/app/config` directory since they use different file names):
      Actually, K8s ConfigMap volumes mount ALL keys as files in the mountPath directory. Since we now have 3 ConfigMaps but one mount path, we need to use `projected` volume to combine them. Replace the single `configMap` volume with a `projected` volume that projects all three ConfigMaps into `/app/config`:
      ```yaml
      volumes:
      - name: config
        projected:
          sources:
          - configMap:
              name: simetra-config
          - configMap:
              name: simetra-oidmaps
          - configMap:
              name: simetra-devices
      ```
    - The volumeMount stays the same (`mountPath: /app/config`, `readOnly: true`).

    **Update `deploy/k8s/production/deployment.yaml`**:
    - Same projected volume pattern as above.

    **Update `tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs`**:
    - Remove `using SnmpCollector.Configuration;` (SimetraConfigModel is deleted)
    - Update `GetSimetraConfigPath()` to return path to `oidmaps.json` instead of `simetra-config.json`. Rename method to `GetOidMapsPath()`.
    - Update `LoadOidMapFromSimetraConfig()`: read `oidmaps.json`, deserialize as `Dictionary<string, string>` directly (not via SimetraConfigModel). Rename to `LoadOidMap()`.
    - Update all call sites in the test class to use the renamed methods.
    - Update test comments to reference `oidmaps.json` instead of `simetra-config.json`.
    - The `LoadsOidMapFromJsoncFile` and `MergesMultipleOidMapFiles` tests use temp files and IConfiguration binding -- these don't reference SimetraConfigModel and should remain as-is (they test IConfiguration binding, not file loading).
  </action>
  <verify>
    Run `dotnet build` for the full solution -- must succeed.
    Run `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests must pass.
    Verify `deploy/k8s/configmap.yaml` contains three ConfigMap objects separated by `---`.
    Verify `deploy/k8s/deployment.yaml` uses `projected` volume with three sources.
    Grep for `simetra-config.json` across entire repo -- should only appear in git history, not in any active file.
  </verify>
  <done>
    K8s manifests define three ConfigMaps (simetra-config for appsettings, simetra-oidmaps for OID map, simetra-devices for devices). Deployments use projected volumes. All tests pass against the new oidmaps.json file.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` succeeds
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests pass
3. No references to `SimetraConfigModel` or `ConfigMapWatcherService` in src/ or tests/
4. No references to `simetra-config.json` in any active source file
5. `OidMapWatcherService` only depends on `IOidMapService` (no device/scheduler deps)
6. `DeviceWatcherService` only depends on `IDeviceRegistry` + `DynamicPollScheduler` (no OID map dep)
7. K8s configmap.yaml has three `---`-separated ConfigMap objects
8. Deployment yamls use `projected` volumes with three ConfigMap sources
</verification>

<success_criteria>
- Solution builds with zero errors and zero warnings related to removed types
- All existing tests pass (OidMapAutoScanTests updated to read oidmaps.json)
- OidMapWatcherService watches `simetra-oidmaps` ConfigMap and only calls UpdateMap
- DeviceWatcherService watches `simetra-devices` ConfigMap and only calls ReloadAsync + ReconcileAsync
- Local dev Program.cs loads oidmaps.json and devices.json independently
- K8s manifests split into three ConfigMaps with projected volume mounts
</success_criteria>

<output>
After completion, create `.planning/quick/017-split-configmap-oidmap-devices/017-SUMMARY.md`
</output>
