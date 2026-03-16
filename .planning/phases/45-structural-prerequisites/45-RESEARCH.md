# Phase 45: Structural Prerequisites - Research

**Researched:** 2026-03-16
**Domain:** C# model/enum additions — MetricSlotHolder, Tenant, SnmpSource, OidResolutionBehavior
**Confidence:** HIGH

---

## Summary

Phase 45 closes three data-propagation gaps that block all SnapshotJob evaluation logic. Every change is a pure model addition: no new behaviors, no new services, no new DI registrations. The three gaps are (1) `MetricSlotHolder.Role` — the `Role` field exists in `MetricSlotOptions` but is never passed to the constructor or stored on the holder; (2) `Tenant.Commands` — `TenantOptions.Commands` is populated at config load but the runtime `Tenant` class has no `Commands` property; and (3) `SnmpSource.Command` — the enum has `Poll`, `Trap`, `Synthetic` but no `Command` value, which all subsequent pipeline work requires.

All three changes are mechanical: add a constructor parameter, store it on a read-only property, pass the value from `TenantVectorRegistry.Reload`. The only non-trivial work is the `OidResolutionBehavior` refactor, where the existing `Source == Synthetic` bypass is replaced with a data-driven `MetricName already set and valid` guard. This is architecturally cleaner and means `SnmpSource.Command` does NOT need any bypass — it flows through full OID resolution the same as `Poll` and `Trap` (command map pre-sets `MetricName` in a later phase; the guard handles it then).

**Primary recommendation:** Make all three additions in order — `SnmpSource.Command` first (zero dependencies), then `MetricSlotHolder.Role` (constructor + Reload update), then `Tenant.Commands` (constructor + Reload update), then `OidResolutionBehavior` refactor. Each step is independently compilable and testable.

---

## Standard Stack

No new libraries or packages are required for this phase. All changes are within existing source files.

### Files Changed

| File | Change Type | What |
|------|-------------|------|
| `src/SnmpCollector/Pipeline/SnmpSource.cs` | Enum value added | `Command` value |
| `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` | Constructor param + property | `string Role { get; }` |
| `src/SnmpCollector/Pipeline/Tenant.cs` | Constructor param + property | `IReadOnlyList<CommandSlotOptions> Commands { get; }` |
| `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` | Reload update | Pass `Role` to holder ctor; pass `Commands` to `Tenant` ctor |
| `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` | Bypass guard refactor | Replace `Source == Synthetic` with `MetricName already set and valid` |
| `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` | Test additions | Role propagation tests; Commands propagation tests |
| `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` | Test additions + updates | MetricName-guard tests; rename/update Synthetic tests |

---

## Architecture Patterns

### Pattern 1: MetricSlotHolder Constructor — Add Role as Immutable Parameter

**What:** `Role` is an immutable config-time property identical in nature to `Ip`, `Port`, `MetricName`. It is set once in the constructor and never changes. The existing `CopyFrom` method transfers mutable state (TypeCode, Source, time series) — it does NOT need to copy `Role` because `Role` is set from the constructor at build time.

**Current constructor signature:**
```csharp
// Source: src/SnmpCollector/Pipeline/MetricSlotHolder.cs
public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds,
    int timeSeriesSize = 1, double graceMultiplier = 2.0, ThresholdOptions? threshold = null)
```

**New constructor signature (Role added as required parameter before the optionals):**
```csharp
public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds,
    string role, int timeSeriesSize = 1, double graceMultiplier = 2.0,
    ThresholdOptions? threshold = null)
{
    // ...
    Role = role;
}
public string Role { get; }
```

**Ordering rationale:** `role` is required (no default), so it must precede all optional parameters. Inserting it after `intervalSeconds` and before the optional parameters is the cleanest position that does not disrupt existing callers that already pass `intervalSeconds` positionally. All callers are in `TenantVectorRegistry.Reload` (one call site) and tests — both are easily updated.

**CopyFrom does not need changes:** `Role` is immutable. `CopyFrom` only copies mutable state. The new holder already has its `Role` from the constructor when `CopyFrom` is called.

### Pattern 2: TenantVectorRegistry.Reload — Pass Role from MetricSlotOptions

**What:** The constructor call in `Reload` at line 88–95 currently passes all fields from `metric` (a `MetricSlotOptions`) except `Role`. Adding `role: metric.Role` completes the propagation.

```csharp
// Source: src/SnmpCollector/Pipeline/TenantVectorRegistry.cs, Step 3
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.Role,           // <-- add this
    metric.TimeSeriesSize,
    metric.GraceMultiplier,
    metric.Threshold);
```

### Pattern 3: Tenant — Add Commands as Immutable Property

**What:** `Tenant` is a simple immutable value object. `Commands` follows the exact same pattern as `Holders` — passed in from `TenantVectorRegistry.Reload`, stored as `IReadOnlyList<T>`.

**Current Tenant constructor:**
```csharp
// Source: src/SnmpCollector/Pipeline/Tenant.cs
public Tenant(string id, int priority, IReadOnlyList<MetricSlotHolder> holders)
```

**New Tenant constructor:**
```csharp
using SnmpCollector.Configuration; // needed for CommandSlotOptions

public Tenant(string id, int priority, IReadOnlyList<MetricSlotHolder> holders,
    IReadOnlyList<CommandSlotOptions> commands)
{
    Id = id;
    Priority = priority;
    Holders = holders;
    Commands = commands;
}
public IReadOnlyList<CommandSlotOptions> Commands { get; }
```

**Reload call site** (currently line 112):
```csharp
// Source: src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
var tenant = new Tenant(tenantId, tenantOpts.Priority, holders, tenantOpts.Commands);
```

`tenantOpts.Commands` is `List<CommandSlotOptions>` which satisfies `IReadOnlyList<CommandSlotOptions>` — no conversion needed.

**On reload:** Commands are re-read from the new config (no carry-over needed, matching CONTEXT.md decision). The new `Tenant` instance gets the new config's commands list directly.

### Pattern 4: OidResolutionBehavior — MetricName-Already-Set Guard

**What:** Replace the `Source == Synthetic` bypass with a data-driven guard: if `MetricName` is already non-null and not `OidMapService.Unknown`, skip OID resolution. This is more general and makes `SnmpSource.Command` work correctly — the CommandWorker (Phase C) will pre-set `MetricName` from `ICommandMapService` before dispatching. The guard fires for both Synthetic and Command sources without any Source-specific conditions.

**Current behavior:**
```csharp
// Source: src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs (line 35)
if (msg.Source == SnmpSource.Synthetic) { return await next(); }

msg.MetricName = _oidMapService.Resolve(msg.Oid);
```

**New behavior:**
```csharp
// MetricName-already-set guard: covers Synthetic (pre-set by caller) and Command (pre-set by CommandWorker)
if (msg.MetricName is not null && msg.MetricName != OidMapService.Unknown)
{
    return await next();
}

msg.MetricName = _oidMapService.Resolve(msg.Oid);
```

**Why this is correct for all cases:**
- `Poll` and `Trap` messages: `MetricName` is null at construction (see `SnmpOidReceived` — no default). Guard does not fire. OID resolution runs. Behavior unchanged.
- `Synthetic` messages: Callers pre-set `MetricName` before dispatching. Guard fires. OID resolution bypassed. Behavior unchanged.
- `Command` messages (future): CommandWorker pre-sets `MetricName` from command map. Guard fires. OID resolution bypassed. Correct — command OIDs are not in `oid_metric_map`.
- Edge case: A `Poll` message where `MetricName` was pre-set to `OidMapService.Unknown` — the guard explicitly excludes `Unknown`, so OID resolution still runs. Correct.

**Note on Phase 45 scope:** `SnmpSource.Command` is added in this phase, but the guard change in `OidResolutionBehavior` does not require `SnmpSource.Command` to exist — it is purely data-driven. Adding the guard now removes all Source-specific conditions from `OidResolutionBehavior` permanently.

### Anti-Patterns to Avoid

- **Do NOT add `Role` to `MetricSlot` record** — `MetricSlot` is an immutable time-series sample value (`Value`, `StringValue`, `Timestamp`). Role is a config-time property of the holder, not a per-sample attribute. Adding it to `MetricSlot` would bloat every sample in the ImmutableArray with redundant data.
- **Do NOT add `Role` to `CopyFrom`** — `Role` is set by the constructor at build time. `CopyFrom` only transfers mutable runtime state. Copying `Role` in `CopyFrom` would be a no-op at best, a bug source at worst (if the new holder has a different Role from config, `CopyFrom` would silently overwrite it).
- **Do NOT extend the Synthetic bypass with `|| Source == Command`** — the guard refactor replaces Source-based conditions entirely. Adding a new Source-specific condition would contradict the refactor's purpose.
- **Do NOT convert `Commands` to `ImmutableList` or similar** — `tenantOpts.Commands` is `List<CommandSlotOptions>`, which already satisfies `IReadOnlyList<T>`. The runtime `Tenant.Commands` property prevents mutation at the API level. No wrapping needed.

---

## Don't Hand-Roll

This phase has no algorithmic complexity. All patterns are direct property additions following existing conventions.

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Immutable read-only string property | Custom backing field + setter guard | `public string Role { get; }` set in constructor — C# compiler enforces immutability |
| List assignability | `.ToList()` / `.ToImmutableList()` | `List<T>` already satisfies `IReadOnlyList<T>` — assign directly |

---

## Common Pitfalls

### Pitfall 1: Constructor Parameter Order Breaks Existing Test Data

**What goes wrong:** `MetricSlotHolder` has optional parameters after `intervalSeconds`. Inserting `role` before them shifts positional argument positions. Any test or factory that passes optional arguments positionally (e.g., `new MetricSlotHolder("ip", 161, "metric", 30, 1, 2.0, null)`) will silently pass `timeSeriesSize=1` into `role`, compiling but producing wrong data.

**Why it happens:** C# allows positional arguments to match any parameter by position, so `1` (an int) would fail to compile for `string role` — actually this produces a compile error, not a silent bug. Any test passing `timeSeriesSize` positionally will get a compile error and must be updated.

**How to avoid:** Place `role` as a required non-optional `string` parameter after `intervalSeconds` and before the optional parameters. Compile errors will surface all callers that must be updated. Do not use a default value for `role`.

**Warning signs:** Compile errors at `new MetricSlotHolder(...)` call sites in test files — these must all be fixed, not suppressed.

### Pitfall 2: TenantVectorRegistryTests Helper Does Not Pass Role

**What goes wrong:** The `CreateOptions` helper in `TenantVectorRegistryTests` creates `MetricSlotOptions` with `Role = "Evaluate"` or `Role = "Resolved"` already. The `MetricSlotHolder` constructor call in `Reload` is updated to pass `metric.Role`. The test helper already sets `Role` on the options, so no test data change is needed. However, any test that directly constructs `MetricSlotHolder` with positional args will break.

**How to avoid:** After adding the `role` parameter, search for `new MetricSlotHolder(` in tests and update each call site to pass an explicit `role:` named argument or positional `"Evaluate"`.

### Pitfall 3: Tenant Constructor Call Sites in Tests

**What goes wrong:** Tests may construct `Tenant` directly with three arguments. After adding `commands` as a required 4th parameter, those calls will not compile.

**How to avoid:** After updating `Tenant`, search all test files for `new Tenant(` and add `commands: Array.Empty<CommandSlotOptions>()` or `commands: []` as appropriate for test data.

### Pitfall 4: OidResolutionBehavior Test for Synthetic Source Semantics

**What goes wrong:** The existing test `SyntheticMessage_BypassesOidResolution_MetricNamePreserved` tests the Synthetic bypass by checking that MetricName is preserved. After the refactor, the bypass still works but via the MetricName-guard, not via `Source == Synthetic`. The test remains valid. However, there are now two distinct cases: (a) Synthetic with pre-set MetricName — guard fires correctly; (b) Synthetic without pre-set MetricName — guard does NOT fire, OID resolution runs and will likely set `MetricName = OidMapService.Unknown`. Case (b) is not a new bug (it happens today too — Synthetic with no MetricName goes through resolution unchanged), but the test name may mislead.

**How to avoid:** The existing tests remain valid as-is. Add one new test: `CommandSource_WithPresetMetricName_BypassesOidResolution` to verify the same MetricName-guard behavior for a `Source = Command` message with `MetricName` pre-set.

### Pitfall 5: Missing `using` in Tenant.cs for CommandSlotOptions

**What goes wrong:** `Tenant.cs` currently has no `using SnmpCollector.Configuration;`. Adding `IReadOnlyList<CommandSlotOptions> Commands` requires it.

**How to avoid:** Add `using SnmpCollector.Configuration;` at the top of `Tenant.cs`.

---

## Code Examples

### MetricSlotHolder after change

```csharp
// Additions to src/SnmpCollector/Pipeline/MetricSlotHolder.cs
public string Role { get; }

public MetricSlotHolder(string ip, int port, string metricName, int intervalSeconds,
    string role, int timeSeriesSize = 1, double graceMultiplier = 2.0,
    ThresholdOptions? threshold = null)
{
    Ip = ip;
    Port = port;
    MetricName = metricName;
    IntervalSeconds = intervalSeconds;
    Role = role;
    TimeSeriesSize = timeSeriesSize;
    GraceMultiplier = graceMultiplier;
    Threshold = threshold;
}
```

### Tenant after change

```csharp
// src/SnmpCollector/Pipeline/Tenant.cs
using SnmpCollector.Configuration;

public sealed class Tenant
{
    public string Id { get; }
    public int Priority { get; }
    public IReadOnlyList<MetricSlotHolder> Holders { get; }
    public IReadOnlyList<CommandSlotOptions> Commands { get; }

    public Tenant(string id, int priority, IReadOnlyList<MetricSlotHolder> holders,
        IReadOnlyList<CommandSlotOptions> commands)
    {
        Id = id;
        Priority = priority;
        Holders = holders;
        Commands = commands;
    }
}
```

### SnmpSource after change

```csharp
// src/SnmpCollector/Pipeline/SnmpSource.cs
public enum SnmpSource
{
    Poll,
    Trap,
    Synthetic,
    Command
}
```

### TenantVectorRegistry.Reload — MetricSlotHolder call site

```csharp
// src/SnmpCollector/Pipeline/TenantVectorRegistry.cs — Step 3, inside the metrics loop
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.Role,           // propagate from MetricSlotOptions
    metric.TimeSeriesSize,
    metric.GraceMultiplier,
    metric.Threshold);
```

### TenantVectorRegistry.Reload — Tenant call site

```csharp
// src/SnmpCollector/Pipeline/TenantVectorRegistry.cs — Step 3, after building holders list
var tenant = new Tenant(tenantId, tenantOpts.Priority, holders, tenantOpts.Commands);
```

### OidResolutionBehavior — refactored bypass guard

```csharp
// src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
public async Task<TResponse> Handle(
    TNotification notification,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken cancellationToken)
{
    if (notification is SnmpOidReceived msg)
    {
        // Data-driven guard: MetricName already resolved (Synthetic pre-sets it; Command path will too).
        // Replaces Source-specific bypass conditions — no Source checks in OidResolutionBehavior.
        if (msg.MetricName is not null && msg.MetricName != OidMapService.Unknown)
        {
            return await next();
        }

        msg.MetricName = _oidMapService.Resolve(msg.Oid);

        if (msg.MetricName == OidMapService.Unknown)
            _logger.LogDebug("OID {Oid} not found in OidMap", msg.Oid);
        else
            _logger.LogDebug("OID {Oid} resolved to {MetricName}", msg.Oid, msg.MetricName);
    }

    return await next();
}
```

### Test: Role propagation in TenantVectorRegistryTests

```csharp
[Fact]
public void Reload_RoleFromConfig_StoredInHolder()
{
    var registry = CreateRegistry();
    var options = new TenantVectorOptions
    {
        Tenants =
        [
            new TenantOptions
            {
                Priority = 1,
                Metrics =
                [
                    new MetricSlotOptions { Ip = "10.0.0.1", Port = 161, MetricName = "cpu", Role = "Evaluate" },
                    new MetricSlotOptions { Ip = "10.0.0.1", Port = 161, MetricName = "linkState", Role = "Resolved" }
                ],
                Commands = []
            }
        ]
    };

    registry.Reload(options);

    registry.TryRoute("10.0.0.1", 161, "cpu", out var evaluateHolders);
    Assert.Equal("Evaluate", evaluateHolders![0].Role);

    registry.TryRoute("10.0.0.1", 161, "linkState", out var resolvedHolders);
    Assert.Equal("Resolved", resolvedHolders![0].Role);
}
```

### Test: Commands propagation in TenantVectorRegistryTests

```csharp
[Fact]
public void Reload_CommandsFromConfig_StoredOnTenant()
{
    var registry = CreateRegistry();
    var options = new TenantVectorOptions
    {
        Tenants =
        [
            new TenantOptions
            {
                Priority = 1,
                Metrics =
                [
                    new MetricSlotOptions { Ip = "10.0.0.1", Port = 161, MetricName = "cpu", Role = "Evaluate" },
                    new MetricSlotOptions { Ip = "10.0.0.1", Port = 161, MetricName = "linkState", Role = "Resolved" }
                ],
                Commands =
                [
                    new CommandSlotOptions { Ip = "10.0.0.2", Port = 161, CommandName = "setSpeed", Value = "100", ValueType = "Integer32" }
                ]
            }
        ]
    };

    registry.Reload(options);

    var tenant = registry.Groups[0].Tenants[0];
    Assert.Single(tenant.Commands);
    Assert.Equal("setSpeed", tenant.Commands[0].CommandName);
    Assert.Equal("10.0.0.2", tenant.Commands[0].Ip);
}

[Fact]
public void Reload_NoCommands_TenantCommandsIsEmpty()
{
    var registry = CreateRegistry();
    var options = new TenantVectorOptions
    {
        Tenants =
        [
            new TenantOptions
            {
                Priority = 1,
                Metrics =
                [
                    new MetricSlotOptions { Ip = "10.0.0.1", Port = 161, MetricName = "cpu", Role = "Evaluate" },
                    new MetricSlotOptions { Ip = "10.0.0.1", Port = 161, MetricName = "linkState", Role = "Resolved" }
                ],
                Commands = []
            }
        ]
    };

    registry.Reload(options);

    var tenant = registry.Groups[0].Tenants[0];
    Assert.Empty(tenant.Commands);
}
```

### Test: OidResolutionBehavior — Command source with pre-set MetricName

```csharp
[Fact]
public async Task CommandSource_WithPresetMetricName_BypassesOidResolution()
{
    var oidMapService = new StubOidMapService(knownOid: "1.3.6.1.2.1.25.3.3.1.2", metricName: "hrProcessorLoad");
    var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(
        oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
    var notification = MakeNotification("0.0.1.2", SnmpSource.Command);
    notification.MetricName = "setSpeed";  // pre-set by CommandWorker

    await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

    Assert.Equal("setSpeed", notification.MetricName);  // guard fired, OidMapService never called
}
```

---

## Claude's Discretion Recommendations

### Role on MetricSlot record?

**Recommendation: Keep Role on MetricSlotHolder only, not on MetricSlot.**

`MetricSlot` is a time-series sample: `(Value, StringValue, Timestamp)`. Role is a config-time classification of the holder, not a per-observation attribute. Every sample in a holder has the same Role (the holder's role never changes). Adding Role to `MetricSlot` would:
- Bloat every ImmutableArray element with a redundant string reference
- Create conceptual confusion (Role is a holder concern, not a sample concern)
- Require updating `WriteValue`, `CopyFrom`, and every `MetricSlot` construction site

SnapshotJob evaluates holders, not individual samples — it checks `holder.Role`, then calls `holder.ReadSlot()`. Role on the holder is exactly where it needs to be.

### Constructor parameter ordering for MetricSlotHolder?

**Recommendation:** Place `role` immediately after `intervalSeconds` (the last required parameter), before all optional parameters.

Rationale: `role` must be required (no sensible default; always required per CONTEXT.md decision). Required parameters must precede optional ones in C#. Placing it immediately after `intervalSeconds` means all existing callers that do NOT pass optional parameters positionally will only need to add one argument. Named-argument call sites need no reordering. This is the minimally disruptive position.

### Test organization?

**Recommendation:** Add new tests as numbered sections in existing test classes (following the existing `//──` section pattern). Do not create new test files.

- `TenantVectorRegistryTests.cs` section 13: Role propagation (2 tests: Evaluate stored, Resolved stored)
- `TenantVectorRegistryTests.cs` section 14: Commands propagation (2 tests: with commands, empty commands)
- `OidResolutionBehaviorTests.cs`: Add 2 new tests at the end — `CommandSource_WithPresetMetricName_BypassesOidResolution`, `CommandSource_WithPresetMetricName_StillCallsNext`. Rename/update existing `SyntheticMessage_BypassesOidResolution_MetricNamePreserved` test name to remain accurate (behavior unchanged; the bypass still works via MetricName guard).

---

## State of the Art

No external library changes. All changes are within codebase source files following established patterns.

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Source == Synthetic` bypass in OidResolutionBehavior | MetricName-already-set guard | Phase 45 | Removes Source coupling; covers Command source automatically |
| Role only in MetricSlotOptions | Role in MetricSlotOptions AND MetricSlotHolder | Phase 45 | SnapshotJob can partition holders without touching config objects |
| Commands only in TenantOptions | Commands in TenantOptions AND Tenant | Phase 45 | SnapshotJob can access command targets at evaluation time |

---

## Open Questions

None. CONTEXT.md resolves all previously open questions for this phase:

- Role type: **string** ("Evaluate"/"Resolved") — decided.
- Commands shape: **flat `IReadOnlyList<CommandSlotOptions>`** — decided.
- Bypass guard mechanism: **MetricName-already-set** — decided.
- OidResolutionBehavior Synthetic cleanup: **yes, replace entirely** — decided.

---

## Sources

### Primary (HIGH confidence — direct codebase reading)

- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — current constructor signature, `CopyFrom` pattern, existing read-only properties
- `src/SnmpCollector/Pipeline/Tenant.cs` — current constructor, `Holders` property pattern
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — current enum values
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `Reload` method, holder construction call site (lines 88–95), tenant construction call site (line 112)
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — current `Source == Synthetic` bypass (line 35), full behavior logic
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — `Role` field exists at line 42
- `src/SnmpCollector/Configuration/TenantOptions.cs` — `Commands` field exists at line 31
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — full shape of `CommandSlotOptions`
- `src/SnmpCollector/Pipeline/MetricSlot.cs` — immutable sample record (no Role)
- `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — `MetricName` is nullable, no default (line 45)
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` — existing test patterns, `CreateOptions` helper
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` — existing Synthetic bypass tests
- `.planning/research/SUMMARY.md` — Phase A structural gaps analysis and build order
- `.planning/phases/45-structural-prerequisites/45-CONTEXT.md` — all locked decisions

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all changes are within existing source files
- Architecture: HIGH — all patterns derived from direct source reading; MetricSlotHolder, Tenant, TenantVectorRegistry, OidResolutionBehavior all read and understood
- Pitfalls: HIGH — all pitfalls derived from C# language rules and existing test structure, not speculation

**Research date:** 2026-03-16
**Valid until:** Stable — no external dependencies; valid until codebase is refactored
