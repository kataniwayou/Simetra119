# Phase 25: Config Models and Validation - Research

**Researched:** 2026-03-10
**Domain:** .NET Options pattern — POCO hierarchy + IValidateOptions with DI-injected service dependency
**Confidence:** HIGH

## Summary

Phase 25 introduces `tenantvector.json` as a new config file containing a flat array of tenant objects, each with priority and metric slot definitions. The implementation follows the exact same patterns already established in the codebase for `DevicesOptions` / `DevicesOptionsValidator`: a POCO hierarchy bound via `IConfiguration`, validated at startup via a custom `IValidateOptions<T>` implementation, with all errors collected into `List<string>` before returning.

The key technical challenge is that the validator needs to check each `MetricName` against the OID map. The current `IOidMapService` interface only supports forward lookup (`Resolve(oid) -> name`). A reverse lookup method (`ContainsMetricName(name) -> bool`) must be added to `IOidMapService` and implemented in `OidMapService` to support validation. This is a small, focused change to the existing service.

**Primary recommendation:** Follow the DevicesOptions/DevicesOptionsValidator pattern exactly. Add a `ContainsMetricName` method to `IOidMapService`. Use custom-only validation (no DataAnnotations) since all rules require cross-field or cross-service checks.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Options | 9.0.0 | `IValidateOptions<T>`, `ValidateOnStart()` | Already used for all config in project |
| Microsoft.Extensions.Configuration | 9.0.0 | JSON config binding | Already used for devices.json, oidmaps.json |
| System.Net (BCL) | n/a | `IPAddress.TryParse` for IP validation | Already used in DevicesOptionsValidator |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xunit | 2.9.3 | Unit tests | Already in test project |
| NSubstitute | 5.3.0 | Mock IOidMapService in validator tests | Already in test project |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom-only validator | DataAnnotations + Custom | DataAnnotations add complexity for zero benefit — all rules need cross-field or service-injected checks |
| Manual IP parse | `[RegularExpression]` | Regex is fragile for IPs; `IPAddress.TryParse` is the proven pattern in this codebase |

**Installation:** No new packages required.

## Architecture Patterns

### Recommended File Structure
```
src/SnmpCollector/
├── Configuration/
│   ├── TenantVectorOptions.cs        # Top-level wrapper (binds "Tenants" section)
│   ├── TenantOptions.cs              # Single tenant: Id, Priority, Metrics[]
│   ├── MetricSlotOptions.cs          # Single metric: Ip, Port, MetricName, IntervalSeconds
│   └── Validators/
│       └── TenantVectorOptionsValidator.cs  # IValidateOptions<TenantVectorOptions>
├── config/
│   └── tenantvector.json             # Dev config file
tests/SnmpCollector.Tests/
└── Configuration/
    └── TenantVectorOptionsValidatorTests.cs
```

### Pattern 1: POCO Hierarchy with SectionName Constant

**What:** Each options class defines `public const string SectionName` for binding. The top-level wrapper class contains a list property matching the JSON array structure.

**When to use:** Every config section in this project.

**Example (from existing codebase):**
```csharp
// DevicesOptions pattern — replicate for TenantVectorOptions
public sealed class TenantVectorOptions
{
    public const string SectionName = "TenantVector";
    public List<TenantOptions> Tenants { get; set; } = [];
}

public sealed class TenantOptions
{
    public string Id { get; set; } = string.Empty;
    public int Priority { get; set; }
    public List<MetricSlotOptions> Metrics { get; set; } = [];
}

public sealed class MetricSlotOptions
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public string MetricName { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; }
}
```

### Pattern 2: Collect-All-Errors Validator with DI Injection

**What:** Validator implements `IValidateOptions<T>`, collects all failures into `List<string>` with path context `Tenants[i].Property`, and returns `Fail(failures)` or `Success`. Service dependencies (IOidMapService) are constructor-injected.

**When to use:** For validators that need access to runtime services or cross-field validation.

**Example (from existing DevicesOptionsValidator + extension for IOidMapService injection):**
```csharp
public sealed class TenantVectorOptionsValidator : IValidateOptions<TenantVectorOptions>
{
    private readonly IOidMapService _oidMapService;

    public TenantVectorOptionsValidator(IOidMapService oidMapService)
    {
        _oidMapService = oidMapService;
    }

    public ValidateOptionsResult Validate(string? name, TenantVectorOptions options)
    {
        var failures = new List<string>();
        // ... collect all errors ...
        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
```

### Pattern 3: Config Binding for Nested Object (not bare array)

**What:** `tenantvector.json` uses a `{ "Tenants": [...] }` wrapper (not a bare array like devices.json). This means standard `.Bind()` works — no need for the `Configure<IConfiguration>` delegate pattern used for DevicesOptions.

**When to use:** When JSON has a named root object wrapping the array.

**Registration in AddSnmpConfiguration:**
```csharp
// Standard Bind — "TenantVector" section is a JSON object with "Tenants" array
services.AddOptions<TenantVectorOptions>()
    .Bind(configuration.GetSection(TenantVectorOptions.SectionName))
    .ValidateOnStart();

services.AddSingleton<IValidateOptions<TenantVectorOptions>, TenantVectorOptionsValidator>();
```

**Important distinction from DevicesOptions:** `devices.json` is a bare JSON array (no wrapper), which required the `Configure<IConfiguration>` delegate pattern. `tenantvector.json` has `{ "Tenants": [...] }` wrapper object, so standard `.Bind()` works directly.

### Pattern 4: Config File Loading in Program.cs (Local Dev)

**What:** For local dev (non-K8s), the config file is loaded from the `config/` directory in `Program.cs` after build, similar to how `devices.json` and `oidmaps.json` are loaded.

**Key decision:** tenantvector.json can be loaded via the standard configuration provider chain (AddJsonFile in Program.cs before build) since it uses a named section. Unlike devices.json (bare array needing manual deserialization), the `TenantVector` section can be bound automatically.

### Anti-Patterns to Avoid
- **DataAnnotations for cross-field rules:** The codebase uses custom IValidateOptions for any validation beyond simple required/range. MetricName-in-OidMap requires IOidMapService — DataAnnotations cannot inject services.
- **Bare JSON array for new config:** devices.json uses a bare array which caused binding complexity. tenantvector.json correctly uses `{ "Tenants": [...] }` wrapper.
- **Throwing on first error:** Always collect all errors. The `failures.Add()` + final check pattern is mandatory.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| IP address validation | Regex or manual parsing | `IPAddress.TryParse()` | Handles IPv4 and IPv6 correctly; proven in DevicesOptionsValidator |
| Config binding | Manual JSON deserialization | `IOptions<T>` + `.Bind()` | Built-in support for hot reload, validation, DI |
| Startup validation | Manual checks in Program.cs | `ValidateOnStart()` + `IValidateOptions<T>` | Integrates with OptionsValidationException catch block in Program.cs |
| Duplicate detection | Manual nested loops | `HashSet<T>.Add()` returns false | Clean O(n) duplicate detection; pattern from DevicesOptionsValidator |

**Key insight:** The entire options validation infrastructure is already built and working in this codebase. Phase 25 adds new options classes and a new validator following the exact same patterns.

## Common Pitfalls

### Pitfall 1: IOidMapService Has No Reverse Lookup
**What goes wrong:** The validator needs to check if a `MetricName` exists in the OID map, but `IOidMapService.Resolve(oid)` only goes OID -> name. There is no name -> exists check.
**Why it happens:** The OID map was designed for pipeline use (resolve incoming OIDs), not config validation.
**How to avoid:** Add `bool ContainsMetricName(string metricName)` to `IOidMapService`. Implement in `OidMapService` by checking `_map.Values.Contains(metricName)` or maintaining a reverse `FrozenSet<string>` of known metric names for O(1) lookup.
**Warning signs:** Validator cannot compile without this method.
**Recommendation:** Use a `FrozenSet<string>` of metric name values, atomically swapped alongside the main map in `UpdateMap()`. Linear scan of `_map.Values` would work but is O(n) per metric slot validated — a frozen set is O(1) and the map is already being rebuilt on reload anyway.

### Pitfall 2: Validator Registered Before OidMapService Is Populated
**What goes wrong:** `ValidateOnStart()` fires during host startup. If the OID map is still empty (initial state), every MetricName will fail validation.
**Why it happens:** In K8s mode, `OidMapWatcherService` populates the OID map asynchronously after startup. In local dev, `oidmaps.json` is loaded in `Program.cs` after `builder.Build()`.
**How to avoid:** Two options:
1. Load tenantvector.json config via `AddJsonFile` before `Build()` so the `TenantVector` section is available to the options system, but defer MetricName validation to a later point (not ValidateOnStart).
2. Accept that ValidateOnStart fires after local dev oidmap loading in Program.cs — check if this ordering works.
**Recommendation:** The safest approach is to NOT use `ValidateOnStart()` for the MetricName-in-OidMap check specifically, OR to ensure the OID map is seeded before ValidateOnStart fires. Given the current architecture where `ValidateOnStart` fires during `app.RunAsync()` and local dev loads oidmaps before that call, the ordering should work. However, in K8s mode the OID map is loaded asynchronously by `OidMapWatcherService` which starts during `RunAsync`, so the map may be empty when validation fires. **This needs careful handling:** either skip MetricName validation when the OID map is empty (with a warning log), or accept that K8s validation happens against whatever map state exists at startup time.

### Pitfall 3: Config File Location and Loading
**What goes wrong:** tenantvector.json not found at runtime.
**Why it happens:** New config file needs to be added to the configuration provider chain.
**How to avoid:** Add `tenantvector.json` to the config directory and register it via `AddJsonFile` in Program.cs, following the same pattern as `appsettings.k8s.json`. Alternatively, embed the `TenantVector` section inside `appsettings.json` (simpler but mixes concerns). Given that devices.json and oidmaps.json are separate files loaded manually, tenantvector.json should follow the same pattern for consistency.
**Recommendation:** Load tenantvector.json via `AddJsonFile` before `Build()` so it participates in normal options binding. This is simpler than devices.json (which needed manual deserialization) because tenantvector.json uses a named section object.

### Pitfall 4: Duplicate Detection Across vs Within Tenants
**What goes wrong:** Confusing "duplicate tenant IDs" (global) vs "duplicate (ip, port, metric_name) within a tenant" (per-tenant) vs "overlapping metrics across tenants" (allowed).
**Why it happens:** Three different duplicate scopes with different rules.
**How to avoid:** Clearly separate validation methods:
- `ValidateUniqueTenantIds()` — global HashSet across all tenants
- `ValidateNoDuplicateMetrics(tenant, index)` — per-tenant HashSet of (ip, port, metric_name) tuples
- Do NOT check cross-tenant metric overlap (it is explicitly allowed per success criteria #3)

### Pitfall 5: JSON Property Naming
**What goes wrong:** JSON uses PascalCase (`MetricName`) but C# property binding is case-insensitive by default. However, the JSON example in CONTEXT.md uses PascalCase throughout.
**Why it happens:** .NET configuration binding is case-insensitive, so this is not actually a problem.
**How to avoid:** Use PascalCase in both C# properties and JSON. Configuration binding handles case-insensitivity automatically.

## Code Examples

### TenantVectorOptions POCO Hierarchy
```csharp
// Source: follows DevicesOptions/DeviceOptions pattern from codebase
namespace SnmpCollector.Configuration;

public sealed class TenantVectorOptions
{
    public const string SectionName = "TenantVector";
    public List<TenantOptions> Tenants { get; set; } = [];
}

public sealed class TenantOptions
{
    public string Id { get; set; } = string.Empty;
    public int Priority { get; set; }
    public List<MetricSlotOptions> Metrics { get; set; } = [];
}

public sealed class MetricSlotOptions
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public string MetricName { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; }
}
```

### IOidMapService Extension for Reverse Lookup
```csharp
// Add to IOidMapService interface
/// <summary>
/// Checks whether a metric name exists as a value in the current OID map.
/// Used by TenantVectorOptionsValidator to verify config MetricName references.
/// </summary>
bool ContainsMetricName(string metricName);
```

```csharp
// Add to OidMapService implementation
private volatile FrozenSet<string> _metricNames = FrozenSet<string>.Empty;

public bool ContainsMetricName(string metricName)
{
    return _metricNames.Contains(metricName);
}

// In UpdateMap, after building _map:
_metricNames = newMap.Values.ToFrozenSet();

// In constructor, after building _map:
_metricNames = _map.Values.ToFrozenSet();
```

### Validator with IOidMapService Injection
```csharp
// Source: follows DevicesOptionsValidator pattern from codebase
public sealed class TenantVectorOptionsValidator : IValidateOptions<TenantVectorOptions>
{
    private readonly IOidMapService _oidMapService;

    public TenantVectorOptionsValidator(IOidMapService oidMapService)
    {
        _oidMapService = oidMapService;
    }

    public ValidateOptionsResult Validate(string? name, TenantVectorOptions options)
    {
        var failures = new List<string>();

        // Global: unique tenant IDs
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < options.Tenants.Count; i++)
        {
            var tenant = options.Tenants[i];
            ValidateTenant(tenant, i, failures);

            if (!string.IsNullOrWhiteSpace(tenant.Id) && !seenIds.Add(tenant.Id))
                failures.Add($"Tenants[{i}].Id '{tenant.Id}' is a duplicate");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private void ValidateTenant(TenantOptions tenant, int index, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(tenant.Id))
            failures.Add($"Tenants[{index}].Id is required");

        // Empty Metrics[] is valid (per CONTEXT.md decision)

        var seenMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < tenant.Metrics.Count; j++)
        {
            var metric = tenant.Metrics[j];
            ValidateMetricSlot(metric, index, j, failures);

            // Per-tenant duplicate (ip, port, metric_name)
            var key = $"{metric.Ip}:{metric.Port}:{metric.MetricName}";
            if (!string.IsNullOrWhiteSpace(metric.Ip) &&
                !string.IsNullOrWhiteSpace(metric.MetricName) &&
                !seenMetrics.Add(key))
            {
                failures.Add($"Tenants[{index}].Metrics[{j}] ({key}) is a duplicate within this tenant");
            }
        }
    }

    private void ValidateMetricSlot(MetricSlotOptions metric, int tenantIndex, int metricIndex, List<string> failures)
    {
        var prefix = $"Tenants[{tenantIndex}].Metrics[{metricIndex}]";

        if (string.IsNullOrWhiteSpace(metric.Ip))
            failures.Add($"{prefix}.Ip is required");
        else if (!System.Net.IPAddress.TryParse(metric.Ip, out _))
            failures.Add($"{prefix}.Ip '{metric.Ip}' is not a valid IP address");

        if (metric.Port < 1 || metric.Port > 65535)
            failures.Add($"{prefix}.Port must be between 1 and 65535");

        if (string.IsNullOrWhiteSpace(metric.MetricName))
            failures.Add($"{prefix}.MetricName is required");
        else if (!_oidMapService.ContainsMetricName(metric.MetricName))
            failures.Add($"{prefix}.MetricName '{metric.MetricName}' not found in OID map");

        if (metric.IntervalSeconds <= 0)
            failures.Add($"{prefix}.IntervalSeconds must be greater than 0");
    }
}
```

### DI Registration in AddSnmpConfiguration
```csharp
// Add to AddSnmpConfiguration method in ServiceCollectionExtensions.cs
services.AddOptions<TenantVectorOptions>()
    .Bind(configuration.GetSection(TenantVectorOptions.SectionName))
    .ValidateOnStart();

services.AddSingleton<IValidateOptions<TenantVectorOptions>, TenantVectorOptionsValidator>();
```

### tenantvector.json Example (Dev Config)
```json
{
  "TenantVector": {
    "Tenants": [
      {
        "Id": "fiber-monitor",
        "Priority": 1,
        "Metrics": [
          {
            "Ip": "127.0.0.1",
            "Port": 10161,
            "MetricName": "obp_link_state_L1",
            "IntervalSeconds": 10
          }
        ]
      },
      {
        "Id": "traffic-baseline",
        "Priority": 2,
        "Metrics": [
          {
            "Ip": "127.0.0.1",
            "Port": 10162,
            "MetricName": "npb_cpu_util",
            "IntervalSeconds": 30
          }
        ]
      }
    ]
  }
}
```

### Unit Test Pattern (NSubstitute for IOidMapService)
```csharp
public sealed class TenantVectorOptionsValidatorTests
{
    private readonly IOidMapService _oidMapService = Substitute.For<IOidMapService>();
    private readonly TenantVectorOptionsValidator _sut;

    public TenantVectorOptionsValidatorTests()
    {
        // Default: all metric names are valid
        _oidMapService.ContainsMetricName(Arg.Any<string>()).Returns(true);
        _sut = new TenantVectorOptionsValidator(_oidMapService);
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsSuccess()
    {
        var options = new TenantVectorOptions
        {
            Tenants = [new TenantOptions
            {
                Id = "test-tenant",
                Priority = 1,
                Metrics = [new MetricSlotOptions
                {
                    Ip = "10.0.0.1",
                    Port = 161,
                    MetricName = "obp_link_state_L1",
                    IntervalSeconds = 10
                }]
            }]
        };

        var result = _sut.Validate(null, options);

        Assert.True(result.Succeeded);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual validation in startup | `IValidateOptions<T>` + `ValidateOnStart()` | .NET 6+ | Fail-fast with structured error messages |
| `FrozenDictionary` not available | `FrozenDictionary` / `FrozenSet` for immutable lookup | .NET 8+ | O(1) lookup with zero ongoing allocation |

**Deprecated/outdated:**
- `IOptionsValidator` (older interface): replaced by `IValidateOptions<T>` — the project already uses the current pattern

## Open Questions

1. **OID Map Empty at ValidateOnStart in K8s Mode**
   - What we know: In K8s, OidMapWatcherService populates the map asynchronously during RunAsync. ValidateOnStart also fires during RunAsync. The ordering is not guaranteed.
   - What's unclear: Whether the OID map is populated before ValidateOnStart fires in K8s mode.
   - Recommendation: Either (a) skip MetricName validation when OidMapService.EntryCount == 0 with a log warning (graceful degradation), or (b) do not validate MetricName against OID map at startup — defer to runtime. Option (a) is pragmatic: in production, if the OID map is empty, no metrics will resolve anyway. The validator adds value for catching typos when the map IS loaded.

2. **Config File Loading Strategy**
   - What we know: tenantvector.json is a new config file. The CONTEXT.md example shows `{ "Tenants": [...] }` wrapper structure.
   - What's unclear: Whether to add it via `AddJsonFile` in the config chain (like appsettings.k8s.json) or load it manually in Program.cs (like devices.json/oidmaps.json).
   - Recommendation: Use `AddJsonFile` since the wrapper object makes standard binding work. Add it in the config directory loading block in Program.cs, before `Build()`. In K8s, it would be delivered via ConfigMap like the other config files.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: `DevicesOptions`, `DevicesOptionsValidator`, `OidMapService`, `IOidMapService`, `ServiceCollectionExtensions.cs` — all patterns verified from source code
- Codebase analysis: `LeaseOptionsValidator`, `OtlpOptionsValidator` — DI-injected validator pattern verified
- Codebase analysis: Test project uses xunit 2.9.3 + NSubstitute 5.3.0, .NET 9.0

### Secondary (MEDIUM confidence)
- .NET Options validation pattern (`IValidateOptions<T>`, `ValidateOnStart()`) — well-established in .NET 6+ and already used extensively in this codebase
- `FrozenSet<T>` available in .NET 8+ — project targets net9.0

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — zero new packages, all patterns from existing codebase
- Architecture: HIGH — direct replication of DevicesOptions/DevicesOptionsValidator pattern
- Pitfalls: HIGH — IOidMapService gap identified from source code analysis; K8s ordering concern is MEDIUM

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (stable — .NET options pattern is mature)
