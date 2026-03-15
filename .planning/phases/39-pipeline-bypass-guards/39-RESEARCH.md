# Phase 39: Pipeline Bypass Guards - Research

**Researched:** 2026-03-15
**Domain:** C# MediatR pipeline behaviors, enum extension, unit testing
**Confidence:** HIGH

## Summary

This phase is a small, surgical codebase change: add one enum member, insert one bypass guard in one behavior, and add tests covering those two changes. No external libraries are involved beyond what already exists in the project.

All three implementation decisions are pre-locked in CONTEXT.md. Research confirms they are technically sound and no alternatives need consideration. The regex in `ValidationBehavior` (`^\d+(\.\d+){1,}$`) provably accepts "0.0" — the sentinel OID clears both the OID-format check and (when `DeviceName` is populated) the DeviceName check without any code changes. The bypass guard placement in `OidResolutionBehavior` must be *inside* the `if (notification is SnmpOidReceived msg)` block to have access to `msg.Source`.

**Primary recommendation:** Three files to touch: `SnmpSource.cs` (add member), `OidResolutionBehavior.cs` (add guard), `OidResolutionBehaviorTests.cs` (add 2–3 tests). `ValidationBehaviorTests.cs` gains one test confirming "0.0" passes. No new files required.

## Standard Stack

### Core
| Component | Version | Purpose | Why Standard |
|-----------|---------|---------|--------------|
| C# enum | n/a | `SnmpSource.Synthetic` flag | Already-used pattern in codebase |
| MediatR IPipelineBehavior | Already in use | Pipeline guard bypass | Framework already in place |
| xUnit | Already in use | Unit tests for bypass guard | Project's test framework |

### Supporting
| Component | Version | Purpose | When to Use |
|-----------|---------|---------|-------------|
| NullLogger | Already in use | Behavior test construction | All behavior tests use this pattern |
| StubOidMapService | In OidResolutionBehaviorTests.cs | Isolated unit test | Reuse for Synthetic bypass tests |

No new packages are required. This phase adds no new dependencies.

## Architecture Patterns

### Recommended Project Structure
```
src/SnmpCollector/Pipeline/
├── SnmpSource.cs                      # Add Synthetic member here
└── Behaviors/
    └── OidResolutionBehavior.cs       # Add bypass guard here

tests/SnmpCollector.Tests/Pipeline/Behaviors/
├── OidResolutionBehaviorTests.cs      # Add Synthetic bypass tests here
└── ValidationBehaviorTests.cs         # Add "0.0" sentinel passthrough test here
```

### Pattern 1: Enum Extension
**What:** Add `Synthetic` as a third member to the existing `SnmpSource` enum.
**When to use:** Adding a new source variant that must be distinguishable from Poll and Trap throughout the pipeline.
**Example:**
```csharp
// Source: src/SnmpCollector/Pipeline/SnmpSource.cs (current state)
public enum SnmpSource
{
    Poll,
    Trap,
    Synthetic   // ← add this
}
```

### Pattern 2: Early-Return Bypass Guard
**What:** Insert a guard inside the `if (notification is SnmpOidReceived msg)` block of `OidResolutionBehavior.Handle`, before the OID resolution logic.
**When to use:** When a message source requires pipeline continuation without executing the behavior's main logic.

**Critical placement decision:** The guard must be placed *inside* the `is SnmpOidReceived msg` cast block, where `msg` is already typed. It must come before `msg.MetricName = _oidMapService.Resolve(msg.Oid)`.

```csharp
// Source: src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
if (notification is SnmpOidReceived msg)
{
    // Locked decision from CONTEXT.md — placed before OID resolution
    if (msg.Source == SnmpSource.Synthetic) { await next(); return; }

    msg.MetricName = _oidMapService.Resolve(msg.Oid);
    // ... rest of existing logic
}

return await next();
```

**Important:** The existing `return await next()` at the bottom of `Handle` covers non-SnmpOidReceived notifications. The bypass inside the block calls `await next()` and returns to pass execution downstream without performing OID resolution. The return type is `Task<TResponse>` so `return` without a value is not allowed — `await next()` returns `TResponse`, which must be returned explicitly. The correct form is:

```csharp
if (msg.Source == SnmpSource.Synthetic) { return await next(); }
```

This is correct because `next()` returns `Task<TResponse>`, not `Task`. The locked decision in CONTEXT.md writes `await next(); return;` as pseudo-code intent; the actual implementation must `return await next();` to satisfy the return type.

### Pattern 3: Sentinel OID Validation Proof
**What:** Confirm "0.0" passes the existing `ValidationBehavior` without code changes.
**Regex:** `^\d+(\.\d+){1,}$` (RegexOptions.Compiled)
**Proof:**
- `^\d+` matches "0" (one digit)
- `(\.\d+){1,}` matches ".0" (one arc, satisfying minimum of 1)
- `$` end of string
- Result: "0.0" is accepted. No changes to ValidationBehavior needed.

The DeviceName check (`if (msg.DeviceName is null)`) means synthetic messages must have `DeviceName` set at construction to pass ValidationBehavior. Since ValidationBehavior runs *before* OidResolutionBehavior in the pipeline order (Logging → Exception → Validation → OidResolution → handler), synthetic messages must supply a DeviceName at publish time.

### Pattern 4: Existing Test Conventions
**What:** Tests in `OidResolutionBehaviorTests.cs` use a private `MakeNotification(string oid)` factory and a private `StubOidMapService` inner class. The Synthetic bypass tests should follow this exact pattern.
**Helper to reuse:**
```csharp
// Extend MakeNotification or add overload to pass Source:
private static SnmpOidReceived MakeNotification(string oid, SnmpSource source = SnmpSource.Poll) =>
    new()
    {
        Oid = oid,
        AgentIp = IPAddress.Parse("10.0.0.1"),
        Value = new Integer32(42),
        Source = source,
        TypeCode = SnmpType.Integer32,
        DeviceName = "test-device"
    };
```

### Anti-Patterns to Avoid
- **Guard placed outside the `is SnmpOidReceived` cast block:** Would require a separate cast or would miss type-safe access to `msg.Source`. The locked decision specifically targets the block after the cast.
- **Using `await next(); return;` without returning the result:** `Handle` returns `Task<TResponse>`, not `Task`. Must be `return await next();`.
- **Adding a bypass to ValidationBehavior:** CONTEXT.md explicitly states no changes to ValidationBehavior — "0.0" already passes.
- **Creating a new test file:** Extending the two existing behavior test files is sufficient and consistent with project style.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Source discrimination | Custom interface or tag on notification | `msg.Source == SnmpSource.Synthetic` check | `Source` property already exists on `SnmpOidReceived` |
| OID sentinel generation | OID factory or special type | Literal "0.0" string | Regex already accepts it; no new infrastructure |

**Key insight:** The entire feature is three lines of production code. No infrastructure, no new abstractions, no new services.

## Common Pitfalls

### Pitfall 1: Wrong Return Pattern in Bypass Guard
**What goes wrong:** Writing `await next(); return;` compiles to a void-like form but `Handle` returns `Task<TResponse>`. The compiler will reject a bare `return;` after `await next()` in a method with a non-void return type, OR worse, it compiles with an implicit `return default!` if you put `return;` at the end — silently losing the TResponse value.
**Why it happens:** CONTEXT.md's bypass pseudo-code `{ await next(); return; }` reads naturally but is not valid C# for a `Task<TResponse>` method.
**How to avoid:** Always write `return await next();` in one expression.
**Warning signs:** Compiler error CS0161 or unexpected `default!` returns in tests.

### Pitfall 2: Pipeline Order Confusion for Synthetic DeviceName
**What goes wrong:** Assuming OidResolutionBehavior can set DeviceName before ValidationBehavior runs.
**Why it happens:** Pipeline order is Logging → Exception → Validation → OidResolution. Validation runs first. If a synthetic message is published without DeviceName, it is rejected before the bypass guard is ever reached.
**How to avoid:** All `SnmpSource.Synthetic` messages must have `DeviceName` set at publish time (not expected to be null).
**Warning signs:** Test shows `nextCalled = false` for Synthetic messages and the reject reason is `MissingDeviceName`.

### Pitfall 3: Forgetting to Update MakeNotification Factory in Tests
**What goes wrong:** Existing `MakeNotification` helper hardcodes `Source = SnmpSource.Poll`. Tests for the Synthetic bypass must pass `Source = SnmpSource.Synthetic`.
**Why it happens:** Copy-paste from existing test body without changing the Source.
**How to avoid:** Add a `source` parameter with a default to the existing factory, or create a separate `MakeSyntheticNotification` factory.
**Warning signs:** Test passes even when bypass guard is not implemented (Poll messages route through OID resolution normally, potentially masking the bypass logic).

### Pitfall 4: Verifying "0.0" Only in Production Path
**What goes wrong:** Not adding a unit test that explicitly confirms "0.0" passes ValidationBehavior.
**Why it happens:** Developer trusts the regex proof without a test.
**How to avoid:** Add one `[Fact]` in `ValidationBehaviorTests.cs`: Oid="0.0", DeviceName="some-device", assert `nextCalled = true`.
**Warning signs:** Future regex changes silently break synthetic dispatch without a failing test catching it.

## Code Examples

### SnmpSource.cs Final State
```csharp
// Source: src/SnmpCollector/Pipeline/SnmpSource.cs
namespace SnmpCollector.Pipeline;

public enum SnmpSource
{
    Poll,
    Trap,
    Synthetic
}
```

### OidResolutionBehavior.cs Bypass Guard (exact placement)
```csharp
// Source: src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
if (notification is SnmpOidReceived msg)
{
    if (msg.Source == SnmpSource.Synthetic) { return await next(); }

    msg.MetricName = _oidMapService.Resolve(msg.Oid);

    if (msg.MetricName == OidMapService.Unknown)
        _logger.LogDebug("OID {Oid} not found in OidMap", msg.Oid);
    else
        _logger.LogDebug("OID {Oid} resolved to {MetricName}", msg.Oid, msg.MetricName);
}

return await next();
```

### OidResolutionBehaviorTests — New Tests
```csharp
[Fact]
public async Task SyntheticMessage_BypassesOidResolution_MetricNameNotSet()
{
    var oidMapService = new StubOidMapService(knownOid: "1.3.6.1.2.1.25.3.3.1.2", metricName: "hrProcessorLoad");
    var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
    var notification = MakeNotification("0.0", SnmpSource.Synthetic);

    await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

    Assert.Null(notification.MetricName);  // OID resolution was skipped
}

[Fact]
public async Task SyntheticMessage_StillCallsNext()
{
    var oidMapService = new StubOidMapService(knownOid: null, metricName: null);
    var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
    var notification = MakeNotification("0.0", SnmpSource.Synthetic);
    var nextCalled = false;

    await behavior.Handle(notification, ct =>
    {
        nextCalled = true;
        return Task.FromResult(Unit.Value);
    }, CancellationToken.None);

    Assert.True(nextCalled);
}

[Fact]
public async Task PollMessage_StillResolvesOid_NotAffectedByBypassGuard()
{
    // Regression: ensure Poll messages are unaffected
    var oidMapService = new StubOidMapService(knownOid: "1.3.6.1.2.1.25.3.3.1.2", metricName: "hrProcessorLoad");
    var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
    var notification = MakeNotification("1.3.6.1.2.1.25.3.3.1.2", SnmpSource.Poll);

    await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

    Assert.Equal("hrProcessorLoad", notification.MetricName);
}
```

### ValidationBehaviorTests — Sentinel OID Test
```csharp
[Fact]
public async Task AcceptsSentinelOid_ZeroDotZero_WhenDeviceNameSet()
{
    // "0.0" is the synthetic sentinel OID — must pass OID format and DeviceName checks
    var behavior = CreateBehavior();
    var nextCalled = false;

    await behavior.Handle(MakeNotification("0.0", deviceName: "synthetic-device"), ct =>
    {
        nextCalled = true;
        return Task.FromResult(Unit.Value);
    }, CancellationToken.None);

    Assert.True(nextCalled);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| n/a — new feature | Source-discriminated bypass guard | Phase 39 | Unlocks synthetic dispatch in Phase 40 |

No deprecated patterns relevant to this phase.

## Open Questions

1. **`MakeNotification` factory signature change**
   - What we know: The existing factory has no `source` parameter; it hardcodes `Source = SnmpSource.Poll`.
   - What's unclear: Whether to add an optional `source` parameter to the existing factory or add a dedicated `MakeSyntheticNotification` factory. Both work.
   - Recommendation: Add `SnmpSource source = SnmpSource.Poll` as an optional parameter to the existing factory — minimal churn, backward compatible with all existing test calls.

2. **Trap messages and the bypass guard**
   - What we know: Trap messages use `SnmpSource.Trap` and go through OID resolution normally. The bypass only activates for `SnmpSource.Synthetic`.
   - What's unclear: No ambiguity. Explicitly confirmed by CONTEXT.md ("Existing Poll and Trap messages are completely unaffected").
   - Recommendation: Add one regression test for Trap to mirror the Poll regression test.

## Sources

### Primary (HIGH confidence)
- Direct code read: `src/SnmpCollector/Pipeline/SnmpSource.cs` — current enum state (Poll, Trap only)
- Direct code read: `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — exact insertion point identified
- Direct code read: `src/SnmpCollector/Pipeline/Behaviors/ValidationBehavior.cs` — regex `^\d+(\.\d+){1,}$` verified to accept "0.0"
- Direct code read: `src/SnmpCollector/Pipeline/SnmpOidReceived.cs` — `Source` property confirmed as `required SnmpSource Source { get; init; }`
- Direct code read: `tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs` — test patterns, StubOidMapService, factory method
- Direct code read: `tests/SnmpCollector.Tests/Pipeline/Behaviors/ValidationBehaviorTests.cs` — test patterns, PipelineMetricService setup
- Direct read: `.planning/phases/39-pipeline-bypass-guards/39-CONTEXT.md` — all decisions locked

### Secondary (MEDIUM confidence)
- n/a — all findings from direct codebase inspection

### Tertiary (LOW confidence)
- n/a

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all components already in codebase, no new dependencies
- Architecture: HIGH — bypass pattern directly read from source; regex proof is mathematical
- Pitfalls: HIGH — derived from direct analysis of method signature and pipeline order

**Research date:** 2026-03-15
**Valid until:** 2026-04-15 (stable internal codebase, no external library churn risk)
