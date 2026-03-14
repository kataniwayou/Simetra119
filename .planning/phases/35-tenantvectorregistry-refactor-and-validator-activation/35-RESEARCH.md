# Phase 35: TenantVectorRegistry Refactor & Validator Activation - Research

**Researched:** 2026-03-15
**Domain:** C# architectural refactor — registry/watcher separation, DI rewiring, test migration
**Confidence:** HIGH (all findings based on direct codebase inspection)

---

## Summary

Phase 35 is a pure architectural consistency refactor: move all validation and resolution logic out of both registries and into their respective watcher services. The target state is the same "watcher validates, registry stores" pattern already established by OidMapWatcherService and CommandMapWatcherService.

The codebase has been fully inspected. Both registries currently embed significant validation logic that belongs in the watchers. DeviceRegistry embeds CommunityString extraction, DNS resolution, duplicate IP+Port detection, duplicate CS warning, OID resolution, and zero-OID group filtering in both its constructor and `ReloadAsync`. TenantVectorRegistry embeds 6-step per-metric validation, 5-step per-command validation, IP resolution, and the TEN-13 completeness gate inside `Reload()`. Both registries hold cross-service dependencies (IOidMapService, IDeviceRegistry) solely for this validation work.

After Phase 35, registries are pure FrozenDictionary swap + index build operations. All validation log messages stay identical; they just move call sites to the watcher.

**Primary recommendation:** Implement in two sequential sub-tasks — DeviceWatcher/Registry refactor first (because TenantVectorWatcher depends on IDeviceRegistry, not TenantVectorRegistry), then TenantVectorWatcher/Registry refactor. Tests follow each refactor.

---

## Standard Stack

This is a codebase-internal refactor. No new libraries are introduced.

### Core (already in project)
| Component | Role | Notes |
|-----------|------|-------|
| `FrozenDictionary<K,V>` | Atomic volatile swap in registries | Pattern already established |
| `SemaphoreSlim` | Concurrent reload serialization | Already in both watchers |
| `NSubstitute` | Test mock framework | Already used in all test files |
| `xUnit` | Test framework | Already used |
| `IValidateOptions<T>` | Validator contract | Simplified, not replaced |

### No New Dependencies
No NuGet packages are added. This refactor reuses all existing types and patterns.

---

## Architecture Patterns

### Current State (to be refactored)

```
DeviceRegistry constructor/ReloadAsync(List<DeviceOptions>):
  1. TryExtractDeviceName → Error + skip if invalid
  2. DNS.GetHostAddresses (sync) / DNS.GetHostAddressesAsync (async) → resolve IP
  3. BuildPollGroups → calls IOidMapService.ResolveToOid → logs warnings + filters
  4. Duplicate IP+Port check → Error + skip
  5. Duplicate CommunityString check → Warning (both load)
  → FrozenDictionary swap

TenantVectorRegistry.Reload(TenantVectorOptions):
  1. Per-metric: structural (Ip, Port, MetricName), Role, OidMap.ContainsMetricName, DeviceRegistry.TryGetByIpPort, ResolveIp
  2. Per-command: structural (Ip, Port, CommandName), ValueType, Value, DeviceRegistry.TryGetByIpPort
  3. TEN-13 gate (≥1 Resolved + ≥1 Evaluate + ≥1 command)
  → FrozenDictionary swap + routing index build
```

### Target State (after Phase 35)

```
DeviceWatcherService.ValidateAndBuildDevices(List<DeviceOptions>, CancellationToken):
  [All validation logic moved here]
  → returns List<DeviceInfo>

DeviceRegistry.ReloadAsync(List<DeviceInfo>):
  [Pure store: build FrozenDictionaries + compute added/removed + notify]
  [No IOidMapService, no DNS, no validation]

DeviceRegistry constructor(List<DeviceInfo>, ILogger):
  [Same pure store logic]
  [No IOptions<DevicesOptions>, no IOidMapService]

TenantVectorWatcherService.ValidateAndBuildTenants(TenantVectorOptions):
  [All validation logic moved here]
  [Injects IOidMapService + IDeviceRegistry]
  → returns structured clean tenant data

TenantVectorRegistry.Reload(clean data):
  [Pure store: build groups + routing index]
  [No IDeviceRegistry, no IOidMapService, only ILogger]

TenantVectorRegistry constructor(ILogger):
  [No IDeviceRegistry, no IOidMapService]
```

### Pattern: ValidateAndBuild as Static/Internal Method

The OidMapWatcher and CommandMapWatcher both expose their core logic as `internal static` methods (`ValidateAndParseOidMap`, `ValidateAndParseCommandMap`). Program.cs local dev path calls these same static methods directly. This is the established pattern to follow.

For DeviceWatcherService: `ValidateAndBuildDevices` needs to be async (DNS resolution is async). It can be `internal` and called from both `HandleConfigMapChangedAsync` and from Program.cs local dev path.

For TenantVectorWatcherService: `ValidateAndBuildTenants` is synchronous (all lookups are in-memory). Can be `internal static` accepting explicit IOidMapService + IDeviceRegistry parameters (same injection pattern as CommandMapWatcherService.ValidateAndParseCommandMap accepts a logger parameter).

### Clean Data Shape for TenantVectorRegistry.Reload()

The watcher needs to pass pre-validated, pre-resolved tenant data to the registry. The most natural approach given the codebase:

**Option A (recommended):** Reuse existing types. The watcher builds `List<TenantOptions>` where all invalid entries have already been filtered out, and each MetricSlotOptions.Ip has already been resolved. Pass `TenantVectorOptions` with a filtered/resolved Tenants list. The registry's Reload() retains the same signature but can now trust the input is clean.

**Option B:** New DTO (e.g., `ValidatedTenantData`). Cleaner contract but adds a new type that only serves as a pass-through.

The CONTEXT.md leaves this as Claude's Discretion. Option A is lower friction — no new types, same `Reload(TenantVectorOptions)` signature but now treated as pre-validated data. The risk is that the registry's contract appears unchanged even though the semantics changed (no longer tolerates raw config). A doc comment update clarifies this.

### BuildPollGroups — Move or Keep?

`BuildPollGroups` in DeviceRegistry calls `IOidMapService.ResolveToOid`. The CONTEXT.md notes this is at Claude's Discretion. The cleanest split: move `BuildPollGroups` into `DeviceWatcherService.ValidateAndBuildDevices`. The registry constructor/ReloadAsync would then accept `List<DeviceInfo>` where `DeviceInfo.PollGroups` are already populated. This removes the last reason for DeviceRegistry to depend on IOidMapService.

Keeping BuildPollGroups in the registry would require either keeping `IOidMapService` injection or making it a static pure function that takes `IOidMapService` as a parameter. Moving it to the watcher is cleaner.

---

## Critical Interface Changes

### IDeviceRegistry.ReloadAsync signature change

**Current:** `Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceOptions> devices)`

**Target:** `Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceInfo> devices)`

This change ripples through:
1. `IDeviceRegistry` interface — update signature
2. `DeviceRegistry` implementation — update signature + remove validation logic
3. `DeviceWatcherService.HandleConfigMapChangedAsync` — now calls `ValidateAndBuildDevices()` first, then passes `List<DeviceInfo>` to `ReloadAsync`
4. `Program.cs` local dev path — now calls `ValidateAndBuildDevices()` first, then `ReloadAsync(deviceInfos)`
5. `DeviceRegistryTests` — the `ReloadAsync` tests currently pass `List<DeviceOptions>`; they need updating
6. `IDeviceRegistry` XML doc comment — update to reflect `List<DeviceInfo>` parameter

### DeviceRegistry constructor signature change

**Current:** `DeviceRegistry(IOptions<DevicesOptions> devicesOptions, IOidMapService oidMapService, ILogger<DeviceRegistry> logger)`

**Target:** `DeviceRegistry(List<DeviceInfo> initialDevices, ILogger<DeviceRegistry> logger)`

This change ripples through:
1. `ServiceCollectionExtensions.cs` — the DI registration `services.AddSingleton<IDeviceRegistry, DeviceRegistry>()` currently relies on DI injecting `IOptions<DevicesOptions>` and `IOidMapService` automatically. After refactor, we need explicit factory registration: `services.AddSingleton<IDeviceRegistry>(sp => new DeviceRegistry(new List<DeviceInfo>(), sp.GetRequiredService<ILogger<DeviceRegistry>>()))`
2. `DeviceRegistryTests.CreateRegistry()` — currently passes `IOptions<DevicesOptions>` + `IOidMapService`; will need to pass `List<DeviceInfo>` directly

### TenantVectorRegistry constructor signature change

**Current:** `TenantVectorRegistry(IDeviceRegistry deviceRegistry, IOidMapService oidMapService, ILogger<TenantVectorRegistry> logger)`

**Target:** `TenantVectorRegistry(ILogger<TenantVectorRegistry> logger)`

This change ripples through:
1. `ServiceCollectionExtensions.cs` — `new TenantVectorRegistry(sp.GetRequiredService<IDeviceRegistry>(), sp.GetRequiredService<IOidMapService>(), sp.GetRequiredService<ILogger<TenantVectorRegistry>>())` simplifies to `new TenantVectorRegistry(sp.GetRequiredService<ILogger<TenantVectorRegistry>>())`
2. `TenantVectorRegistryTests.CreateRegistry()` — currently passes `IDeviceRegistry` + `IOidMapService` mocks; the passthrough mocks are no longer needed
3. `TenantVectorWatcherService` — gains `IDeviceRegistry` + `IOidMapService` constructor injection

### TenantVectorWatcherService gains injected services

**Current constructor:** `(IKubernetes, TenantVectorRegistry, TenantVectorOptionsValidator, ILogger)`

**Target constructor:** `(IKubernetes, TenantVectorRegistry, IOidMapService, IDeviceRegistry, ILogger)` (validator removed or kept for structural-only check)

TenantVectorOptionsValidator is currently a no-op (always returns Success). The CONTEXT.md says it should check "JSON parsed correctly, required arrays exist, basic structural sanity." Since `TenantVectorOptions` has `Tenants = []` as default and deserialization ensures the object exists, a truly minimal validator may still be a no-op or check only `options.Tenants != null`. The watcher service field `_validator` and its DI registration may remain but become truly minimal.

---

## Test Impact Analysis

### Tests to be significantly rewritten

**`DeviceRegistryTests.cs`** (34 tests approximately):
- All constructor tests that pass `IOptions<DevicesOptions>` and `IOidMapService` must change to pass `List<DeviceInfo>`
- `CreateRegistry()` helper must build `DeviceInfo` objects directly instead of building from `DevicesOptions`
- `ReloadAsync` tests must pass `List<DeviceInfo>` instead of `List<DeviceOptions>`
- Tests for validation behavior (duplicate IP+Port, CommunityString extraction, zero-OID filtering, DNS resolution) **move** to new `DeviceWatcherValidationTests.cs`
- Tests for pure registry behavior (TryGetByIpPort, TryGetDeviceByName, AllDevices, FrozenDictionary swap) remain but with simplified setup

**`TenantVectorRegistryTests.cs`** (large file, ~53KB):
- `CreateRegistry()` helper drops the `IDeviceRegistry` and `IOidMapService` parameters
- `CreatePassthroughDeviceRegistry()` and `CreatePassthroughOidMapService()` helpers become unused in registry tests
- All tests that verify validation behavior (structural checks, Role validation, MetricName/OidMap check, IP+Port/DeviceRegistry check, TEN-13 gate) **move** to new `TenantVectorWatcherValidationTests.cs`
- Tests for pure registry behavior (slot carry-over, routing index, priority grouping, heartbeat, TryRoute, counts) remain — with simplified `Reload()` inputs (pre-validated data)

**`TenantVectorOptionsValidatorTests.cs`** (currently tests no-op validator):
- Already tests unconditional acceptance; may need minor updates if validator gains minimal structural checks

### New test files to create

**`DeviceWatcherValidationTests.cs`** (tests for `DeviceWatcherService.ValidateAndBuildDevices`):
- CommunityString extraction: valid, invalid (Error + skip)
- DNS resolution: IP passthrough, hostname resolution
- Duplicate IP+Port: Error + skip second
- Duplicate CommunityString different IP+Port: Warning + both load
- BuildPollGroups: resolved OIDs, unresolvable names (Warning), zero-OID group skip (Warning)
- Mixed poll groups: partial resolution

**`TenantVectorWatcherValidationTests.cs`** (tests for `TenantVectorWatcherService.ValidateAndBuildTenants`):
- Metric structural: empty Ip, port range, empty MetricName
- Role validation: Evaluate/Resolved valid, other invalid (Error + skip)
- OidMap check: ContainsMetricName true/false
- DeviceRegistry check: TryGetByIpPort true/false
- IP resolution: ConfigAddress -> ResolvedIp lookup via AllDevices
- Command structural: empty Ip, port range, empty CommandName
- ValueType validation: Integer32/IpAddress/OctetString valid, others invalid
- Empty Value validation
- DeviceRegistry check for commands
- CommandName: empty = Error + skip; non-empty but unresolvable = Debug log, pass through
- TEN-13 gate: missing Resolved, missing Evaluate, missing commands

---

## DI Registration Changes (ServiceCollectionExtensions.cs)

### DeviceRegistry registration change

```csharp
// BEFORE:
services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
// DeviceRegistry constructor auto-injected: IOptions<DevicesOptions>, IOidMapService, ILogger

// AFTER:
services.AddSingleton<IDeviceRegistry>(sp =>
    new DeviceRegistry(
        new List<DeviceInfo>(),
        sp.GetRequiredService<ILogger<DeviceRegistry>>()));
```

The DevicesOptions binding can remain (it's still used by AddSnmpScheduling for Quartz job setup) but is no longer injected into DeviceRegistry.

### TenantVectorRegistry registration change

```csharp
// BEFORE:
services.AddSingleton<TenantVectorRegistry>(sp =>
    new TenantVectorRegistry(
        sp.GetRequiredService<IDeviceRegistry>(),
        sp.GetRequiredService<IOidMapService>(),
        sp.GetRequiredService<ILogger<TenantVectorRegistry>>()));

// AFTER:
services.AddSingleton<TenantVectorRegistry>(sp =>
    new TenantVectorRegistry(
        sp.GetRequiredService<ILogger<TenantVectorRegistry>>()));
```

### TenantVectorWatcherService registration change

The watcher is registered as singleton; DI will auto-inject the new `IOidMapService` and `IDeviceRegistry` constructor parameters since both are already registered as singletons.

---

## Program.cs Local Dev Path Changes

```csharp
// BEFORE (devices):
var devices = JsonSerializer.Deserialize<List<DeviceOptions>>(devicesJson, jsonOptions);
if (devices != null)
{
    var deviceRegistry = app.Services.GetRequiredService<IDeviceRegistry>();
    await deviceRegistry.ReloadAsync(devices);
    ...
}

// AFTER (devices):
var rawDevices = JsonSerializer.Deserialize<List<DeviceOptions>>(devicesJson, jsonOptions);
if (rawDevices != null)
{
    var deviceWatcher = app.Services.GetRequiredService<DeviceWatcherService>();
    var deviceInfos = await deviceWatcher.ValidateAndBuildDevicesAsync(rawDevices, CancellationToken.None);
    var deviceRegistry = app.Services.GetRequiredService<IDeviceRegistry>();
    await deviceRegistry.ReloadAsync(deviceInfos);
    ...
}
```

Note: If `ValidateAndBuildDevices` is `internal static`, Program.cs can call it as `DeviceWatcherService.ValidateAndBuildDevicesAsync(rawDevices, oidMapService, logger, ct)`. This matches the CommandMapWatcher pattern where `ValidateAndParseCommandMap` is `internal static` and takes explicit parameters.

```csharp
// BEFORE (tenant vector):
var tvValidator = app.Services.GetRequiredService<TenantVectorOptionsValidator>();
var tvValidation = tvValidator.Validate(null, tvOptions);
if (!tvValidation.Failed)
{
    var tvRegistry = app.Services.GetRequiredService<TenantVectorRegistry>();
    tvRegistry.Reload(tvOptions);
}

// AFTER (tenant vector):
var tvWatcher = app.Services.GetRequiredService<TenantVectorWatcherService>();
// OR call static method directly:
var cleanTenants = TenantVectorWatcherService.ValidateAndBuildTenants(tvOptions, oidMapService, deviceRegistry, logger);
var tvRegistry = app.Services.GetRequiredService<TenantVectorRegistry>();
tvRegistry.Reload(cleanTenants);
```

---

## DevicesOptionsValidator Simplification

**Current:** Validates CommunityString format, IpAddress validity (rejects DNS names), Port range, Poll IntervalSeconds, MetricNames non-empty, TimeoutMultiplier range, duplicate IP+Port detection.

**Target after Phase 35:** Minimal structural sanity only. The per-entry validation (CommunityString, DNS resolution, duplicates, OID resolution) moves entirely to the watcher. The validator should check:
- `options` is not null (implied by framework)
- `options.Devices` is not null (already defaulted to `[]`)

In practice the validator may become as minimal as the TenantVectorOptionsValidator (always Success), since DevicesOptions still binds from appsettings for Quartz scheduling purposes (AddSnmpScheduling reads it at build time), not from ConfigMap JSON.

**Key insight:** The validator fires at startup against appsettings-bound options (local dev or initial static config). Since K8s uses ConfigMap watchers for actual device config, the validator on `IOptions<DevicesOptions>` primarily protects local dev appsettings.json. Removing the duplicate-detection and per-entry validation from the validator means local dev may get runtime Error logs rather than startup failures — this is acceptable per the CONTEXT.md decision (all per-entry validation lives in the watcher's ValidateAndBuild method).

---

## Common Pitfalls

### Pitfall 1: Constructor Bootstrap Order in K8s Mode

**What goes wrong:** In K8s mode, DeviceWatcherService starts as a BackgroundService and calls `HandleConfigMapChangedAsync` asynchronously. If DeviceRegistry constructor is now empty (starts with `List<DeviceInfo>()`), the registry is empty until the watcher fires. This is the expected behavior — same as how OidMapService starts empty.

**Why it matters:** Any code that reads `IDeviceRegistry.AllDevices` or calls `TryGetByIpPort` before the initial load completes will see empty results. This was already possible with the prior design; now it's explicit.

**How to avoid:** This is by design. The startup health check pattern (ReadinessHealthCheck) already handles this.

### Pitfall 2: DevicesOptions Still Needed for Quartz Scheduling

**What goes wrong:** `AddSnmpScheduling` reads `DevicesOptions` at build time to register Quartz poll jobs and calculate thread pool size. Removing `IOptions<DevicesOptions>` injection from `DeviceRegistry` does not remove the need to bind `DevicesOptions` in DI.

**How to avoid:** Keep the `services.AddOptions<DevicesOptions>()` binding and `DevicesOptionsValidator` registration in ServiceCollectionExtensions. Only remove the injection into `DeviceRegistry`. The validator simplification is separate from the binding removal.

### Pitfall 3: Test Helper CreateRegistry() Signature Change Cascade

**What goes wrong:** `TenantVectorRegistryTests.CreateRegistry()` currently creates passthrough mocks for `IDeviceRegistry` and `IOidMapService`. After refactor, these mocks are no longer needed by the registry. However, if any test is testing validation behavior through the registry's `Reload()` (which moves to the watcher), those tests need to be moved rather than simply updated.

**How to avoid:** Audit every test that currently exercises validation paths. Move validation tests wholesale to the new WatcherValidation test files. Keep pure registry tests (routing index, carry-over, priority grouping) in the original test files with simplified setup.

### Pitfall 4: Async vs Sync for ValidateAndBuildDevices

**What goes wrong:** DNS resolution is async (`Dns.GetHostAddressesAsync`). The watcher's `ValidateAndBuildDevices` must be `async Task<List<DeviceInfo>>`. If it is made `static`, it cannot use `_logger` as an instance field — it must accept a logger parameter (matching CommandMapWatcher's `internal static` pattern with explicit logger parameter).

**How to avoid:** Signature: `internal static async Task<List<DeviceInfo>> ValidateAndBuildDevicesAsync(List<DeviceOptions> devices, IOidMapService oidMapService, ILogger logger, CancellationToken ct)`. Program.cs resolves `IOidMapService` and `ILogger<DeviceWatcherService>` directly from the DI container for the local dev call.

### Pitfall 5: TenantVectorRegistry.Reload() Parameter Type Decision

**What goes wrong:** If the target `Reload()` still accepts `TenantVectorOptions` (same type), the code appears unchanged but the semantics have flipped (input is now pre-validated). A caller who passes raw unvalidated `TenantVectorOptions` would bypass all validation silently.

**How to avoid:** Either (a) rename the method to `ReloadValidated(TenantVectorOptions options)` or use a clear XML doc update, or (b) create a thin wrapper type `ValidatedTenantVectorOptions` to make the semantic change visible at the type level. Given the team pattern of using plain types, a clear doc comment on the method is sufficient and lower friction.

### Pitfall 6: ResolveIp Logic in TenantVectorRegistry

`TenantVectorRegistry.ResolveIp()` iterates `_deviceRegistry.AllDevices` to map ConfigAddress to ResolvedIp. After refactor, this logic moves to `TenantVectorWatcherService.ValidateAndBuildTenants()`. The watcher already has IDeviceRegistry injected. `ResolveIp` is a private method and can simply be deleted from TenantVectorRegistry. The resolved IP is written into the MetricSlotOptions or into the MetricSlotHolder directly by the watcher before passing to the registry.

---

## Code Examples

### ValidateAndBuildDevicesAsync Pattern (matches CommandMapWatcher pattern)

```csharp
// In DeviceWatcherService — mirrors CommandMapWatcherService.ValidateAndParseCommandMap pattern
internal static async Task<List<DeviceInfo>> ValidateAndBuildDevicesAsync(
    List<DeviceOptions> devices,
    IOidMapService oidMapService,
    ILogger logger,
    CancellationToken ct)
{
    var byIpPortSeen = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
    var seenCommunityStrings = new Dictionary<string, string>(StringComparer.Ordinal);
    var result = new List<DeviceInfo>(devices.Count);

    foreach (var d in devices)
    {
        // 1. CommunityString extraction
        if (!CommunityStringHelper.TryExtractDeviceName(d.CommunityString, out var deviceName)) { /* Error log + continue */ }

        // 2. DNS resolution (async)
        var ip = await ResolveIpAsync(d.IpAddress, ct).ConfigureAwait(false);

        // 3. BuildPollGroups (moved from registry)
        var pollGroups = BuildPollGroups(d.Polls, deviceName, oidMapService, logger);

        // 4. Duplicate IP+Port check
        var ipPortKey = $"{d.IpAddress}:{d.Port}";
        if (byIpPortSeen.ContainsKey(ipPortKey)) { /* Error log + continue */ }

        // 5. Duplicate CommunityString warning
        if (seenCommunityStrings.TryGetValue(d.CommunityString, out var priorName)) { /* Warning log */ }
        seenCommunityStrings.TryAdd(d.CommunityString, deviceName);

        var info = new DeviceInfo(deviceName, d.IpAddress, ip, d.Port, pollGroups, d.CommunityString);
        byIpPortSeen[ipPortKey] = info;
        result.Add(info);
    }
    return result;
}
```

### DeviceRegistry.ReloadAsync Pure Store Pattern

```csharp
public async Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceInfo> devices)
{
    var oldKeys = new HashSet<string>(_byIpPort.Keys, StringComparer.OrdinalIgnoreCase);

    var byIpPortBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);
    var byNameBuilder = new Dictionary<string, DeviceInfo>(devices.Count, StringComparer.OrdinalIgnoreCase);

    foreach (var info in devices)
    {
        var ipPortKey = IpPortKey(info.ConfigAddress, info.Port);
        byIpPortBuilder[ipPortKey] = info;
        byNameBuilder[info.Name] = info;
    }

    _byIpPort = byIpPortBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    _byName = byNameBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // ... added/removed computation + log
    return (added, removed);
}
```

### TenantVectorWatcherService HandleConfigMapChangedAsync Pattern

```csharp
private async Task HandleConfigMapChangedAsync(V1ConfigMap configMap, CancellationToken ct)
{
    // ... parse JSON to TenantVectorOptions ...

    // Validate structure (minimal validator -- options already deserialized OK)
    var validationResult = _validator.Validate(null, options);
    if (validationResult.Failed) { /* log + return */ }

    // ValidateAndBuild: all per-entry validation happens here
    var cleanOptions = ValidateAndBuildTenants(options, _oidMapService, _deviceRegistry, _logger);

    await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        _registry.Reload(cleanOptions);
    }
    finally { _reloadLock.Release(); }
}
```

---

## Sequencing Recommendation

Given that TenantVectorWatcher depends on IDeviceRegistry (cross-service), the refactor order is:

1. **DeviceWatcher + DeviceRegistry refactor** — move validation to watcher, clean registry
2. **DeviceWatcher + DeviceRegistry tests** — new DeviceWatcherValidationTests + updated DeviceRegistryTests
3. **TenantVectorWatcher + TenantVectorRegistry refactor** — move validation to watcher (which already has IDeviceRegistry), clean registry
4. **TenantVectorWatcher + TenantVectorRegistry tests** — new TenantVectorWatcherValidationTests + updated TenantVectorRegistryTests
5. **Validator simplification** — DevicesOptionsValidator minimal, TenantVectorOptionsValidator confirm still minimal

All five steps can map to separate PLAN sub-tasks or grouped into 2-3 tasks depending on planning preference.

---

## Open Questions

1. **ValidatedTenantVectorOptions vs reusing TenantVectorOptions for Reload()**
   - What we know: Reload() currently accepts TenantVectorOptions; after refactor, input is pre-validated
   - What's unclear: Whether type safety is desired at the method boundary
   - Recommendation: Keep TenantVectorOptions as parameter type, rename method to `ReloadPreValidated` or add prominent XML doc; avoid new DTO type unless planner chooses to add it

2. **DevicesOptionsValidator post-refactor scope**
   - What we know: Currently validates CommunityString format, IP format, Port range, Poll structure, duplicate IP+Port — all of which are moving to the watcher
   - What's unclear: Whether the validator should retain IP-format validation (rejects DNS names that the watcher resolves) or become fully no-op
   - Recommendation: Simplify to check only that `options` is not null and `options.Devices` is not null (both always true from default initialization); effectively make it a no-op like TenantVectorOptionsValidator

3. **CommunityStringHelper visibility**
   - What we know: Currently `internal` to the Pipeline namespace; DeviceWatcherService is in Services namespace
   - What's unclear: Whether moving validation to the watcher requires making CommunityStringHelper accessible from Services
   - Recommendation: Change `CommunityStringHelper` from `internal static` to `internal static` but in a shared namespace, or move it to a utility class, or make it `public internal` — easiest is to move it to the Configuration namespace or make it public. The simplest fix: make the class `public` or move `ValidateAndBuildDevicesAsync` into a class in the Pipeline namespace that can access it.

---

## Sources

### Primary (HIGH confidence)
- Direct file inspection: `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — full validation logic catalogued
- Direct file inspection: `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — full validation logic catalogued
- Direct file inspection: `src/SnmpCollector/Services/DeviceWatcherService.cs` — current watcher state
- Direct file inspection: `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — current watcher state
- Direct file inspection: `src/SnmpCollector/Services/CommandMapWatcherService.cs` — reference pattern for ValidateAndParse static method
- Direct file inspection: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — DI registration details
- Direct file inspection: `src/SnmpCollector/Program.cs` — local dev path details
- Direct file inspection: `src/SnmpCollector/Pipeline/IDeviceRegistry.cs` — interface to change
- Direct file inspection: `src/SnmpCollector/Pipeline/ITenantVectorRegistry.cs` — interface (unchanged)
- Direct file inspection: `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` — test impact analysis
- Direct file inspection: `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` — test impact analysis
- Direct file inspection: `tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs` — validator test state
- Direct file inspection: `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — current validator logic
- Direct file inspection: `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — current no-op validator

---

## Metadata

**Confidence breakdown:**
- Standard Stack: HIGH — no new libraries; pure codebase patterns
- Architecture Patterns: HIGH — based on full source inspection + existing OidMapWatcher/CommandMapWatcher reference patterns
- Interface Changes: HIGH — exact signatures read from source
- Test Impact: HIGH — test files fully inspected
- Pitfalls: HIGH — based on actual code structure, not speculation

**Research date:** 2026-03-15
**Valid until:** Indefinite (codebase-internal; not dependent on external library versions)
