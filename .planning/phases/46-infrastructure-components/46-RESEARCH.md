# Phase 46: Infrastructure Components - Research

**Researched:** 2026-03-16
**Domain:** C# infrastructure additions — SuppressionCache, SnapshotJobOptions, ISnmpClient.SetAsync, PipelineMetricService command counters, TenantOptions.SuppressionWindowSeconds, Value+ValueType parse validation
**Confidence:** HIGH

---

## Summary

Phase 46 builds four independently testable components that downstream phases (47 SnapshotJob, 48 CommandWorker) consume. Every pattern follows an existing in-codebase model — there is nothing novel to design. Phase 45 (structural prerequisites) is confirmed complete: `SnmpSource.Command`, `MetricSlotHolder.Role`, and `Tenant.Commands` all exist in the current source.

The four deliverables are: (1) `ISuppressionCache` / `SuppressionCache` — a `ConcurrentDictionary<string, DateTimeOffset>` singleton following the `LivenessVectorService` pattern; (2) `SnapshotJobOptions` — a configuration POCO following `HeartbeatJobOptions`/`CorrelationJobOptions` exactly, bound from `"SnapshotJob"` section with `ValidateDataAnnotations` + `ValidateOnStart`; (3) `ISnmpClient.SetAsync` — a new method on the existing interface, delegating to `Messenger.SetAsync` in `SharpSnmpClient`, with a `ParseSnmpData` static helper for the three supported types; (4) three new pipeline counters on `PipelineMetricService` — `snmp.command.sent`, `snmp.command.failed`, `snmp.command.suppressed` — following the existing 12-counter pattern exactly.

Two additional items are required by this phase that were not explicitly called out in the phase description but are direct dependencies: (a) `TenantOptions.SuppressionWindowSeconds` property (per-tenant, default 60s) — referenced by `ISuppressionCache.TrySuppress` at evaluation time; (b) Value+ValueType parse-time validation in `TenantVectorWatcherService.ValidateAndBuildTenants` — the existing validation skips invalid ValueType strings but does NOT check that `Value` is parseable as that type. A `"ValueType": "Integer32", "Value": "not-a-number"` config entry currently passes validation and would throw `FormatException` at execution time.

**Primary recommendation:** Build in order — `TenantOptions.SuppressionWindowSeconds` first (requires watcher update for Value+ValueType parse validation), then `SnapshotJobOptions`, then `ISuppressionCache`/`SuppressionCache`, then `ISnmpClient.SetAsync` + `ParseSnmpData`, then the three pipeline counters. Each step is independently compilable and testable.

---

## Standard Stack

No new NuGet packages are required. All APIs are from existing dependencies.

### Files Changed / Created

| File | Change Type | What |
|------|-------------|------|
| `src/SnmpCollector/Configuration/TenantOptions.cs` | Property added | `SuppressionWindowSeconds` (default 60, `[Range(1, 3600)]`) |
| `src/SnmpCollector/Services/TenantVectorWatcherService.cs` | Validation added | Value+ValueType parse-time check in `ValidateAndBuildTenants` |
| `src/SnmpCollector/Configuration/SnapshotJobOptions.cs` | New file | `SectionName`, `IntervalSeconds`, `TimeoutMultiplier` with `[Range]` DataAnnotations |
| `src/SnmpCollector/Pipeline/ISuppressionCache.cs` | New file | `ISuppressionCache` interface (`TrySuppress`, `Count`) |
| `src/SnmpCollector/Pipeline/SuppressionCache.cs` | New file | `ConcurrentDictionary<string, DateTimeOffset>` implementation |
| `src/SnmpCollector/Pipeline/ISnmpClient.cs` | Method added | `SetAsync` signature |
| `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` | Method added | `SetAsync` delegation to `Messenger.SetAsync` + `ParseSnmpData` static helper |
| `src/SnmpCollector/Telemetry/PipelineMetricService.cs` | Counters + methods added | `snmp.command.sent`, `snmp.command.failed`, `snmp.command.suppressed` |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Registration added | `SnapshotJobOptions` bind + `ISuppressionCache` singleton |
| `tests/SnmpCollector.Tests/Pipeline/SuppressionCacheTests.cs` | New file | Full test coverage |
| `tests/SnmpCollector.Tests/Pipeline/SharpSnmpClientSetTests.cs` | New file | `ParseSnmpData` dispatch tests |
| `tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs` | Test additions | Three command counter tests |
| `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` | Test additions | Value+ValueType parse validation tests |

---

## Architecture Patterns

### Pattern 1: SnapshotJobOptions — Follow HeartbeatJobOptions / CorrelationJobOptions Exactly

**What:** `SnapshotJobOptions` is a simple configuration POCO registered the same way as the two existing job options types.

**Current HeartbeatJobOptions pattern (source: `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs`):**
```csharp
public const string SectionName = "HeartbeatJob";
[Range(1, int.MaxValue)]
public int IntervalSeconds { get; set; } = 15;
```

**SnapshotJobOptions structure:**
```csharp
// src/SnmpCollector/Configuration/SnapshotJobOptions.cs
using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

public sealed class SnapshotJobOptions
{
    public const string SectionName = "SnapshotJob";

    [Range(1, 300)]
    public int IntervalSeconds { get; set; } = 15;

    [Range(0.1, 0.9)]
    public double TimeoutMultiplier { get; set; } = 0.8;
}
```

**Registration in `ServiceCollectionExtensions.AddSnmpConfiguration` (source: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs`, following the `HeartbeatJobOptions` block at line 206):**
```csharp
services.AddOptions<SnapshotJobOptions>()
    .Bind(configuration.GetSection(SnapshotJobOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Key facts:**
- `SuppressionWindowSeconds` is NOT on `SnapshotJobOptions` — it is per-tenant on `TenantOptions`
- Range for `IntervalSeconds`: 1–300 (matches spec; much tighter than `HeartbeatJobOptions`'s 1–intMax)
- Range for `TimeoutMultiplier`: 0.1–0.9 (same as poll pattern constraint)
- No `SectionName` const is used in `AddSnmpScheduling` for Quartz registration — the same pattern as `HeartbeatJobOptions` where options are bound separately from scheduling

### Pattern 2: TenantOptions.SuppressionWindowSeconds — Simple Property Addition

**What:** `SuppressionWindowSeconds` is a per-tenant config property. It controls the suppression window duration passed to `ISuppressionCache.TrySuppress` at evaluation time in SnapshotJob (Phase 47). It must be on `TenantOptions` because different tenants may have different device response rates.

**Addition to `src/SnmpCollector/Configuration/TenantOptions.cs`:**
```csharp
/// <summary>
/// Duration of the command suppression window in seconds.
/// After a command is sent for a tenant, the same command is suppressed for this many seconds.
/// Default: 60 seconds. Must be positive.
/// </summary>
public int SuppressionWindowSeconds { get; set; } = 60;
```

No validation attribute needed here (default 60 is always valid; the suppression cache accepts any positive int). No changes to `TenantVectorRegistry.Reload` or `Tenant` — the suppression window is read from `TenantOptions` at evaluation time by SnapshotJob, not stored in the runtime `Tenant` object. Wait — SnapshotJob reads `Tenant.Commands` which are `CommandSlotOptions`. The window needs to be accessible at evaluation time. Two options: (a) store it on `Tenant` directly; (b) SnapshotJob reads `TenantVectorOptions` from config. Option (a) is cleaner and follows the existing `Holders`/`Commands` pattern.

**Recommendation:** Add `SuppressionWindowSeconds` to `Tenant` as an immutable property, populated from `tenantOpts.SuppressionWindowSeconds` in `TenantVectorRegistry.Reload`, same as `Priority` and `Commands`.

**Updated `Tenant` constructor:**
```csharp
public Tenant(string id, int priority, IReadOnlyList<MetricSlotHolder> holders,
    IReadOnlyList<CommandSlotOptions> commands, int suppressionWindowSeconds)
{
    Id = id;
    Priority = priority;
    Holders = holders;
    Commands = commands;
    SuppressionWindowSeconds = suppressionWindowSeconds;
}
public int SuppressionWindowSeconds { get; }
```

**Updated `TenantVectorRegistry.Reload` call site:**
```csharp
var tenant = new Tenant(tenantId, tenantOpts.Priority, holders, tenantOpts.Commands,
    tenantOpts.SuppressionWindowSeconds);
```

This adds a required constructor parameter — all `new Tenant(...)` call sites in tests must be updated (add `suppressionWindowSeconds: 60` as a named argument).

### Pattern 3: Value+ValueType Parse Validation — Add to ValidateAndBuildTenants

**What:** The existing `TenantVectorWatcherService.ValidateAndBuildTenants` validates ValueType against the set `{"Integer32", "IpAddress", "OctetString"}` but does NOT check that `Value` is parseable as that type. A `{"ValueType": "Integer32", "Value": "not-a-number"}` entry passes today and causes `FormatException` at SET execution time. This check belongs in the per-command validation loop, immediately after the existing empty-Value check (after step 5, before the IP+Port check that is currently step 6).

**Addition to the command validation loop in `ValidateAndBuildTenants` (after line 277 in current source):**
```csharp
// 5b. Value parseable as ValueType (prevents FormatException at execution time)
if (cmd.ValueType == "Integer32" && !int.TryParse(cmd.Value, out _))
{
    logger.LogError(
        "Tenant '{TenantId}' Commands[{Index}] skipped: Value '{Value}' is not a valid Integer32",
        tenantId, k, cmd.Value);
    continue;
}
if (cmd.ValueType == "IpAddress" && !System.Net.IPAddress.TryParse(cmd.Value, out _))
{
    logger.LogError(
        "Tenant '{TenantId}' Commands[{Index}] skipped: Value '{Value}' is not a valid IP address",
        tenantId, k, cmd.Value);
    continue;
}
// OctetString: any non-empty string is valid (already checked above)
```

**Why here (not in CommandWorker):** The CONTEXT.md locked decision: "Value+ValueType validation at config load time (TenantVectorWatcherService) — invalid entries skipped with Error log. CommandWorker receives pre-validated data."

### Pattern 4: ISuppressionCache — ConcurrentDictionary Pattern from LivenessVectorService

**What:** `SuppressionCache` follows `LivenessVectorService` exactly: a `ConcurrentDictionary<string, DateTimeOffset>` singleton, no background sweep, lazy expiry on read.

**Semantics (from CONTEXT.md locked decisions):**
- `TrySuppress(key, windowSeconds)` → `true` = suppressed (skip), `false` = not suppressed (proceed)
- On `false` (allowed): stamps `DateTimeOffset.UtcNow` for the key — window starts from this call
- On `true` (suppressed): does NOT update the timestamp — window is not extended by suppressed calls
- `Count` property for diagnostics

**Interface:**
```csharp
// src/SnmpCollector/Pipeline/ISuppressionCache.cs
namespace SnmpCollector.Pipeline;

public interface ISuppressionCache
{
    /// <summary>
    /// Checks whether the given key is within its suppression window.
    /// Returns false (not suppressed) and stamps the key if the window has expired or the key is new.
    /// Returns true (suppressed) without updating the stamp if the window is still active.
    /// </summary>
    bool TrySuppress(string key, int windowSeconds);

    /// <summary>
    /// Number of entries currently in the cache (including expired entries not yet evicted).
    /// Diagnostic only.
    /// </summary>
    int Count { get; }
}
```

**Implementation:**
```csharp
// src/SnmpCollector/Pipeline/SuppressionCache.cs
using System.Collections.Concurrent;

namespace SnmpCollector.Pipeline;

public sealed class SuppressionCache : ISuppressionCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stamps = new();

    public bool TrySuppress(string key, int windowSeconds)
    {
        var now = DateTimeOffset.UtcNow;

        if (_stamps.TryGetValue(key, out var lastStamp)
            && now - lastStamp < TimeSpan.FromSeconds(windowSeconds))
        {
            return true; // still within window — suppressed
        }

        // Window expired or first call — stamp and allow
        _stamps[key] = now;
        return false;
    }

    public int Count => _stamps.Count;
}
```

**Registration in `ServiceCollectionExtensions.AddSnmpPipeline`:**
```csharp
services.AddSingleton<ISuppressionCache, SuppressionCache>();
```

**No race condition concern:** `[DisallowConcurrentExecution]` on SnapshotJob (Phase 47) guarantees a single execution at any moment. The check-then-stamp sequence in `TrySuppress` is non-atomic but safe because only one SnapshotJob execution can be in-flight.

### Pattern 5: ISnmpClient.SetAsync — Mirrors GetAsync Exactly

**What:** A new method on `ISnmpClient` following the exact same signature shape as `GetAsync`. Returns `IList<Variable>` (SET response varbinds) for dispatch through the MediatR pipeline by CommandWorker.

**Updated interface (`src/SnmpCollector/Pipeline/ISnmpClient.cs`):**
```csharp
Task<IList<Variable>> SetAsync(
    VersionCode version,
    IPEndPoint endpoint,
    OctetString community,
    Variable variable,
    CancellationToken ct);
```

Note: `SetAsync` takes a single `Variable` (not `IList<Variable>`) — one OID per call, matching `CommandSlotOptions` (one command = one OID). This matches the CONTEXT.md locked decision.

**Implementation in `SharpSnmpClient.cs`:**
```csharp
// src/SnmpCollector/Pipeline/SharpSnmpClient.cs
// Add using alias at top of file (required — avoids CS0104 ambiguity with System.TimeoutException):
using SnmpTimeout = Lextm.SharpSnmpLib.Messaging.TimeoutException;

public Task<IList<Variable>> SetAsync(
    VersionCode version,
    IPEndPoint endpoint,
    OctetString community,
    Variable variable,
    CancellationToken ct)
    => Messenger.SetAsync(version, endpoint, community, new List<Variable> { variable }, ct);

/// <summary>
/// Converts a config Value+ValueType pair into the SharpSnmpLib ISnmpData type
/// required for Variable construction.
/// </summary>
public static ISnmpData ParseSnmpData(string value, string valueType)
    => valueType switch
    {
        "Integer32"  => new Integer32(int.Parse(value)),
        "OctetString" => new OctetString(value),
        "IpAddress"   => new IP(value),
        _ => throw new ArgumentException($"Unsupported ValueType: {valueType}", nameof(valueType))
    };
```

**Critical naming facts (from CONTEXT.md Specifics):**
- `IP` not `IpAddress` — `Lextm.SharpSnmpLib.IP` is the correct class. Using `IpAddress` produces CS0246.
- `Lextm.SharpSnmpLib.Messaging.TimeoutException` — NOT `System.TimeoutException`. Requires a `using` alias or fully qualified reference to avoid CS0104.
- `Messenger.SetAsync` signature: `(VersionCode, IPEndPoint, OctetString, IList<Variable>, CancellationToken)` → `Task<IList<Variable>>` — verified by reflection against SharpSnmpLib 12.5.7.

**Using directives needed in SharpSnmpClient.cs:**
```csharp
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using System.Net;
using SnmpTimeout = Lextm.SharpSnmpLib.Messaging.TimeoutException; // alias for CS0104
```

### Pattern 6: Pipeline Command Counters — Follow PipelineMetricService Pattern Exactly

**What:** Three new `Counter<long>` instruments on `PipelineMetricService`, each with a `device_name` tag. Follows the 12-existing-counter pattern exactly.

**Source: `src/SnmpCollector/Telemetry/PipelineMetricService.cs`** — the pattern is:
1. Private `Counter<long>` field with comment identifying the spec item
2. Created in constructor via `_meter.CreateCounter<long>("snmp.xxx.yyy")`
3. Public increment method: `void IncrementXxx(string deviceName)` → `_counter.Add(1, new TagList { { "device_name", deviceName } })`

**Three new counters:**
```csharp
// PMET-13: counts SNMP SET commands dispatched to CommandWorkerService
private readonly Counter<long> _commandSent;

// PMET-14: counts SNMP SET commands that failed (timeout, error response, OID not found)
private readonly Counter<long> _commandFailed;

// PMET-15: counts SNMP SET commands suppressed by SuppressionCache within the suppression window
private readonly Counter<long> _commandSuppressed;
```

**Constructor additions:**
```csharp
_commandSent       = _meter.CreateCounter<long>("snmp.command.sent");
_commandFailed     = _meter.CreateCounter<long>("snmp.command.failed");
_commandSuppressed = _meter.CreateCounter<long>("snmp.command.suppressed");
```

**Public methods:**
```csharp
/// <summary>PMET-13: Increment the count of dispatched SET commands by 1.</summary>
public void IncrementCommandSent(string deviceName)
    => _commandSent.Add(1, new TagList { { "device_name", deviceName } });

/// <summary>PMET-14: Increment the count of failed SET commands by 1.</summary>
public void IncrementCommandFailed(string deviceName)
    => _commandFailed.Add(1, new TagList { { "device_name", deviceName } });

/// <summary>PMET-15: Increment the count of suppressed SET commands by 1.</summary>
public void IncrementCommandSuppressed(string deviceName)
    => _commandSuppressed.Add(1, new TagList { { "device_name", deviceName } });
```

**Note on `device_name` for command counters:** Command counters use the `CommandSlotOptions.Ip` or a resolved device name as the `deviceName` argument. Phase 47/48 callers will pass whatever device identifier is appropriate. The planner should note that `CommandSlotOptions` has `Ip` and `Port` but no `Name` — the device name must be resolved from `IDeviceRegistry.TryGetByIpPort` at execution time.

### Anti-Patterns to Avoid

- **Do NOT add `SnmpSource.Command` bypass in OidResolutionBehavior** — Phase 45 established the MetricName-already-set guard. No Source-specific conditions are needed. The bypass is already generic.
- **Do NOT make `TrySuppress` atomic with a lock** — `[DisallowConcurrentExecution]` on SnapshotJob is the concurrency guard. Adding a lock to `SuppressionCache` would be defensive redundancy that complicates testing and contradicts the design.
- **Do NOT extend `ParseSnmpData` default case to silently return a default value** — throwing `ArgumentException` is correct; invalid ValueType strings should never reach this method because `TenantVectorWatcherService` filters them at config load time. A silent fallback would mask bugs.
- **Do NOT use `AddHostedService<SuppressionCache>()` pattern** — `ISuppressionCache` is a pure singleton data structure, not a background service. Register with `AddSingleton<ISuppressionCache, SuppressionCache>()` only.
- **Do NOT store `windowSeconds` in the cache entry** — CONTEXT.md explicitly decided: window is passed at check time, not stored. This means tenant config changes to `SuppressionWindowSeconds` take effect immediately on the next check without requiring cache invalidation.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe timestamp store | Custom lock + Dictionary | `ConcurrentDictionary<string, DateTimeOffset>` | BCL; already used in `LivenessVectorService`; no additional dependencies |
| SNMP SET wire call | Custom UDP socket | `Messenger.SetAsync` (SharpSnmpLib 12.5.7) | Already installed; verified by reflection; handles SNMP framing, version encoding, response parsing |
| OTel counter registration | Custom metrics aggregation | `_meter.CreateCounter<long>()` | Existing pattern on `PipelineMetricService`; meter factory injection already configured |
| Config validation at startup | Manual startup check | `.ValidateDataAnnotations().ValidateOnStart()` | Already used for all options; throws `OptionsValidationException` before app starts |
| IP address string parsing | Regex or manual split | `System.Net.IPAddress.TryParse` | BCL; handles IPv4 and IPv6; already used elsewhere in the codebase |

---

## Common Pitfalls

### Pitfall 1: SharpSnmpLib IP Type Name (CS0246)

**What goes wrong:** `new IpAddress(value)` — `IpAddress` does not exist in SharpSnmpLib. The correct class is `Lextm.SharpSnmpLib.IP`.

**Why it happens:** `System.Net.IPAddress` is the commonly known type; developers assume SharpSnmpLib follows the same name. SharpSnmpLib's type represents an SNMP IpAddress-typed OID value, not a .NET `System.Net.IPAddress`.

**How to avoid:** In `ParseSnmpData`, use `new IP(value)` with `using Lextm.SharpSnmpLib;` in scope.

**Warning signs:** CS0246 "The type or namespace name 'IpAddress' could not be found" during build.

### Pitfall 2: TimeoutException Ambiguity (CS0104)

**What goes wrong:** `catch (TimeoutException)` catches `System.TimeoutException` in CommandWorker but the SNMP library throws `Lextm.SharpSnmpLib.Messaging.TimeoutException`. The wrong exception type is caught, leaving the timeout unhandled.

**Why it happens:** Both `System.TimeoutException` and `Lextm.SharpSnmpLib.Messaging.TimeoutException` are named `TimeoutException`. With both namespaces in scope, the compiler produces CS0104 (ambiguous reference) or silently resolves to the wrong type.

**How to avoid:** Add a `using` alias in `SharpSnmpClient.cs` (and any future CommandWorker):
```csharp
using SnmpTimeout = Lextm.SharpSnmpLib.Messaging.TimeoutException;
```
Then catch `catch (SnmpTimeout)`.

**Warning signs:** CS0104 at compile time, or — if only `System.Net` is in scope — TimeoutException exceptions from SNMP ops reaching unhandled exception handlers.

### Pitfall 3: ValueType String "IpAddress" vs SharpSnmpLib Class "IP"

**What goes wrong:** The config string label for IP addresses is `"IpAddress"` (set by TenantVectorWatcherService validation). The SharpSnmpLib class is `IP`. The `ParseSnmpData` switch must map the config string `"IpAddress"` to `new IP(value)`, NOT `new IpAddress(value)`.

**Why it happens:** Two different naming conventions in the same code path. The config-facing name uses the SNMP type name ("IpAddress"); SharpSnmpLib uses its own abbreviated class name ("IP").

**How to avoid:** In `ParseSnmpData`:
```csharp
"IpAddress" => new IP(value),  // config string "IpAddress" → SharpSnmpLib IP class
```

**Warning signs:** Any attempt to write `new IpAddress(value)` produces CS0246.

### Pitfall 4: Tenant Constructor Call Sites Not Updated After Adding SuppressionWindowSeconds

**What goes wrong:** `Tenant` currently takes four constructor parameters. Adding `suppressionWindowSeconds` as a fifth required parameter breaks all `new Tenant(...)` calls in tests.

**Why it happens:** Tests construct `Tenant` directly with positional arguments.

**How to avoid:** After adding the parameter, search for `new Tenant(` across all test files and add `suppressionWindowSeconds: 60` (or a named argument) to each call site.

**Warning signs:** Compile errors at `new Tenant(...)` test construction sites — these are CS errors, not silent bugs.

### Pitfall 5: Missing Value+ValueType Parse Validation Leaves Runtime FormatException

**What goes wrong:** `TenantVectorWatcherService.ValidateAndBuildTenants` validates `ValueType` is in the accepted set but does NOT currently validate that `Value` is parseable as that type. A config entry `{ "ValueType": "Integer32", "Value": "not-a-number" }` passes all existing checks. At SET execution time, `int.Parse("not-a-number")` in `ParseSnmpData` throws `FormatException`, crashing the command execution.

**Why it happens:** The existing validation was implemented incrementally and the parse-time check was not yet added.

**How to avoid:** Add the parse validation block in `ValidateAndBuildTenants` after the empty-Value check. See Pattern 3 for the exact code.

**Warning signs:** Integration tests that supply invalid Value strings succeeding at config load but failing at execution — this gap will produce confusing failure modes in Phase 48 tests if not closed now.

### Pitfall 6: SuppressionCache TrySuppress — Suppressed Calls Must NOT Re-Stamp

**What goes wrong:** If `TrySuppress` stamps `DateTimeOffset.UtcNow` on both allowed (returns false) and suppressed (returns true) calls, the suppression window effectively extends indefinitely as long as commands keep firing — the window never expires.

**Why it happens:** It is intuitive to "update" the timestamp on every call. But the CONTEXT.md decision is explicit: stamp only on allowed calls (returns false). Suppressed calls must not change the stamp.

**How to avoid:** Only assign `_stamps[key] = now` in the code path that returns `false`. The code path that returns `true` must not touch `_stamps`.

**Warning signs:** Suppression test: `TrySuppress` called 100 times in sequence should return `false` once, then `true` for all subsequent calls within the window — if subsequent calls return `false`, stamping on suppressed calls is the bug.

---

## Code Examples

### SuppressionCache — Full Implementation

```csharp
// Source: Pattern derived from LivenessVectorService (src/SnmpCollector/Pipeline/LivenessVectorService.cs)
using System.Collections.Concurrent;

namespace SnmpCollector.Pipeline;

public sealed class SuppressionCache : ISuppressionCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stamps = new();

    public bool TrySuppress(string key, int windowSeconds)
    {
        var now = DateTimeOffset.UtcNow;

        if (_stamps.TryGetValue(key, out var lastStamp)
            && now - lastStamp < TimeSpan.FromSeconds(windowSeconds))
        {
            return true;  // within window — suppressed, do NOT update stamp
        }

        _stamps[key] = now;  // expired or new — stamp and allow
        return false;
    }

    public int Count => _stamps.Count;
}
```

### SharpSnmpClient — SetAsync and ParseSnmpData

```csharp
// Source: Messenger.SetAsync verified by reflection against SharpSnmpLib 12.5.7
// src/SnmpCollector/Pipeline/SharpSnmpClient.cs additions

using SnmpTimeout = Lextm.SharpSnmpLib.Messaging.TimeoutException;

public Task<IList<Variable>> SetAsync(
    VersionCode version,
    IPEndPoint endpoint,
    OctetString community,
    Variable variable,
    CancellationToken ct)
    => Messenger.SetAsync(version, endpoint, community, new List<Variable> { variable }, ct);

public static ISnmpData ParseSnmpData(string value, string valueType)
    => valueType switch
    {
        "Integer32"   => new Integer32(int.Parse(value)),
        "OctetString" => new OctetString(value),
        "IpAddress"   => new IP(value),   // config string "IpAddress" → SharpSnmpLib IP class (NOT IpAddress)
        _ => throw new ArgumentException($"Unsupported ValueType: {valueType}", nameof(valueType))
    };
```

### PipelineMetricService — Command Counter Increment Methods

```csharp
// Source: existing pattern in src/SnmpCollector/Telemetry/PipelineMetricService.cs
public void IncrementCommandSent(string deviceName)
    => _commandSent.Add(1, new TagList { { "device_name", deviceName } });

public void IncrementCommandFailed(string deviceName)
    => _commandFailed.Add(1, new TagList { { "device_name", deviceName } });

public void IncrementCommandSuppressed(string deviceName)
    => _commandSuppressed.Add(1, new TagList { { "device_name", deviceName } });
```

### SuppressionCache Tests — Key Behavioral Scenarios

```csharp
// File: tests/SnmpCollector.Tests/Pipeline/SuppressionCacheTests.cs

[Fact]
public void FirstCall_ReturnsFalse_AndStamps()
{
    var cache = new SuppressionCache();
    var result = cache.TrySuppress("10.0.0.1:161:setSpeed", windowSeconds: 60);
    Assert.False(result);  // not suppressed — first call
    Assert.Equal(1, cache.Count);
}

[Fact]
public void SecondCallWithinWindow_ReturnsTrue()
{
    var cache = new SuppressionCache();
    cache.TrySuppress("10.0.0.1:161:setSpeed", windowSeconds: 60);
    var result = cache.TrySuppress("10.0.0.1:161:setSpeed", windowSeconds: 60);
    Assert.True(result);  // suppressed within window
}

[Fact]
public void AfterWindowExpires_ReturnsFalse()
{
    var cache = new SuppressionCache();
    cache.TrySuppress("10.0.0.1:161:setSpeed", windowSeconds: 0);  // 0s window expires immediately
    var result = cache.TrySuppress("10.0.0.1:161:setSpeed", windowSeconds: 0);
    Assert.False(result);  // expired — allowed again
}

[Fact]
public void DifferentKeys_Independent()
{
    var cache = new SuppressionCache();
    cache.TrySuppress("10.0.0.1:161:setSpeed", windowSeconds: 60);
    var result = cache.TrySuppress("10.0.0.2:161:setSpeed", windowSeconds: 60);
    Assert.False(result);  // different device key — independent
}

[Fact]
public void WindowPassedAtCheckTime_NotStored()
{
    // Demonstrates that changing windowSeconds on second call takes effect immediately
    var cache = new SuppressionCache();
    cache.TrySuppress("key", windowSeconds: 60);      // stamp with 60s window
    var result = cache.TrySuppress("key", windowSeconds: 0); // 0s window — expired immediately
    Assert.False(result);  // new window value overrides; not suppressed
}
```

### ParseSnmpData Tests

```csharp
// File: tests/SnmpCollector.Tests/Pipeline/SharpSnmpClientSetTests.cs

[Theory]
[InlineData("42", "Integer32")]
[InlineData("hello", "OctetString")]
[InlineData("10.0.0.1", "IpAddress")]
public void ParseSnmpData_ValidInput_ReturnsCorrectType(string value, string valueType)
{
    var result = SharpSnmpClient.ParseSnmpData(value, valueType);
    Assert.NotNull(result);
}

[Fact]
public void ParseSnmpData_Integer32_ReturnsInteger32Instance()
{
    var result = SharpSnmpClient.ParseSnmpData("42", "Integer32");
    Assert.IsType<Integer32>(result);
}

[Fact]
public void ParseSnmpData_OctetString_ReturnsOctetStringInstance()
{
    var result = SharpSnmpClient.ParseSnmpData("hello", "OctetString");
    Assert.IsType<OctetString>(result);
}

[Fact]
public void ParseSnmpData_IpAddress_ReturnsIPInstance()
{
    var result = SharpSnmpClient.ParseSnmpData("10.0.0.1", "IpAddress");
    Assert.IsType<IP>(result);
}

[Fact]
public void ParseSnmpData_UnknownType_ThrowsArgumentException()
{
    Assert.Throws<ArgumentException>(() =>
        SharpSnmpClient.ParseSnmpData("value", "Unknown"));
}
```

### PipelineMetricService Counter Tests (additions to existing test class)

```csharp
// Source: existing test pattern in tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs

[Fact]
public void IncrementCommandSent_RecordsWithDeviceNameTag()
{
    _service.IncrementCommandSent("device-01");
    var match = _measurements.Single(m => m.InstrumentName == "snmp.command.sent");
    Assert.Equal(1L, match.Value);
    var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
    Assert.Equal("device-01", tags["device_name"]);
}

[Fact]
public void IncrementCommandFailed_RecordsWithDeviceNameTag()
{
    _service.IncrementCommandFailed("device-01");
    var match = _measurements.Single(m => m.InstrumentName == "snmp.command.failed");
    Assert.Equal(1L, match.Value);
    var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
    Assert.Equal("device-01", tags["device_name"]);
}

[Fact]
public void IncrementCommandSuppressed_RecordsWithDeviceNameTag()
{
    _service.IncrementCommandSuppressed("device-01");
    var match = _measurements.Single(m => m.InstrumentName == "snmp.command.suppressed");
    Assert.Equal(1L, match.Value);
    var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
    Assert.Equal("device-01", tags["device_name"]);
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| No command suppression | `SuppressionCache` singleton | Phase 46 | Prevents duplicate SET commands within configurable window |
| `ISnmpClient` GET-only | `ISnmpClient` GET + SET | Phase 46 | CommandWorker (Phase 48) can use mock client for testing without real SNMP |
| 12 pipeline counters | 15 pipeline counters | Phase 46 | `snmp.command.*` counters enable Prometheus alerting on SET failure rate |
| No `TenantOptions.SuppressionWindowSeconds` | Per-tenant `SuppressionWindowSeconds` | Phase 46 | Tenants can have independent suppression windows; window from config is live (not cached in Tenant) |

**Deprecated/outdated:**

- None. No existing code is deprecated or replaced in this phase. All changes are additions.

---

## Open Questions

1. **`device_name` tag source for command counters**
   - What we know: `CommandSlotOptions` has `Ip` and `Port` but no `Name`. Counter methods take `string deviceName`.
   - What's unclear: Whether the planner should use `cmd.Ip` as-is, `$"{cmd.Ip}:{cmd.Port}"`, or resolve a name from `IDeviceRegistry` for the counter tag.
   - Recommendation: Use `$"{cmd.Ip}:{cmd.Port}"` as a consistent device identifier for command counters in Phase 46 increment methods. Phase 48 (CommandWorker) can resolve a friendly name if needed. The counter method signature accepts any string — this is a call-site decision, not an API decision.

2. **SuppressionWindowSeconds on Tenant vs accessed from TenantOptions at runtime**
   - What we know: CONTEXT.md says it's per-tenant on TenantOptions. SnapshotJob (Phase 47) needs it at evaluation time.
   - What's unclear: Whether the planner puts it on `Tenant` (requires constructor update, test updates) or has SnapshotJob access `TenantOptions` directly.
   - Recommendation: Add to `Tenant` (immutable property from constructor), populated in `TenantVectorRegistry.Reload`. This follows the existing pattern for all tenant data and avoids SnapshotJob needing an `IOptions<TenantVectorOptions>` injection. Adds one required constructor parameter — all test call sites must be updated (a compile-time error, not a silent bug).

---

## Sources

### Primary (HIGH confidence — direct codebase reading)

- `src/SnmpCollector/Pipeline/ISnmpClient.cs` — existing `GetAsync` signature, extension model
- `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` — delegation pattern to `Messenger.GetAsync`
- `src/SnmpCollector/Pipeline/LivenessVectorService.cs` — `ConcurrentDictionary<string, DateTimeOffset>` pattern for `SuppressionCache`
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — 12-counter pattern, `TagList { "device_name" }` convention
- `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` — `SectionName` const, `[Range]` DataAnnotations, defaults pattern
- `src/SnmpCollector/Configuration/CorrelationJobOptions.cs` — minimal options POCO pattern
- `src/SnmpCollector/Configuration/TenantOptions.cs` — current state (no `SuppressionWindowSeconds` yet)
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — `Value`, `ValueType` field shapes
- `src/SnmpCollector/Pipeline/Tenant.cs` — current constructor (4 params — Phase 45 complete)
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — `Command` value present (Phase 45 complete)
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `Role` property present (Phase 45 complete)
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — current validation logic (ValueType validated, Value parse NOT validated)
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — options registration pattern, singleton pattern
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — timeout pattern (`intervalSeconds * pollGroup.TimeoutMultiplier`)
- `tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs` — MeterListener test pattern for counter assertions
- `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` — watcher validation test structure
- `.planning/phases/46-infrastructure-components/46-CONTEXT.md` — all locked decisions
- `.planning/phases/45-structural-prerequisites/45-RESEARCH.md` — confirmed Phase 45 complete
- `.planning/research/SUMMARY.md` — SharpSnmpLib API verification (Messenger.SetAsync reflected from DLL)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all patterns read directly from existing source files
- Architecture: HIGH — all patterns derived from existing working code in the same repository; Phase 45 confirmed complete
- Pitfalls: HIGH — all pitfalls derived from direct source inspection and CONTEXT.md specifics; naming pitfalls verified against codebase

**Research date:** 2026-03-16
**Valid until:** Stable — no external dependencies; valid until codebase is refactored
