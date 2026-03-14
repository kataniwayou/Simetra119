# Phase 33: Config Model Additions - Research

**Researched:** 2026-03-14
**Domain:** C# configuration model layer — options classes, runtime records, validators, config JSON files
**Confidence:** HIGH (all findings from direct codebase inspection)

---

## Summary

Phase 33 is a pure model-layer addition phase. It adds new C# types and fields to the configuration and pipeline layers that v1.7 requires — specifically: rename `DeviceOptions.Name` to `DeviceOptions.CommunityString`, derive `DeviceInfo.Name` from the community string at load time, add `CommandSlotOptions` as a new config type, add `Commands[]` to `TenantOptions`, add optional `IntervalSeconds` to `MetricSlotOptions`, and add optional `Name` to `TenantOptions`.

The primary technical challenge is that `DeviceOptions.Name` is currently referenced in many downstream consumers (DeviceRegistry, DevicesOptionsValidator, DeviceRegistryTests, DeviceWatcherService, e2e fixtures, K8s ConfigMaps). The rename of the JSON field is a clean break — all config files must be updated atomically. The `DeviceInfo` record currently uses `Name` as the identity key for `_byName`; under the new model that field is derived from `CommunityString` via `CommunityStringHelper.TryExtractDeviceName()`, which already exists.

The `TenantVectorRegistry` currently derives `IntervalSeconds` from `IOidMapService` via a lookup through device poll groups. After this phase, `IntervalSeconds` comes directly from config (`MetricSlotOptions.IntervalSeconds`), which means `TenantVectorRegistry` can drop its `IOidMapService` dependency. This is a significant structural change that affects DI wiring.

**Primary recommendation:** Execute in three tightly-coupled sub-tasks: (1) rename DeviceOptions/DeviceInfo and update all consumers, (2) add CommandSlotOptions + TenantOptions.Commands + TenantOptions.Name + MetricSlotOptions.IntervalSeconds, (3) update all config files atomically.

---

## Standard Stack

This phase uses no new libraries — all work is within the existing codebase.

### Core (existing, in use)
| Component | Location | Purpose |
|-----------|----------|---------|
| `sealed class` options | `src/SnmpCollector/Configuration/` | Config options shape — all existing options use `sealed class` with `{ get; set; }` properties |
| `sealed record` | `src/SnmpCollector/Pipeline/DeviceInfo.cs` | Immutable runtime representation built from options at load time |
| `IValidateOptions<T>` | `src/SnmpCollector/Configuration/Validators/` | Manual deep-graph validation — `DevicesOptionsValidator` validates nested `DeviceOptions` |
| `System.Text.Json` | `DeviceWatcherService`, `TenantVectorWatcherService` | JSON deserialization from ConfigMaps, uses `PropertyNameCaseInsensitive = true` |
| `CommunityStringHelper` | `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` | `TryExtractDeviceName(string, out string)` — already exists, already used in trap listener |
| `FrozenDictionary` | `DeviceRegistry` | `_byName` keyed on short device name |

---

## Architecture Patterns

### Current DeviceOptions.Name flow (to be replaced)

```
devices.json: { "Name": "NPB-01", ... }
    -> DeviceOptions.Name = "NPB-01"
    -> DeviceRegistry builds DeviceInfo(Name: "NPB-01", ...)
    -> _byName["NPB-01"] = info
    -> MetricPollJob uses device.Name for SNMP community: CommunityStringHelper.DeriveFromDeviceName(device.Name)
    -> DevicesOptionsValidator validates Name is non-empty
```

### New DeviceOptions.CommunityString flow (target)

```
devices.json: { "CommunityString": "Simetra.NPB-01", ... }
    -> DeviceOptions.CommunityString = "Simetra.NPB-01"
    -> DeviceRegistry calls CommunityStringHelper.TryExtractDeviceName("Simetra.NPB-01", out name)
       -> name = "NPB-01"
    -> DeviceInfo(Name: "NPB-01", CommunityString: "Simetra.NPB-01", ...)
    -> _byName["NPB-01"] = info  (unchanged — downstream consumers unaffected)
    -> MetricPollJob uses device.CommunityString directly (no derivation)
    -> DevicesOptionsValidator validates CommunityString is non-empty and follows Simetra.* convention
```

### CommandSlotOptions (new type)

Per decisions, this is a new config type for tenant command entries. All existing config types in `Configuration/` use `sealed class` with `{ get; set; }` mutable properties (matching `MetricSlotOptions` pattern). Use the same style:

```csharp
// src/SnmpCollector/Configuration/CommandSlotOptions.cs
public sealed class CommandSlotOptions
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public string CommandName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
}
```

Using `sealed class` (not `record`) is consistent with every other options class in the project: `MetricSlotOptions`, `PollOptions`, `TenantOptions`, `DeviceOptions`, etc. The discretion to choose is noted but the codebase pattern is unambiguous — all options types are mutable classes.

### TenantOptions additions (additive)

Current `TenantOptions` has `Priority` and `Metrics`. Phase 33 adds `Commands`, `Name`:

```csharp
public sealed class TenantOptions
{
    public int Priority { get; set; }
    public string? Name { get; set; }          // NEW: optional, null = auto-generate "tenant-{index}"
    public List<MetricSlotOptions> Metrics { get; set; } = [];
    public List<CommandSlotOptions> Commands { get; set; } = [];  // NEW: absent = empty list
}
```

### MetricSlotOptions addition (additive)

```csharp
public int IntervalSeconds { get; set; } = 0;  // NEW: optional, default 0 = unspecified
```

### DeviceInfo record change

Current:
```csharp
public sealed record DeviceInfo(
    string Name,
    string ConfigAddress,
    string ResolvedIp,
    int Port,
    IReadOnlyList<MetricPollInfo> PollGroups,
    string? CommunityString = null);
```

The `CommunityString` field currently is optional (nullable, default null). Under the new model:
- `Name` stays in the record (derived from CommunityString at load time in DeviceRegistry)
- `CommunityString` becomes required (non-nullable, always populated from config)

New shape:
```csharp
public sealed record DeviceInfo(
    string Name,             // derived: CommunityStringHelper.TryExtractDeviceName(CommunityString)
    string ConfigAddress,
    string ResolvedIp,
    int Port,
    IReadOnlyList<MetricPollInfo> PollGroups,
    string CommunityString); // required: stored as-is from DeviceOptions.CommunityString
```

Removing the default parameter value means all callsites constructing `DeviceInfo` must be updated.

### DeviceRegistry changes

Two build paths both need updating:

1. **Constructor** (`DeviceRegistry(IOptions<DevicesOptions>, ...)`)
2. **`ReloadAsync(List<DeviceOptions>)`**

In both paths, replace:
```csharp
var info = new DeviceInfo(d.Name, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);
var pollGroups = BuildPollGroups(d.Polls, d.Name);
```
With:
```csharp
if (!CommunityStringHelper.TryExtractDeviceName(d.CommunityString, out var deviceName))
{
    _logger.LogError("Device[{Index}].CommunityString '{CommunityString}' does not follow Simetra.{{name}} convention -- skipping",
        index, d.CommunityString);
    continue;
}
var pollGroups = BuildPollGroups(d.Polls, deviceName);
var info = new DeviceInfo(deviceName, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);
```

Note: `_byName` key stays as `deviceName` (short name like "NPB-01") — all downstream consumers (MetricPollJob, trap listener, DynamicPollScheduler) use `device.Name` unchanged.

### TenantVectorRegistry changes — drop IOidMapService dependency

Currently `TenantVectorRegistry` has:
- `private readonly IOidMapService _oidMapService;`
- `DeriveIntervalSeconds(string ip, int port, string metricName)` method that looks up through poll groups

After this phase, `MetricSlotOptions.IntervalSeconds` is the source of truth. The `DeriveIntervalSeconds` method is deleted entirely. In `Reload()`:

```csharp
// OLD:
var derivedInterval = DeriveIntervalSeconds(metric.Ip, metric.Port, metric.MetricName);
var newHolder = new MetricSlotHolder(resolvedIp, metric.Port, metric.MetricName, derivedInterval, metric.TimeSeriesSize);

// NEW:
var newHolder = new MetricSlotHolder(resolvedIp, metric.Port, metric.MetricName, metric.IntervalSeconds, metric.TimeSeriesSize);
```

Constructor signature changes:
```csharp
// OLD:
public TenantVectorRegistry(
    IDeviceRegistry deviceRegistry,
    IOidMapService oidMapService,
    ILogger<TenantVectorRegistry> logger)

// NEW:
public TenantVectorRegistry(
    IDeviceRegistry deviceRegistry,
    ILogger<TenantVectorRegistry> logger)
```

Impact on `TenantVectorRegistryTests`: `CreateRegistry()` currently passes `Substitute.For<IOidMapService>()`. That argument is removed. All tests that set up IOidMapService stubs in this test file must be updated.

### MetricPollJob changes — use CommunityString directly

Current:
```csharp
var communityStr = !string.IsNullOrEmpty(device.CommunityString)
    ? device.CommunityString
    : CommunityStringHelper.DeriveFromDeviceName(device.Name);
```

After rename, `DeviceInfo.CommunityString` is always non-null and non-empty (guaranteed by DeviceRegistry load), so:
```csharp
var communityStr = device.CommunityString;
```

The derivation fallback is gone because the clean break means CommunityString is always present.

### DevicesOptionsValidator changes

The validator currently checks `device.Name` is non-empty. After the rename:
- Remove check for `device.Name`
- Add check for `device.CommunityString` is non-empty and follows `Simetra.` convention
- Add check that `CommunityStringHelper.TryExtractDeviceName(device.CommunityString, out _)` succeeds

### TenantOptions.Name usage in TenantVectorRegistry

Currently `tenantId = $"tenant-{i}"`. With `Name` optional:
```csharp
var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name)
    ? tenantOpts.Name
    : $"tenant-{i}";
```

---

## Complete File Change Inventory

### New files to create
| File | Type | Content |
|------|------|---------|
| `src/SnmpCollector/Configuration/CommandSlotOptions.cs` | New | CommandSlotOptions class |

### C# files to modify
| File | Change |
|------|--------|
| `Configuration/DeviceOptions.cs` | Remove `Name` property; `CommunityString` becomes required non-nullable |
| `Pipeline/DeviceInfo.cs` | `CommunityString` becomes non-nullable (no default); `Name` stays (derived at load) |
| `Pipeline/DeviceRegistry.cs` | Extract `deviceName` from `CommunityString` in both constructor and `ReloadAsync`; update `DeviceInfo` construction |
| `Configuration/MetricSlotOptions.cs` | Add `IntervalSeconds` property (int, default 0) |
| `Configuration/TenantOptions.cs` | Add `Name?` and `Commands` properties |
| `Pipeline/TenantVectorRegistry.cs` | Remove `IOidMapService` field and `DeriveIntervalSeconds`; use `metric.IntervalSeconds` directly; use `tenantOpts.Name` for ID; constructor signature change |
| `Jobs/MetricPollJob.cs` | Remove community string derivation fallback |
| `Configuration/Validators/DevicesOptionsValidator.cs` | Replace Name validation with CommunityString validation |
| `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` | Update all `DeviceOptions` constructions to use `CommunityString` instead of `Name` |
| `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` | Remove `IOidMapService` from `CreateRegistry()`; add tests for `Name` field and `IntervalSeconds` |
| `tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs` | Add tests for `Commands` field |
| `tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs` | Update community string test setup |

### Config JSON files to update (atomic with code changes)
| File | Change |
|------|--------|
| `src/SnmpCollector/config/devices.json` | All entries: remove `"Name"`, add `"CommunityString": "Simetra.{OldName}"` |
| `src/SnmpCollector/appsettings.Development.json` | Same rename in Devices entries |
| `deploy/k8s/snmp-collector/simetra-devices.yaml` | Same rename |
| `deploy/k8s/production/configmap.yaml` | Both `simetra-devices` entries |
| `tests/e2e/fixtures/device-added-configmap.yaml` | Same rename |
| `tests/e2e/fixtures/device-removed-configmap.yaml` | Check and update if present |
| `tests/e2e/fixtures/device-modified-interval-configmap.yaml` | Check and update if present |
| `tests/e2e/fixtures/fake-device-configmap.yaml` | Check and update if present |
| `tests/e2e/fixtures/invalid-json-devices-schema-configmap.yaml` | May need update |

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| CommunityString extraction | Custom string parsing | `CommunityStringHelper.TryExtractDeviceName()` — already exists |
| DeviceInfo construction | Ad-hoc string manipulation | Consistent pattern in both `DeviceRegistry` constructor and `ReloadAsync` |

---

## Common Pitfalls

### Pitfall 1: Partial rename — only updating DeviceOptions but not validators
**What goes wrong:** Compilation succeeds but `DevicesOptionsValidator` still references `device.Name`, causing either a compile error or (if the field was kept as nullable) silent acceptance of old config.
**How to avoid:** Remove `Name` entirely from `DeviceOptions`. The compile errors will find every reference that needs updating.
**Warning signs:** Any remaining use of `.Name` on a `DeviceOptions` instance.

### Pitfall 2: Missing CommunityString in existing e2e fixture ConfigMaps
**What goes wrong:** E2e fixtures still use old `"Name"` field format, causing device entries to be silently skipped (empty CommunityString fails validation/extraction) during e2e runs.
**How to avoid:** Grep all YAML fixture files for `"Name"` in device contexts and update atomically. The `device-added-configmap.yaml` currently has `"Name": "OBP-01"` — all entries need the rename.
**Warning signs:** E2e tests for device add/remove/modify-interval scenarios fail with "device not found" or zero poll executions.

### Pitfall 3: TenantVectorRegistry IOidMapService not fully removed
**What goes wrong:** If the DI registration is left with IOidMapService injected but the constructor no longer accepts it, startup fails. Or vice versa — constructor removed but DI wiring not updated.
**How to avoid:** Check `ServiceCollectionExtensions.cs` and `Program.cs` for DI registration of TenantVectorRegistry. The constructor signature change is the anchor — fix DI wiring alongside.
**Warning signs:** `InvalidOperationException: Unable to resolve service for type IOidMapService` on startup, or constructor argument mismatch.

### Pitfall 4: DeviceInfo record positional parameter order confusion
**What goes wrong:** `DeviceInfo` is a positional record. Changing `CommunityString` from optional (last, with default) to required changes the call signature. Any test or helper that constructs `DeviceInfo` directly using positional syntax will fail to compile.
**How to avoid:** Search for `new DeviceInfo(` across tests and update. The primary construction points are in `DeviceRegistry` (constructor + ReloadAsync) — but tests may construct directly.
**Warning signs:** Compile errors at `DeviceInfo` construction sites with "wrong number of arguments" or "cannot convert null literal".

### Pitfall 5: TenantVectorRegistry tests with IOidMapService stubs
**What goes wrong:** `TenantVectorRegistryTests.CreateRegistry()` currently injects `Substitute.For<IOidMapService>()`. After removing that dependency, this test helper still compiles if IOidMapService is unused but will fail at runtime if the stub's `Resolve()` or `ResolveToOid()` is set up in individual tests.
**How to avoid:** Remove the IOidMapService parameter from `CreateRegistry()` and check every `[Fact]` in `TenantVectorRegistryTests` for any IOidMapService setup that can be deleted.

### Pitfall 6: IntervalSeconds default 0 breaks existing TenantVectorRegistry logic
**What goes wrong:** If any existing code downstream checks `IntervalSeconds > 0` before using it, supplying 0 may be silently dropped or cause divide-by-zero. The old `DeriveIntervalSeconds` already returned 0 as fallback, so 0 is an established "no interval specified" value.
**How to avoid:** Verify `MetricSlotHolder` constructor accepts 0 for `interalSeconds`. Current code path already uses 0 as returned from `DeriveIntervalSeconds` when device not found, so this is safe.

---

## Code Examples

### DevicesOptionsValidator — CommunityString validation (target pattern)

```csharp
// Source: inferred from existing DevicesOptionsValidator + CommunityStringHelper
private static void ValidateDevice(DeviceOptions device, int index, List<string> failures)
{
    if (string.IsNullOrWhiteSpace(device.CommunityString))
    {
        failures.Add($"Devices[{index}].CommunityString is required");
    }
    else if (!CommunityStringHelper.TryExtractDeviceName(device.CommunityString, out _))
    {
        failures.Add(
            $"Devices[{index}].CommunityString '{device.CommunityString}' " +
            $"does not follow Simetra.{{DeviceName}} convention");
    }
    // ... rest unchanged
}
```

### TenantVectorRegistry.Reload — IntervalSeconds from config (target pattern)

```csharp
// Source: TenantVectorRegistry.cs Reload() method
foreach (var metric in tenantOpts.Metrics)
{
    var resolvedIp = ResolveIp(metric.Ip);
    var newHolder = new MetricSlotHolder(
        resolvedIp,
        metric.Port,
        metric.MetricName,
        metric.IntervalSeconds,   // directly from config, no derivation
        metric.TimeSeriesSize);
    // ...
}
```

### TenantOptions.Name usage in TenantVectorRegistry

```csharp
// In TenantVectorRegistry.Reload():
var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name)
    ? tenantOpts.Name
    : $"tenant-{i}";
var tenant = new Tenant(tenantId, tenantOpts.Priority, holders);
```

### JSON shape — devices.json after rename

```json
[
  {
    "CommunityString": "Simetra.OBP-01",
    "IpAddress": "127.0.0.1",
    "Port": 10161,
    "Polls": [...]
  }
]
```

### JSON shape — tenantvector.json after additions

```json
{
  "Tenants": [
    {
      "Priority": 1,
      "Name": "npb-core-tenant",
      "Metrics": [
        { "Ip": "...", "Port": 161, "MetricName": "npb_cpu_util", "IntervalSeconds": 10 }
      ],
      "Commands": [
        { "Ip": "...", "Port": 161, "CommandName": "npb_reset_counters_P1", "Value": "1", "ValueType": "Integer32" }
      ]
    }
  ]
}
```

---

## Key Observations on Existing State

### DeviceOptions already has CommunityString (optional)

Looking at the current `DeviceOptions.cs`, there is **already** an optional `CommunityString` property (`string?`) alongside `Name`. The current behavior: if CommunityString is null/empty, `MetricPollJob` falls back to `CommunityStringHelper.DeriveFromDeviceName(device.Name)`.

Phase 33 inverts this: `CommunityString` becomes the primary required field; `Name` is removed and derived from `CommunityString` instead. This is a cleaner model change than a net-new property addition.

### DeviceInfo already has CommunityString (optional)

`DeviceInfo` already has `string? CommunityString = null` as the last (optional) positional parameter. Phase 33 makes it non-nullable and required, and makes `Name` derived rather than passed directly. Positional records require all callers to be updated when optional-with-default becomes required.

### TenantVectorRegistry already has IDeviceRegistry for CommunityString lookup

The existing `ResolveIp` method in `TenantVectorRegistry` already uses `_deviceRegistry.AllDevices` to match config addresses to resolved IPs. The context decision says "TenantVectorRegistry keeps IDeviceRegistry dependency for CommunityString lookup by IP+Port". So `ResolveIp` stays, but a new method `LookupCommunityString(string ip, int port)` is added for Phase 34's use. Phase 33 adds the data model but the lookup behavior is Phase 34+.

### ValidateAndParseCommandMap pattern from Phase 32

`CommandMapWatcherService.ValidateAndParseCommandMap` is a static internal method that handles per-entry validation with skip-on-error semantics. The `ValueType` validation for `CommandSlotOptions` (TEN-03) uses the same pattern in the tenant watcher path. However, TEN-03 says validation happens "at load time" — this is Phase 34 behavioral work. Phase 33 only adds the data model shape.

### No existing TenantVectorWatcherValidation test file

There is no `TenantVectorWatcherValidationTests.cs` analogous to `CommandMapWatcherValidationTests.cs` or `OidMapWatcherValidationTests.cs`. The existing `TenantVectorOptionsValidatorTests.cs` tests the `IValidateOptions<TenantVectorOptions>` validator (currently a no-op). Phase 33 adds model fields but not validation behavior — the validator stays as no-op.

---

## Open Questions

1. **DeviceOptions.Name removal completeness**
   - What we know: `DeviceOptions.Name` is used in `DevicesOptionsValidator` and `DeviceRegistryTests`. The `appsettings.Development.json` uses `"Name"` field.
   - What's unclear: Are there any other YAML fixtures not yet identified that use `"Name"` in device objects?
   - Recommendation: Before writing code, do a project-wide grep for `"Name"` in YAML/JSON device contexts and `device.Name` / `DeviceOptions.Name` in C# to get a complete list.

2. **ServiceCollectionExtensions.cs DI wiring for TenantVectorRegistry — RESOLVED**
   - What we know: Line 308-312 in `ServiceCollectionExtensions.cs` uses an explicit factory lambda:
     ```csharp
     services.AddSingleton<TenantVectorRegistry>(sp =>
         new TenantVectorRegistry(
             sp.GetRequiredService<IDeviceRegistry>(),
             sp.GetRequiredService<IOidMapService>(),   // <-- REMOVE THIS LINE
             sp.GetRequiredService<ILogger<TenantVectorRegistry>>()));
     ```
   - The `IOidMapService` argument must be removed from this factory registration.
   - Also note: `AddSnmpScheduling` uses `device.IpAddress`, `device.Port`, and `device.Polls` for Quartz job key generation — it does NOT use `device.Name`. So Quartz job key format is unchanged by the rename.
   - No open question remains here.

3. **MetricSlotHolder IntervalSeconds behavior with 0**
   - What we know: DeriveIntervalSeconds returns 0 as a fallback — MetricSlotHolder already accepts 0.
   - What's unclear: Whether any downstream code (Phase 34+) makes decisions based on IntervalSeconds == 0.
   - Recommendation: Phase 33 just stores 0 as-is. No special handling needed in this phase.

---

## Sources

### Primary (HIGH confidence)
All findings from direct source code inspection:
- `src/SnmpCollector/Configuration/DeviceOptions.cs` — existing CommunityString field (optional)
- `src/SnmpCollector/Pipeline/DeviceInfo.cs` — positional record with optional CommunityString
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — both constructor and ReloadAsync build paths
- `src/SnmpCollector/Pipeline/CommunityStringHelper.cs` — TryExtractDeviceName already exists
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — IOidMapService + DeriveIntervalSeconds
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — current shape
- `src/SnmpCollector/Configuration/TenantOptions.cs` — current shape
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — Name validation
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — community string fallback logic
- `src/SnmpCollector/config/devices.json` — local dev fixture with Name field
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — K8s ConfigMap with Name field
- `deploy/k8s/production/configmap.yaml` — production ConfigMap with Name field
- `tests/e2e/fixtures/device-added-configmap.yaml` — fixture with Name field
- `tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs` — test constructions using Name
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` — IOidMapService in CreateRegistry

---

## Metadata

**Confidence breakdown:**
- Change inventory: HIGH — verified by reading every affected file
- Architecture patterns: HIGH — derived from existing codebase conventions
- Pitfalls: HIGH — identified by tracing actual call chains

**Research date:** 2026-03-14
**Valid until:** Indefinitely (codebase-internal research, not library-version dependent)
