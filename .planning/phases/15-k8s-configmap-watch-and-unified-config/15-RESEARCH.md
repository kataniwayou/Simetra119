# Phase 15: K8s ConfigMap Watch and Unified Config - Research

**Researched:** 2026-03-07
**Domain:** Kubernetes ConfigMap watching, JSONC config parsing, Quartz.NET dynamic job management
**Confidence:** HIGH

## Summary

This phase replaces the file-based `IOptionsMonitor` + `reloadOnChange` hot-reload mechanism with a K8s API watch on a single unified ConfigMap key. The codebase already uses `KubernetesClient` 18.0.13 for leader election (`K8sLeaseElection`), providing an established pattern for the watch service. The unified config merges `oidmap-obp.json`, `oidmap-npb.json`, and `devices.json` into one JSONC key (`simetra-config.json`) within the existing `simetra-config` ConfigMap.

The primary challenge is dynamic Quartz job management: when devices change, poll jobs must be added/removed at runtime via `ISchedulerFactory` -> `IScheduler`. The secondary challenge is replacing `IOptionsMonitor<OidMapOptions>` in `OidMapService` with a callback mechanism from the ConfigMap watcher, maintaining the atomic `FrozenDictionary` swap pattern.

**Primary recommendation:** Build a `ConfigMapWatcherService` as a `BackgroundService` (matching the `K8sLeaseElection` pattern already in the codebase) that watches the `simetra-config` ConfigMap, parses the unified JSONC key, and notifies `OidMapService` and a new `DynamicPollScheduler` via direct method calls.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| KubernetesClient | 18.0.13 (current) | K8s API watch on ConfigMap | Already in codebase for leader election; official C# K8s client |
| System.Text.Json | built-in (net9.0) | JSONC parsing with comments | Built-in; `ReadCommentHandling = JsonCommentHandling.Skip` + `AllowTrailingCommas = true` |
| Quartz.NET | 3.15.1 (current) | Dynamic job add/remove at runtime | Already in codebase; `IScheduler.ScheduleJob` / `DeleteJob` for runtime management |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Hosting | 9.0.0 | BackgroundService base for watcher | ConfigMap watcher lifecycle |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| KubernetesClient 18.0.13 | Upgrade to 19.0.2 | Latest targets K8s 1.35; 18.0.13 targets K8s 1.34; upgrade is optional but not needed for ConfigMap watch API which is stable |

**Installation:** No new packages needed. All required packages are already referenced.

## Architecture Patterns

### Recommended Project Structure
```
src/SnmpCollector/
  Services/
    ConfigMapWatcherService.cs    # BackgroundService: K8s API watch + JSONC parse
    DynamicPollScheduler.cs       # Quartz job add/remove on device config change
    IConfigReloadHandler.cs       # Interface for reload notification
  Configuration/
    SimetraConfigModel.cs         # POCO for unified config JSON structure
    ConfigLoaderService.cs        # Loads config from file (local) or ConfigMap (K8s)
  Pipeline/
    OidMapService.cs              # Modified: remove IOptionsMonitor, add UpdateMap method
    DeviceRegistry.cs             # Modified: make mutable with atomic swap
  config/
    simetra-config.json           # Local dev fallback file (unified format)
```

### Pattern 1: BackgroundService ConfigMap Watcher with Reconnect Loop
**What:** A `BackgroundService` that uses `IKubernetes.CoreV1.ListNamespacedConfigMapWithHttpMessagesAsync(watch: true)` with a reconnect loop, matching the `K8sLeaseElection` pattern.
**When to use:** Always when running in K8s (detected via `KubernetesClientConfiguration.IsInCluster()`).
**Example:**
```csharp
// Source: kubernetes-client/csharp watch pattern + K8sLeaseElection.cs existing pattern
public sealed class ConfigMapWatcherService : BackgroundService
{
    private readonly IKubernetes _kubeClient;
    private readonly string _namespace;
    private readonly string _configMapName;
    private readonly string _configKey;
    private readonly IConfigReloadHandler _reloadHandler;
    private readonly ILogger<ConfigMapWatcherService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load from ConfigMap
        await LoadInitialConfigAsync(stoppingToken);

        // Watch loop with reconnect
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = _kubeClient.CoreV1
                    .ListNamespacedConfigMapWithHttpMessagesAsync(
                        _namespace,
                        fieldSelector: $"metadata.name={_configMapName}",
                        watch: true,
                        cancellationToken: stoppingToken);

                await foreach (var (type, configMap) in response
                    .WatchAsync<V1ConfigMap, V1ConfigMapList>(cancellationToken: stoppingToken))
                {
                    if (type == WatchEventType.Modified)
                    {
                        HandleConfigMapModified(configMap);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ConfigMap watch disconnected, reconnecting in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void HandleConfigMapModified(V1ConfigMap configMap)
    {
        if (!configMap.Data.TryGetValue(_configKey, out var jsonContent))
            return;

        var config = ParseJsonc(jsonContent);
        _reloadHandler.OnConfigReloaded(config);
    }
}
```

### Pattern 2: JSONC Parsing with System.Text.Json
**What:** Parse JSONC (JSON with `//` comments) using built-in `System.Text.Json` options.
**When to use:** Always when deserializing the unified config key.
**Example:**
```csharp
// Source: Microsoft Learn - System.Text.Json invalid JSON handling
private static readonly JsonSerializerOptions JsoncOptions = new()
{
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true
};

public static SimetraConfigModel ParseJsonc(string jsonContent)
{
    return JsonSerializer.Deserialize<SimetraConfigModel>(jsonContent, JsoncOptions)
        ?? throw new InvalidOperationException("Failed to parse ConfigMap JSONC content");
}
```

### Pattern 3: Dynamic Quartz Job Management via ISchedulerFactory
**What:** Use `ISchedulerFactory.GetScheduler()` to get the running `IScheduler`, then use `ScheduleJob`, `DeleteJob`, and `GetJobKeys` for runtime job management.
**When to use:** When device config changes (devices added/removed, poll intervals changed).
**Example:**
```csharp
// Source: Quartz.NET IScheduler API
public sealed class DynamicPollScheduler
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IJobIntervalRegistry _intervalRegistry;

    public async Task ReconcileJobsAsync(
        IReadOnlyList<DeviceOptions> newDevices,
        CancellationToken ct)
    {
        var scheduler = await _schedulerFactory.GetScheduler(ct);

        // Get current poll job keys (prefix-based filtering)
        var currentKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals("DEFAULT"), ct);
        var pollJobKeys = currentKeys
            .Where(k => k.Name.StartsWith("metric-poll-"))
            .ToHashSet();

        // Build desired state from new config
        var desiredJobs = BuildDesiredJobSet(newDevices);

        // Remove jobs no longer needed
        foreach (var key in pollJobKeys.Except(desiredJobs.Keys))
        {
            await scheduler.DeleteJob(key, ct);
            _intervalRegistry.Unregister(key.Name);
        }

        // Add or update jobs
        foreach (var (key, jobConfig) in desiredJobs)
        {
            if (pollJobKeys.Contains(key))
            {
                // Job exists -- check if interval changed, reschedule if needed
                var triggers = await scheduler.GetTriggersOfJob(key, ct);
                // ... compare and reschedule
            }
            else
            {
                // New job -- schedule it
                var job = JobBuilder.Create<MetricPollJob>()
                    .WithIdentity(key)
                    .UsingJobData("deviceName", jobConfig.DeviceName)
                    .UsingJobData("pollIndex", jobConfig.PollIndex)
                    .UsingJobData("intervalSeconds", jobConfig.IntervalSeconds)
                    .Build();

                var trigger = TriggerBuilder.Create()
                    .WithIdentity($"{key.Name}-trigger")
                    .StartNow()
                    .WithSimpleSchedule(s => s
                        .WithIntervalInSeconds(jobConfig.IntervalSeconds)
                        .RepeatForever()
                        .WithMisfireHandlingInstructionNextWithRemainingCount())
                    .Build();

                await scheduler.ScheduleJob(job, trigger, ct);
                _intervalRegistry.Register(key.Name, jobConfig.IntervalSeconds);
            }
        }
    }
}
```

### Pattern 4: Atomic DeviceRegistry Swap
**What:** Make `DeviceRegistry` support reload by adding a `Reload(DevicesOptions)` method that rebuilds the `FrozenDictionary` maps and swaps them atomically (same pattern as `OidMapService`).
**When to use:** On device config change from ConfigMap watcher.
**Example:**
```csharp
// Matches existing OidMapService volatile swap pattern
public sealed class DeviceRegistry : IDeviceRegistry
{
    private volatile FrozenDictionary<IPAddress, DeviceInfo> _byIp;
    private volatile FrozenDictionary<string, DeviceInfo> _byName;

    // Called by ConfigMap watcher on reload
    public void Reload(List<DeviceOptions> devices) { /* rebuild + swap */ }
}
```

### Pattern 5: Unified Config JSON Structure
**What:** Single JSON document with both OidMap and Devices sections.
**When to use:** The single ConfigMap key format.
**Example:**
```jsonc
{
  // ===========================================================================
  // Simetra Unified Device Configuration
  // ===========================================================================

  "OidMap": {
    // ---- OBP (Optical Bypass) OIDs ----
    // SNMP type: INTEGER | Values: 1=active, 2=bypass, 3=fault
    "1.3.6.1.4.1.47477.10.21.1.3.1.0": "obp_link_state_L1",
    // ... (92 total OIDs)
  },

  "Devices": [
    {
      "Name": "OBP-01",
      "IpAddress": "obp-simulator.simetra.svc.cluster.local",
      "Port": 161,
      "CommunityString": "Simetra.OBP-01",
      "MetricPolls": [
        {
          "IntervalSeconds": 10,
          "Oids": ["1.3.6.1.4.1.47477.10.21.1.3.1.0", "..."]
        }
      ]
    }
  ]
}
```

### Anti-Patterns to Avoid
- **Using IOptionsMonitor for ConfigMap watch:** The whole point is to replace `IOptionsMonitor` + `reloadOnChange` with direct K8s API watch. Do not layer the new ConfigMap data back into `IConfiguration`.
- **Polling the K8s API instead of watching:** K8s watch API is event-driven; polling adds latency and load.
- **Restarting the entire host on config change:** The design explicitly avoids pod restarts; use atomic swaps and dynamic job management.
- **Concurrent reload processing:** If a second ConfigMap change arrives while the first is still being processed, queue or debounce -- do not process concurrently.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSONC parsing | Custom comment stripping regex | `System.Text.Json` with `ReadCommentHandling = Skip` | Handles `//`, `/* */`, trailing commas correctly |
| K8s API authentication | Manual token reading from `/var/run/secrets` | `KubernetesClientConfiguration.InClusterConfig()` | Already used in `K8sLeaseElection`; handles cert + token |
| Watch reconnection | Custom HTTP long-poll | `ListNamespacedConfigMapWithHttpMessagesAsync(watch: true)` + reconnect loop | K8s watch protocol with proper `resourceVersion` tracking |
| Job scheduling diff | Manual list comparison | `IScheduler.GetJobKeys()` + set operations | Quartz tracks all jobs; use its query API |
| In-cluster detection | Manual env var check | `KubernetesClientConfiguration.IsInCluster()` | Already used in `ServiceCollectionExtensions.AddSnmpConfiguration` |

**Key insight:** The codebase already has `IKubernetes` registered as a singleton and `KubernetesClientConfiguration.IsInCluster()` branching in `AddSnmpConfiguration`. The new watcher follows the exact same registration pattern as `K8sLeaseElection`.

## Common Pitfalls

### Pitfall 1: Watch Connection Drops After ~30 Minutes
**What goes wrong:** The K8s API server closes watch connections after a timeout (typically 30 minutes). The watcher stops receiving events silently.
**Why it happens:** K8s API server enforces a max watch duration. The C# `Watcher` class does not auto-reconnect.
**How to avoid:** Wrap the watch in a `while (!stoppingToken.IsCancellationRequested)` reconnect loop. On `OnClose` or stream completion, re-establish the watch with a brief delay (5s). Always do an initial full load before starting the watch to avoid missing events during startup.
**Warning signs:** Config changes stop being detected after the pod has been running for ~30 minutes.

### Pitfall 2: DeviceRegistry is Currently Immutable (Built Once at Startup)
**What goes wrong:** `DeviceRegistry` constructor takes `IOptions<DevicesOptions>` (not `IOptionsMonitor`), builds `FrozenDictionary` once, and has no reload mechanism. Adding/removing devices at runtime requires making it mutable.
**Why it happens:** Phase 2 design assumed devices are static at startup.
**How to avoid:** Add a `Reload(List<DeviceOptions>)` method using the same `volatile` + `FrozenDictionary` swap pattern as `OidMapService`. Change constructor from `IOptions<DevicesOptions>` to accept initial config, then support reload via method call.
**Warning signs:** New devices added via ConfigMap don't appear; removed devices continue to be polled.

### Pitfall 3: Quartz Thread Pool Size is Static
**What goes wrong:** `UseDefaultThreadPool(maxConcurrency: jobCount)` is set at startup in `AddSnmpScheduling`. Adding more devices at runtime may exceed the thread pool capacity, causing job queueing.
**Why it happens:** Quartz thread pool size is configured once during `AddQuartz`.
**How to avoid:** Either set an initially generous thread pool size (e.g., maxConcurrency = 50), or accept that Quartz will queue jobs beyond capacity (which is acceptable since `DisallowConcurrentExecution` already prevents pile-up). Log a warning when adding jobs would exceed the thread pool size.
**Warning signs:** Poll jobs start experiencing delays after device additions.

### Pitfall 4: Concurrent ConfigMap Change During Reload
**What goes wrong:** A second ConfigMap change arrives while the first reload is still processing (DNS resolution, Quartz job registration). Two concurrent reloads may leave the system in an inconsistent state.
**Why it happens:** K8s can emit multiple Modified events in rapid succession (e.g., user applies twice quickly).
**How to avoid:** Use a `SemaphoreSlim(1, 1)` or `Channel<T>` to serialize reload operations. Process only the latest config, discarding intermediate states.
**Warning signs:** Sporadic device duplication or missing devices after rapid config changes.

### Pitfall 5: DNS Resolution Blocking in DeviceRegistry.Reload
**What goes wrong:** `Dns.GetHostAddresses()` is synchronous and blocks the thread during reload. K8s Service DNS resolution can take 1-5 seconds.
**Why it happens:** Current `DeviceRegistry` constructor uses synchronous DNS resolution (acceptable at startup, problematic for reload).
**How to avoid:** Use `Dns.GetHostAddressesAsync()` in the reload path. The initial startup path can remain synchronous since it runs before the host starts.
**Warning signs:** Watch event processing stalls during reload if devices use DNS names.

### Pitfall 6: JobIntervalRegistry Needs Cleanup on Job Removal
**What goes wrong:** `JobIntervalRegistry` is populated at startup and used by `LivenessHealthCheck` for staleness thresholds. If jobs are removed but not unregistered from `JobIntervalRegistry`, the liveness check expects stamps from non-existent jobs.
**Why it happens:** `JobIntervalRegistry` was designed as write-once at startup.
**How to avoid:** Add an `Unregister(string jobKey)` method to `IJobIntervalRegistry`. Call it when removing Quartz jobs during device config reload.
**Warning signs:** Liveness probe fails after removing a device because it expects stamps from removed jobs.

### Pitfall 7: OidMapService Constructor Depends on IOptionsMonitor
**What goes wrong:** `OidMapService` currently takes `IOptionsMonitor<OidMapOptions>` in its constructor and subscribes to `OnChange`. Removing `IOptionsMonitor` changes the constructor signature, breaking existing tests.
**Why it happens:** The `IOptionsMonitor` pattern was the original hot-reload mechanism.
**How to avoid:** Replace `IOptionsMonitor<OidMapOptions>` with initial data injection + a public `UpdateMap(Dictionary<string, string> entries)` method. Update `OidMapServiceTests` to call `UpdateMap` directly instead of using `TestOptionsMonitor.Change()`.
**Warning signs:** Tests fail after removing `IOptionsMonitor` dependency.

## Code Examples

### Unified Config POCO Model
```csharp
// New file: Configuration/SimetraConfigModel.cs
public sealed class SimetraConfigModel
{
    public Dictionary<string, string> OidMap { get; set; } = new();
    public List<DeviceOptions> Devices { get; set; } = new();
}
```

### DI Registration Pattern (Matching K8sLeaseElection)
```csharp
// In AddSnmpConfiguration, inside the IsInCluster() branch:
if (KubernetesClientConfiguration.IsInCluster())
{
    // ... existing leader election registration ...

    // ConfigMap watcher (same singleton-forward pattern as K8sLeaseElection)
    services.AddSingleton<ConfigMapWatcherService>();
    services.AddHostedService(sp => sp.GetRequiredService<ConfigMapWatcherService>());
}
else
{
    // Local dev: load from file, no watch
    services.AddSingleton<IConfigReloadHandler>(sp =>
    {
        var filePath = Path.Combine(configDir, "simetra-config.json");
        // Load once from file, no hot-reload in local mode
        return new FileConfigLoader(filePath);
    });
}
```

### RBAC Update for ConfigMap Watch
```yaml
# Updated rbac.yaml -- add configmaps rule to existing role
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: simetra-lease-role  # Consider renaming to simetra-role
  namespace: simetra
rules:
- apiGroups: ["coordination.k8s.io"]
  resources: ["leases"]
  verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
- apiGroups: [""]
  resources: ["configmaps"]
  verbs: ["get", "list", "watch"]
```

### IJobIntervalRegistry Extension
```csharp
// Add to existing IJobIntervalRegistry interface:
void Unregister(string jobKey);

// In JobIntervalRegistry implementation:
public void Unregister(string jobKey)
{
    _intervals.TryRemove(jobKey, out _);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `reloadOnChange: true` + `IOptionsMonitor` | K8s API watch on ConfigMap | This phase | Eliminates 30-60s volume propagation delay; instant reload |
| Separate `oidmap-*.json` + `devices.json` | Single `simetra-config.json` key | This phase | Single source of truth with inline JSONC documentation |
| Static `DeviceRegistry` built at startup | Mutable `DeviceRegistry` with atomic swap | This phase | Devices can be added/removed without pod restart |
| Static Quartz job registration at startup | Dynamic `IScheduler.ScheduleJob` / `DeleteJob` | This phase | Poll jobs reconciled on config change |

**Deprecated/outdated:**
- `oidmap-obp.json`, `oidmap-npb.json`: Replaced by unified key in ConfigMap
- `devices.json`: Replaced by `Devices` section in unified key
- `reloadOnChange: true` on AddJsonFile calls: Replaced by K8s API watch
- `IOptionsMonitor<OidMapOptions>` in OidMapService: Replaced by direct callback from watcher

## Open Questions

Things that could not be fully resolved:

1. **ConfigMap key naming: `simetra-config.json` vs `simetra-config.jsonc`**
   - What we know: K8s ConfigMap keys can have any name. `.jsonc` extension signals "has comments" but has no functional effect since we parse with `System.Text.Json` options regardless.
   - What's unclear: Whether `.jsonc` extension causes issues with any K8s tooling or editors.
   - Recommendation: Use `simetra-config.json` (plain `.json`) since the JSONC parsing is handled by `System.Text.Json` options. K8s tooling and `kubectl edit` work fine with comments in JSON regardless of extension.

2. **Quartz thread pool resize at runtime**
   - What we know: Quartz `UseDefaultThreadPool(maxConcurrency)` is set once at startup. No public API to resize at runtime.
   - What's unclear: Whether Quartz SimpleThreadPool supports dynamic resize in version 3.15.1.
   - Recommendation: Set initial thread pool to a generous ceiling (e.g., 50 threads) or accept queueing for the rare case of many dynamically-added devices. Log when job count approaches pool capacity.

3. **DeviceUnreachabilityTracker cleanup on device removal**
   - What we know: `DeviceUnreachabilityTracker` uses `ConcurrentDictionary<string, int>` keyed by device name. Removed devices leave stale entries.
   - What's unclear: Whether stale entries cause any functional issues (they don't since removed devices won't trigger lookups).
   - Recommendation: Add a `Remove(string deviceName)` method for cleanliness but it is low priority.

## Sources

### Primary (HIGH confidence)
- Existing codebase: `K8sLeaseElection.cs` -- established pattern for `BackgroundService` + `IKubernetes` usage
- Existing codebase: `OidMapService.cs` -- established `FrozenDictionary` atomic swap pattern
- Existing codebase: `ServiceCollectionExtensions.cs` -- established DI registration and `IsInCluster()` branching
- Existing codebase: `configmap.yaml` -- current ConfigMap structure with separate keys
- [Microsoft Learn - System.Text.Json JSONC handling](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/invalid-json) -- `ReadCommentHandling`, `AllowTrailingCommas`

### Secondary (MEDIUM confidence)
- [kubernetes-client/csharp GitHub](https://github.com/kubernetes-client/csharp) -- watch API pattern, version 18.0.13 -> 19.0.2 availability
- [Quartz.NET IScheduler API](https://github.com/quartznet/quartznet/blob/main/src/Quartz/IScheduler.cs) -- `DeleteJob`, `ScheduleJob`, `GetJobKeys` methods
- [Quartz.NET Rescheduling Docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/how-tos/rescheduling-jobs.html) -- runtime job management patterns
- [NuGet KubernetesClient 19.0.2](https://www.nuget.org/packages/KubernetesClient/) -- latest version info

### Tertiary (LOW confidence)
- [kubernetes-client/csharp Issue #533](https://github.com/kubernetes-client/csharp/issues/533) -- watch connection drop after ~30 minutes
- [kubernetes-client/csharp Issue #486](https://github.com/kubernetes-client/csharp/issues/486) -- watcher retry mechanism gaps

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - all libraries already in the project; API patterns well-established in codebase
- Architecture: HIGH - follows existing `K8sLeaseElection` and `OidMapService` patterns directly
- Pitfalls: HIGH - identified through direct codebase analysis of current `DeviceRegistry`, `JobIntervalRegistry`, `OidMapService` constructors and their dependencies
- Dynamic Quartz: MEDIUM - `IScheduler` runtime API is well-documented but the thread pool resize limitation is based on training knowledge

**Research date:** 2026-03-07
**Valid until:** 2026-04-07 (30 days -- stable domain, established libraries)
