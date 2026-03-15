# Phase 42: Threshold Validation & Config Files - Research

**Researched:** 2026-03-15
**Domain:** C# configuration validation, structured logging, JSON config files
**Confidence:** HIGH

## Summary

Phase 42 adds threshold validation logic to `TenantVectorWatcherService.ValidateAndBuildTenants` and populates example `Threshold` entries in both the local dev config (`tenants.json`) and the K8s ConfigMap (`simetra-tenants.yaml`). The data model already exists (Phase 41 shipped `ThresholdOptions` sealed class with `double? Min` / `double? Max`, `MetricSlotOptions.Threshold` property, and `MetricSlotHolder.Threshold` storage). This phase is purely about:

1. Adding a Min > Max guard inside the metric validation loop in `ValidateAndBuildTenants` — log error, set `metric.Threshold = null`, continue (metric still loads).
2. Adding unit tests for the three threshold scenarios (valid, Min > Max, both null).
3. Patching the two config files to include example `Threshold` objects on at least one metric per tenant.

The validation pattern follows the exact same per-entry-skip structure already present in the method. No new services, no new classes, no structural changes required.

**Primary recommendation:** Inline the threshold check as validation step 7 (after the existing 6 checks) in the metric loop, immediately before IP resolution. Nullify the threshold on the options object and continue (do NOT skip the metric). Log with the structured fields `TenantName, MetricIndex, Min, Max` matching the state decision.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Logging | (bundled with .NET) | Structured logging via `ILogger` | Already in use throughout the file |
| xUnit | (project reference) | Unit test framework | Already in use in `TenantVectorWatcherValidationTests.cs` |
| NSubstitute | (project reference) | Mocking `IOidMapService` / `IDeviceRegistry` | Already in use in validation tests |

No new packages required. This phase involves no new library introductions.

## Architecture Patterns

### Recommended Project Structure

No new files needed for the validation logic. New test cases go into:

```
tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs  (extend existing)
src/SnmpCollector/config/tenants.json                                      (add Threshold examples)
deploy/k8s/snmp-collector/simetra-tenants.yaml                            (add Threshold examples)
```

The `ValidateAndBuildTenants` method is in:
```
src/SnmpCollector/Services/TenantVectorWatcherService.cs
```

### Pattern 1: Per-Entry Nullification (not skip)

**What:** When Min > Max, the threshold is invalid. The metric is NOT skipped — only the threshold is nullified. This differs from existing checks (empty IP, bad role, etc.) which use `continue`.

**When to use:** Per the locked decision: "Min > Max = Error log, skip threshold (set to null on holder), metric still loads."

**Implementation location:** Inside the metric for-loop in `ValidateAndBuildTenants`, after the 6 existing checks (empty IP, port range, empty MetricName, invalid Role, OID map check, device registry check), immediately before the IP resolution block.

**Example (to implement):**

```csharp
// 7. Threshold validation: Min > Max is invalid — nullify threshold, metric still loads (THR-04/THR-05)
if (metric.Threshold is { Min: not null, Max: not null } thr && thr.Min > thr.Max)
{
    logger.LogError(
        "Tenant '{TenantName}' Metrics[{MetricIndex}] threshold invalid: Min {Min} > Max {Max} -- threshold cleared, metric still loads",
        tenantId, j, thr.Min, thr.Max);
    metric.Threshold = null;
}
```

Note: `tenantId` is already the resolved tenant name or `tenant-{i}` fallback — it maps to the `TenantName` structured field.

### Pattern 2: Both-Null Is Valid

**What:** `Threshold = null` (absent from JSON) and `Threshold = { Min: null, Max: null }` (explicit both-null) are both valid semantics ("always-violated" per the decision).

**Implication:** No special guard needed for both-null. The nullification step above only fires when both sides are non-null AND Min > Max.

### Pattern 3: Config File Schema (JSON)

The `Threshold` property is optional at the JSON level. Example metric entry with threshold:

```json
{
  "Ip": "127.0.0.1",
  "Port": 10162,
  "MetricName": "npb_cpu_util",
  "TimeSeriesSize": 5,
  "Role": "Evaluate",
  "Threshold": { "Min": 0.0, "Max": 95.0 }
}
```

The JSON deserializer is configured with `PropertyNameCaseInsensitive = true` and `AllowTrailingCommas = true` (see `JsonOptions` in `TenantVectorWatcherService`), so standard PascalCase keys work.

### Anti-Patterns to Avoid

- **Skipping the metric on bad threshold:** The decision is explicit — only the threshold is cleared, the metric still loads. Using `continue` here would break the intent.
- **Throwing on bad threshold:** No exceptions — log error and nullify.
- **Creating a separate validation method for thresholds:** The threshold check is a single 4-line guard inline in the existing loop. No need for extracted helper.
- **Using `LogWarning` instead of `LogError`:** The state decision specifies Error log for Min > Max.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Structured log fields | Custom log formatter | `ILogger.LogError` with named placeholders | Already the project pattern; structured log sinks capture named fields automatically |

**Key insight:** The entire implementation is a small inline addition to an existing method. The heavy lifting (registry reload, semaphore, K8s watch) is already done.

## Common Pitfalls

### Pitfall 1: Mutating metric.Threshold After IP Resolution

**What goes wrong:** If the threshold nullification is placed after IP resolution (the `metric.Ip = resolvedIp;` block), the code still works functionally but is logically out of order — validation should complete before mutation.

**How to avoid:** Place the threshold check immediately after check #6 (device registry), before the `resolvedIp` block.

### Pitfall 2: Pattern Match Condition Ordering

**What goes wrong:** Writing `metric.Threshold?.Min > metric.Threshold?.Max` can return false when either is null because `null > null` is false and `double? > null` is false in C# nullable comparisons.

**How to avoid:** Use the pattern `metric.Threshold is { Min: not null, Max: not null } thr && thr.Min > thr.Max`. This guarantees both sides are non-null before comparison.

**Verified:** C# nullable value type comparisons return false when either operand is null. Explicit null-check pattern is the correct approach.

### Pitfall 3: tenants.json Wrapper

**What goes wrong:** The local `tenants.json` has a double-wrapped structure: `{ "Tenants": { "Tenants": [ ... ] } }`. The K8s YAML has a single-wrapped structure: `{ "Tenants": [ ... ] }`. When adding examples, the correct nesting must be preserved per file.

**How to avoid:** The local `tenants.json` wraps because it's loaded via `IConfiguration` binding, not direct JSON deserialization. When adding Threshold examples, match the existing format in each file exactly.

**File structures confirmed by reading:**
- `src/SnmpCollector/config/tenants.json`: `{ "Tenants": { "Tenants": [ ... ] } }` (double-wrapped)
- `deploy/k8s/snmp-collector/simetra-tenants.yaml`: `tenants.json: |` with `{ "Tenants": [ ... ] }` (single-wrapped inside YAML literal block)

### Pitfall 4: Test Coverage for Threshold Pass-Through

**What goes wrong:** Forgetting to assert that `metric.Threshold` is preserved on the holder when valid. The `TenantVectorRegistry` already passes `metric.Threshold` to `new MetricSlotHolder(...)` — but the test in `ValidateAndBuildTenants` tests should verify the threshold reaches the clean metrics list.

**How to avoid:** Add a test asserting `result.Tenants[0].Metrics[0].Threshold` is the original value when valid, and `null` when Min > Max.

## Code Examples

### Threshold Validation Inline Guard

```csharp
// Source: Codebase analysis — inline after check #6 in ValidateAndBuildTenants
// 7. Threshold: Min > Max clears threshold; metric still loads (THR-04)
if (metric.Threshold is { Min: not null, Max: not null } thr && thr.Min > thr.Max)
{
    logger.LogError(
        "Tenant '{TenantName}' Metrics[{MetricIndex}] threshold invalid: Min {Min} > Max {Max} -- threshold cleared, metric still loads",
        tenantId, j, thr.Min, thr.Max);
    metric.Threshold = null;
}
```

### Unit Test Pattern for Min > Max

```csharp
// Source: Pattern established by TenantVectorWatcherValidationTests.cs
[Fact]
public void MinGreaterThanMax_ThresholdCleared_MetricStillLoads()
{
    var tenant = CreateValidTenant();
    tenant.Metrics[0].Threshold = new ThresholdOptions { Min = 100.0, Max = 50.0 }; // invalid

    var result = TenantVectorWatcherService.ValidateAndBuildTenants(
        Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
        NullLogger.Instance);

    Assert.Single(result.Tenants);
    Assert.Equal(2, result.Tenants[0].Metrics.Count);     // metric survives
    Assert.Null(result.Tenants[0].Metrics[0].Threshold);  // threshold cleared
}
```

### Unit Test Pattern for Both-Null Valid

```csharp
[Fact]
public void BothNullThreshold_IsValid_MetricStillLoads()
{
    var tenant = CreateValidTenant();
    tenant.Metrics[0].Threshold = new ThresholdOptions { Min = null, Max = null };

    var result = TenantVectorWatcherService.ValidateAndBuildTenants(
        Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
        NullLogger.Instance);

    Assert.Single(result.Tenants);
    Assert.Equal(2, result.Tenants[0].Metrics.Count);
    Assert.NotNull(result.Tenants[0].Metrics[0].Threshold); // threshold object preserved
}
```

### Unit Test Pattern for Valid Threshold Pass-Through

```csharp
[Fact]
public void ValidThreshold_PreservedOnCleanMetric()
{
    var tenant = CreateValidTenant();
    tenant.Metrics[0].Threshold = new ThresholdOptions { Min = 10.0, Max = 90.0 };

    var result = TenantVectorWatcherService.ValidateAndBuildTenants(
        Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
        NullLogger.Instance);

    Assert.Single(result.Tenants);
    var th = result.Tenants[0].Metrics[0].Threshold;
    Assert.NotNull(th);
    Assert.Equal(10.0, th.Min);
    Assert.Equal(90.0, th.Max);
}
```

### Config Example: tenants.json (local dev, double-wrapped)

Add `"Threshold": { "Min": 0.0, "Max": 95.0 }` to at least one Evaluate metric per tenant. One suitable candidate per tenant in the existing file:

- Tenant 1 (port 10161): `obp_r1_power_L1` (Evaluate) — add threshold
- Tenant 2 (port 10162): `npb_cpu_util` (Evaluate, already has `TimeSeriesSize`) — add threshold

### Config Example: simetra-tenants.yaml (K8s, 3 tenants)

One candidate per tenant in the existing YAML:

- Tenant Priority 1 (npb): `npb_cpu_util` (Evaluate, already has `TimeSeriesSize: 10`)
- Tenant Priority 2 (npb): `npb_mem_util` (Evaluate, already has `TimeSeriesSize: 5`)
- Tenant Priority 3 (obp): `obp_r1_power_L1` (Evaluate, already has `TimeSeriesSize: 3`)

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| No threshold model | `ThresholdOptions` sealed class with `double? Min / Max` | Phase 41 | Data model exists; only validation + config needed |
| No threshold on holder | `MetricSlotHolder.Threshold { get; }` property | Phase 41 | Already plumbed through registry build |

## Open Questions

None. All decisions are locked in the STATE.md and the codebase is fully inspected.

1. **Threshold example values** — What numeric range makes sense for CPU util? The config files use `npb_cpu_util` with percent semantics, so `{ "Min": 0.0, "Max": 95.0 }` is a reasonable example. Power metrics (obp_r1_power_L1) could use `{ "Min": -10.0, "Max": 3.0 }` (optical dBm range). These are documentation examples only, not runtime-enforced at this phase.

## Sources

### Primary (HIGH confidence)

- Direct codebase inspection: `TenantVectorWatcherService.cs` — complete ValidateAndBuildTenants implementation reviewed
- Direct codebase inspection: `ThresholdOptions.cs` — sealed class with `double? Min / Max` confirmed
- Direct codebase inspection: `MetricSlotOptions.cs` — `ThresholdOptions? Threshold` property confirmed
- Direct codebase inspection: `MetricSlotHolder.cs` — `ThresholdOptions? Threshold { get; }` constructor param confirmed
- Direct codebase inspection: `TenantVectorRegistry.cs` lines 104-110 — `metric.Threshold` already passed to `new MetricSlotHolder(...)` confirmed
- Direct codebase inspection: `TenantVectorWatcherValidationTests.cs` — test patterns, helper methods, assertion style confirmed
- Direct codebase inspection: `src/SnmpCollector/config/tenants.json` — double-wrapped structure confirmed
- Direct codebase inspection: `deploy/k8s/snmp-collector/simetra-tenants.yaml` — single-wrapped 3-tenant structure confirmed

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages, all existing dependencies
- Architecture: HIGH — direct inspection of insertion point, no ambiguity
- Pitfalls: HIGH — nullable double comparison behavior is well-defined C# semantics; config nesting confirmed by file inspection

**Research date:** 2026-03-15
**Valid until:** Stable (no external dependencies; pure codebase change)
