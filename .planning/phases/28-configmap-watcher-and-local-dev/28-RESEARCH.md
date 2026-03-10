# Phase 28: ConfigMap Watcher and Local Dev - Research

**Researched:** 2026-03-10
**Domain:** K8s ConfigMap watch pattern (C# BackgroundService + KubernetesClient)
**Confidence:** HIGH

## Summary

Phase 28 is a pure structural replication task. `OidMapWatcherService` and `DeviceWatcherService` already exist, are fully working, and define the exact pattern TenantVectorWatcherService must follow. The codebase also already has `TenantVectorRegistry.Reload(TenantVectorOptions)` with built-in structured diff logging, `TenantVectorOptionsValidator`, and a local dev `tenantvector.json` in the config directory. The only novel work is wiring the new watcher into `AddSnmpConfiguration`'s `IsInCluster()` block and adding a local dev load block in Program.cs matching the existing OID map / devices pattern.

There are no new dependencies, no new patterns to discover, and no ambiguous design decisions — all choices are locked in the CONTEXT.md and backed by the existing watcher implementations. Research confirms the existing code can be directly imitated line-by-line.

**Primary recommendation:** Copy OidMapWatcherService's structure verbatim, substituting the ConfigMap name, config key, type (`TenantVectorOptions`), validator call, and registry call. The diff logging requirement is already satisfied by `TenantVectorRegistry.Reload()`.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `KSail.KubernetesClient` (k8s) | 18.x (already in project) | `IKubernetes`, `V1ConfigMap`, `WatchEventType`, `WatchAsync` | Already used by OidMapWatcherService and DeviceWatcherService |
| `Microsoft.Extensions.Hosting` | .NET 9 | `BackgroundService` base class | Pattern mandated by existing watchers |
| `System.Text.Json` | .NET 9 | JSON deserialization of ConfigMap data | Already used identically in both existing watchers |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.Extensions.Options` | .NET 9 | `IValidateOptions<TenantVectorOptions>` | Watcher calls validator before Reload() |

### Alternatives Considered

None. All choices are locked.

## Architecture Patterns

### Recommended Project Structure

New file location:
```
src/SnmpCollector/
└── Services/
    ├── OidMapWatcherService.cs      # Existing
    ├── DeviceWatcherService.cs      # Existing
    └── TenantVectorWatcherService.cs # NEW — mirrors OidMapWatcherService exactly
```

Registration locations (both need changes):
```
src/SnmpCollector/
├── Extensions/ServiceCollectionExtensions.cs  # AddSnmpConfiguration — IsInCluster() block
└── Program.cs                                  # Local dev section after app.Build()
```

K8s manifest update:
```
deploy/k8s/production/
└── configmap.yaml  # Add simetra-tenantvector ConfigMap
```

### Pattern 1: TenantVectorWatcherService Structure

The watcher is a near-verbatim copy of OidMapWatcherService with these substitutions:

| OidMapWatcherService | TenantVectorWatcherService |
|---------------------|---------------------------|
| `ConfigMapName = "simetra-oidmaps"` | `ConfigMapName = "simetra-tenantvector"` |
| `ConfigKey = "oidmaps.json"` | `ConfigKey = "tenantvector.json"` |
| `IOidMapService _oidMapService` | `TenantVectorRegistry _registry` + `TenantVectorOptionsValidator _validator` |
| `Deserialize<Dictionary<string, string>>` | `Deserialize<TenantVectorOptions>` |
| `_oidMapService.UpdateMap(oidMap)` | `_validator.Validate(null, options)` then `_registry.Reload(options)` |
| Log: `"OidMapWatcher..."` | Log: `"TenantVectorWatcher..."` |
| Log: `"OID map reload complete"` | Log: `"Tenant vector reload complete"` |
| Log: `"previous map remains active"` | Log: `"previous config remains active"` |

The `HandleConfigMapChangedAsync` method has one additional step for validation:
1. Check `configMap.Data` has `ConfigKey` → warning if missing, return
2. Deserialize JSON → log Error and return on `JsonException`
3. Null-check deserialized value → warning if null, return
4. Call `_validator.Validate(null, options)` → log Error and return on failure
5. Acquire `_reloadLock`, call `_registry.Reload(options)`, release lock
6. Log Information: reload complete with tenant/slot counts (already logged inside `Reload()`)

**Important:** `TenantVectorOptionsValidator` requires `IOidMapService` injection. When the watcher fires (in K8s mode), the OID map is already loaded by `OidMapWatcherService`. On first load the OID map could be empty — the validator already handles this: it skips MetricName validation with a warning log if `EntryCount == 0`. This is the correct behavior and requires no special handling in the watcher.

### Pattern 2: DI Registration in AddSnmpConfiguration

In `ServiceCollectionExtensions.cs`, inside the `if (IsInCluster())` block, add after `DeviceWatcherService`:

```csharp
// Phase 28: Tenant vector ConfigMap watcher
services.AddSingleton<TenantVectorWatcherService>();
services.AddHostedService(sp => sp.GetRequiredService<TenantVectorWatcherService>());
```

This is the concrete-first / resolve-same-instance pattern already used for `K8sLeaseElection`, `OidMapWatcherService`, and `DeviceWatcherService`.

**Constructor injection needed:** `IKubernetes`, `TenantVectorRegistry`, `TenantVectorOptionsValidator`, `ILogger<TenantVectorWatcherService>`. All are already registered in DI:
- `IKubernetes`: registered in the `IsInCluster()` block
- `TenantVectorRegistry`: registered as singleton in Phase 26 code (line 298-301 of ServiceCollectionExtensions.cs)
- `TenantVectorOptionsValidator`: registered as `IValidateOptions<TenantVectorOptions>` at line 295; watcher must inject the concrete type directly or resolve via DI differently

**Key subtlety:** `TenantVectorOptionsValidator` is registered as `IValidateOptions<TenantVectorOptions>`, not as its concrete type. The watcher can either:
1. Inject `IValidateOptions<TenantVectorOptions>` and cast/enumerate — unusual pattern
2. Inject `TenantVectorOptionsValidator` directly after registering its concrete type: `services.AddSingleton<TenantVectorOptionsValidator>()`

Option 2 matches how existing code handles similar cases. `TenantVectorOptionsValidator` is already registered once as `IValidateOptions<TenantVectorOptions>`. To also inject it as a concrete type, register it as concrete first and resolve from that:

```csharp
services.AddSingleton<TenantVectorOptionsValidator>();
services.AddSingleton<IValidateOptions<TenantVectorOptions>>(
    sp => sp.GetRequiredService<TenantVectorOptionsValidator>());
```

This is the same pattern as `K8sLeaseElection` (line 234-236). The existing registration at line 295 (`AddSingleton<IValidateOptions<TenantVectorOptions>, TenantVectorOptionsValidator>()`) creates the concrete type internally — it must be replaced with the two-step pattern above so the same instance serves both roles.

### Pattern 3: Local Dev Load in Program.cs

After the existing `// Load devices from devices.json` block (ends around line 104), add:

```csharp
// Load tenant vector from tenantvector.json (TenantVectorOptions shape)
var tenantVectorPath = Path.Combine(configDir, "tenantvector.json");
if (File.Exists(tenantVectorPath))
{
    var tvJson = File.ReadAllText(tenantVectorPath);
    var tvOptions = System.Text.Json.JsonSerializer.Deserialize<TenantVectorOptions>(tvJson, jsonOptions);
    if (tvOptions != null)
    {
        var tvValidator = app.Services.GetRequiredService<TenantVectorOptionsValidator>();
        var validationResult = tvValidator.Validate(null, tvOptions);
        if (!validationResult.Failed)
        {
            var tvRegistry = app.Services.GetRequiredService<TenantVectorRegistry>();
            tvRegistry.Reload(tvOptions);
        }
        // else: log failures — or rely on ValidateOnStart to have already caught them
    }
}
```

Note: The existing `appsettings` load in Program.cs (lines 33-37) already loads `tenantvector.json` via `IConfiguration`. That path is for appsettings binding. The local dev Program.cs block directly calls `TenantVectorRegistry.Reload()` — the same way the OID map and devices blocks directly call `UpdateMap()` and `ReloadAsync()`. These are parallel mechanisms that both need to happen in local dev mode.

**Validation in local dev:** `TenantVectorOptionsValidator` requires `IOidMapService`. At the point local dev block runs (after `app.Build()`), `OidMapService` has already been populated by the OID map loading block above it. The validator will have a populated OID map if `oidmaps.json` is loaded first. The ordering in Program.cs already has OID map loaded first, devices second — tenant vector should be third.

### Pattern 4: K8s ConfigMap Manifest

Add a new ConfigMap to `deploy/k8s/production/configmap.yaml`:

```yaml
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenantvector
  namespace: simetra
data:
  # TenantVectorWatcherService watches this ConfigMap for live reload.
  tenantvector.json: |
    {
      "TenantVector": {
        "Tenants": []
      }
    }
```

The JSON structure matches `TenantVectorOptions` with the `"TenantVector"` section wrapper (matching `TenantVectorOptions.SectionName`).

**Important subtlety:** The local dev `config/tenantvector.json` uses the full section wrapper `{ "TenantVector": { "Tenants": [...] } }` because it feeds the IConfiguration binding path. The ConfigMap's `tenantvector.json` value is deserialized directly as `TenantVectorOptions` by the watcher — it must also include the `"TenantVector"` wrapper because `JsonSerializer.Deserialize<TenantVectorOptions>` maps the top-level JSON object to `TenantVectorOptions` properties. However, `TenantVectorOptions` has a `Tenants` property, not a `TenantVector` property. So the ConfigMap JSON should be a bare `TenantVectorOptions` document: `{ "Tenants": [...] }` without the wrapper.

This matches how `OidMapWatcherService` handles `oidmaps.json`: the ConfigMap contains a bare dictionary `{"oid": "name"}`, not `{"OidMap": {"oid": "name"}}`. The section name is an IConfiguration concept; direct JSON deserialization bypasses it.

The local dev `config/tenantvector.json` needs this same distinction verified. Currently it has the wrapper `{ "TenantVector": { "Tenants": [...] } }` — that's for IConfiguration `AddJsonFile`. The watcher would need to deserialize without the wrapper, or deserialize with a surrounding object. Both existing watchers (OidMap, Devices) use bare format in their ConfigMap JSON, matching the `Deserialize<T>` call on the raw content.

### Anti-Patterns to Avoid

- **Double-registering TenantVectorOptionsValidator without the concrete-first pattern:** If `AddSingleton<IValidateOptions<..>, TenantVectorOptionsValidator>()` stays as-is (line 295), and the watcher injects `TenantVectorOptionsValidator` directly, DI will create two instances. Use the concrete-first pattern.
- **Injecting IValidateOptions<TenantVectorOptions> in the watcher:** Awkward — `IValidateOptions<T>` is the framework abstraction, not meant to be injected directly into business services. Inject the concrete validator.
- **Using WatchAsync without the CS0618 pragma suppress:** The `WatchAsync` overload on `HttpOperationResponse` is marked obsolete in KubernetesClient 18.x but no IAsyncEnumerable replacement exists. Both existing watchers suppress this with `#pragma disable CS0618`. TenantVectorWatcherService must do the same.
- **Forgetting to handle the `Deleted` WatchEventType:** Must log a warning and retain current config, identical to existing watchers.
- **Wrapping ConfigMap JSON in section key:** ConfigMap data contains bare `TenantVectorOptions` JSON (`{ "Tenants": [...] }`), not `{ "TenantVector": { "Tenants": [...] } }`. The section wrapper is IConfiguration's concern, not direct JSON deserialization.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Watch reconnection | Custom retry logic | Existing `while (!ct.IsCancellationRequested)` loop with 5s delay | Already proven in OidMapWatcherService and DeviceWatcherService |
| Reload serialization | Lock or Monitor | `SemaphoreSlim(1, 1)` | Async-compatible, matches existing watchers |
| Diff logging | Custom diff computation | `TenantVectorRegistry.Reload()` already logs full diff | Added/removed/unchanged/carried-over already in the Reload log |
| Namespace discovery | Environment variable | `ReadNamespace()` reading `/var/run/secrets/kubernetes.io/serviceaccount/namespace` | Already extracted as static helper in both watchers — copy verbatim |

## Common Pitfalls

### Pitfall 1: ConfigMap JSON Format Mismatch (TenantVector section wrapper)

**What goes wrong:** ConfigMap JSON has `{ "TenantVector": { "Tenants": [...] } }` (IConfiguration-style) but the watcher deserializes as `TenantVectorOptions` which has no `TenantVector` property. Result: empty tenants, silent failure (no parse error).

**Why it happens:** The local dev `config/tenantvector.json` uses the section wrapper for `AddJsonFile()`. It's easy to copy this format to the ConfigMap.

**How to avoid:** ConfigMap `tenantvector.json` value should be a bare `{ "Tenants": [...] }` document. Verify by checking that `Deserialize<TenantVectorOptions>(json)` produces non-null with non-empty Tenants.

**Warning signs:** Reload logs show `tenants=0, slots=0` after a ConfigMap update with non-empty JSON.

### Pitfall 2: TenantVectorOptionsValidator DI Double-Instance

**What goes wrong:** `services.AddSingleton<IValidateOptions<TenantVectorOptions>, TenantVectorOptionsValidator>()` (existing, line 295) registers a new concrete instance. Then `services.AddSingleton<TenantVectorOptionsValidator>()` creates a second instance. The watcher gets one instance; the framework validator uses another.

**Why it happens:** Not a functional problem per se (both work independently), but wastes memory and can cause confusion.

**How to avoid:** Replace the existing registration with the concrete-first two-step pattern. Verify with a unit test that `sp.GetRequiredService<TenantVectorOptionsValidator>()` and `sp.GetRequiredService<IValidateOptions<TenantVectorOptions>>()` return the same instance.

### Pitfall 3: Validation Before OID Map Loaded (K8s Race)

**What goes wrong:** `TenantVectorWatcherService` fires its initial load at startup. If `OidMapWatcherService` hasn't loaded yet, `IOidMapService.EntryCount == 0`, and the validator skips MetricName checks. The registry loads with potentially invalid MetricNames.

**Why it happens:** Both watchers start concurrently as hosted services.

**How to avoid:** This is intentional by design — the validator already logs a warning when the OID map is empty. The operator must ensure OID map and tenant vector configs are consistent. No code change needed; document in a comment.

### Pitfall 4: Missing ConfigMap Deletion Handling

**What goes wrong:** No `Deleted` case in the WatchEventType switch. On ConfigMap deletion, the watch stream still sends an event; if unhandled, it falls through silently.

**Why it happens:** Omitting a case that feels unlikely.

**How to avoid:** Include explicit `else if (eventType is WatchEventType.Deleted)` block with a `LogWarning` matching the existing watchers' message. This is part of the verbatim copy.

### Pitfall 5: Forgetting CS0618 Pragma

**What goes wrong:** Build error or warning on `WatchAsync` call if pragma is omitted.

**Why it happens:** Easy to miss if copying selectively.

**How to avoid:** Copy the `#pragma disable CS0618` / `#pragma restore CS0618` block verbatim from OidMapWatcherService lines 95-98.

## Code Examples

### TenantVectorWatcherService Skeleton (verified pattern from codebase)

```csharp
// Source: OidMapWatcherService.cs + DeviceWatcherService.cs (verbatim pattern)
public sealed class TenantVectorWatcherService : BackgroundService
{
    internal const string ConfigMapName = "simetra-tenantvector";
    internal const string ConfigKey = "tenantvector.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IKubernetes _kubeClient;
    private readonly TenantVectorRegistry _registry;
    private readonly TenantVectorOptionsValidator _validator;
    private readonly ILogger<TenantVectorWatcherService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly string _namespace;

    // Constructor + ExecuteAsync (initial load + watch loop) +
    // LoadFromConfigMapAsync + HandleConfigMapChangedAsync + ReadNamespace()
    // all follow OidMapWatcherService exactly.
}
```

### HandleConfigMapChangedAsync — Validation Step (NEW vs existing watchers)

```csharp
// After JSON deserialization succeeds and options != null:
var validationResult = _validator.Validate(null, options);
if (validationResult.Failed)
{
    _logger.LogError(
        "TenantVector config validation failed for {ConfigMap}/{Key} -- skipping reload. Failures: {Failures}",
        ConfigMapName, ConfigKey,
        string.Join("; ", validationResult.Failures));
    return;
}

await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
try
{
    _registry.Reload(options);
    // TenantVectorRegistry.Reload() logs the structured diff internally.
    _logger.LogInformation(
        "TenantVectorWatcher reload complete for {ConfigMap}/{Key}: tenants={TenantCount}, slots={SlotCount}",
        ConfigMapName, ConfigKey, _registry.TenantCount, _registry.SlotCount);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Tenant vector reload failed -- previous config remains active");
}
finally
{
    _reloadLock.Release();
}
```

### DI Registration — Concrete-First Pattern for Validator

```csharp
// In AddSnmpConfiguration, replacing existing line 295:
// OLD: services.AddSingleton<IValidateOptions<TenantVectorOptions>, TenantVectorOptionsValidator>();
// NEW (concrete-first, same instance for both registrations):
services.AddSingleton<TenantVectorOptionsValidator>();
services.AddSingleton<IValidateOptions<TenantVectorOptions>>(
    sp => sp.GetRequiredService<TenantVectorOptionsValidator>());

// Then in IsInCluster() block:
services.AddSingleton<TenantVectorWatcherService>();
services.AddHostedService(sp => sp.GetRequiredService<TenantVectorWatcherService>());
```

### Local Dev Program.cs Block

```csharp
// Load tenant vector from tenantvector.json (after OID map and devices are loaded)
var tenantVectorPath = Path.Combine(configDir, "tenantvector.json");
if (File.Exists(tenantVectorPath))
{
    var tvJson = File.ReadAllText(tenantVectorPath);
    // tenantvector.json uses section wrapper { "TenantVector": { "Tenants": [...] } }
    // for IConfiguration binding; extract the inner object for direct deserialization.
    // OR: deserialize the full wrapper and navigate to .TenantVector, or load differently.
    // Simplest: deserialize as TenantVectorOptions directly if file uses bare format.
    // See Pitfall 1 — match the format used by the watcher ConfigMap (bare { "Tenants": [...] }).
}
```

**Note on local dev file format:** The existing `config/tenantvector.json` has the IConfiguration wrapper. Two options:
1. Change `config/tenantvector.json` to bare format `{ "Tenants": [...] }` and update IConfiguration section to bind differently
2. Keep `config/tenantvector.json` as-is for IConfiguration, and in the local dev block deserialize via `JsonDocument` navigating to `TenantVector` property

Since the file is already loaded by `AddJsonFile()` at lines 33-37 of Program.cs (IConfiguration path), the local dev Reload block should deserialize the same file. Easiest approach: use `JsonSerializer.Deserialize<TenantVectorOptions>` directly after extracting the `"TenantVector"` property:

```csharp
using var doc = JsonDocument.Parse(tvJson);
if (doc.RootElement.TryGetProperty("TenantVector", out var tvElement))
{
    var tvOptions = tvElement.Deserialize<TenantVectorOptions>(jsonOptions);
    // ... validate and reload
}
```

Or change `config/tenantvector.json` to bare format and add `"TenantVector"` section registration differently. The planner should choose one approach and be explicit.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| FileSystemWatcher for local dev hot-reload | Load-once in Program.cs (no FileSystemWatcher) | Phase 15 decision | Restart required in local dev — acceptable, already used for OID map and devices |
| Separate appsettings.json keys for all config | Separate ConfigMaps per config type (oidmaps, devices, tenantvector) | Phase 15 pattern | Independent watchers, each handles its own ConfigMap |

## Open Questions

1. **Local dev file format: bare vs. wrapped**
   - What we know: `config/tenantvector.json` currently has `{ "TenantVector": { "Tenants": [...] } }` wrapper for IConfiguration binding
   - What's unclear: Should the local dev Reload block use `JsonDocument` navigation to extract the inner object, or should the file be changed to bare format and the IConfiguration binding adjusted?
   - Recommendation: Use `JsonDocument` to extract the `TenantVector` property in the local dev block. This avoids changing the existing file format that IConfiguration already consumes. Keep `config/tenantvector.json` as-is. The ConfigMap's `tenantvector.json` value uses bare format `{ "Tenants": [...] }` (matching OID map and devices patterns). The planner should make this explicit in the task.

2. **Watcher log message for "reload complete" vs. relying on Reload() log**
   - What we know: `TenantVectorRegistry.Reload()` already logs `"TenantVectorRegistry reloaded: tenants=X, slots=Y, added=[...], removed=[...], unchanged=[...], carried_over=N"` at Information level
   - What's unclear: Should the watcher log an additional "reload complete" line, or trust that Reload()'s own log is sufficient?
   - Recommendation: Add a brief watcher-level log at Information after the `Reload()` call (e.g., `"TenantVectorWatcher reload triggered by {EventType} for {ConfigMap}"`). This provides the trigger context (K8s event type) that `Reload()` doesn't know about. The CONTEXT.md specifies: "Watcher logs the event trigger (ConfigMap Added/Modified) at Information level." Log the trigger before calling Reload(); no post-Reload log needed since Reload() provides the diff.

## Sources

### Primary (HIGH confidence)

- `src/SnmpCollector/Services/OidMapWatcherService.cs` — canonical pattern (228 lines, complete implementation)
- `src/SnmpCollector/Services/DeviceWatcherService.cs` — second canonical example, adds scheduler reconcile
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — Reload() signature, diff log format, volatile swap
- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — validate before Reload() call
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — IsInCluster() DI registration pattern
- `src/SnmpCollector/Program.cs` — local dev load-once block structure (lines 67-105)
- `deploy/k8s/production/configmap.yaml` — ConfigMap manifest format (simetra-oidmaps, simetra-devices patterns)
- `src/SnmpCollector/config/tenantvector.json` — existing local dev config file (wrapped format)

### Secondary (MEDIUM confidence)

None needed — all findings verified directly from codebase.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — KubernetesClient pattern directly observed in two existing watchers
- Architecture: HIGH — identical pattern already proven in OidMapWatcherService + DeviceWatcherService
- Pitfalls: HIGH — discovered from direct code inspection of existing DI registrations and file format analysis

**Research date:** 2026-03-10
**Valid until:** Until OidMapWatcherService or DeviceWatcherService are refactored (structural reference)
