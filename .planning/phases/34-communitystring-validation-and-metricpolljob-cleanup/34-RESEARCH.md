# Phase 34: CommunityString Validation & MetricPollJob Cleanup - Research

**Researched:** 2026-03-14
**Domain:** Configuration validation, per-entry skip semantics, structured error logging
**Confidence:** HIGH

## Summary

This phase adds validation logic to existing registries and reload paths — no new services, no new libraries, no new infrastructure. The codebase already has the critical building blocks: `CommunityStringHelper.TryExtractDeviceName` for CS format validation, `DeviceRegistry.ReloadAsync` as the site for device-side validation, `TenantVectorRegistry.Reload` as the site for tenant-side validation, `IOidMapService.ContainsMetricName` for MetricName resolution, and `IDeviceRegistry.TryGetByIpPort` for IP+Port existence checks.

All work falls into two main areas:
1. **DeviceRegistry changes**: Change duplicate IP+Port from `throw` to `skip+Error`, add duplicate-CommunityString Warning (DEV-10), add zero-OID poll group skip (DEV-08). The constructor and `ReloadAsync` share identical logic — a private helper is the cleanest approach.
2. **TenantVectorRegistry.Reload changes**: Add per-entry validation (structural, Role, ValueType, MetricName resolution, IP+Port existence) with skip+Error semantics, followed by TEN-13 post-validation gate per tenant.

The `TenantVectorOptionsValidator` stays a no-op (JSON structural validation only). The `DevicesOptionsValidator` already validates CommunityString format at startup (startup-fail mode); the new per-entry device validation is in `DeviceRegistry` (runtime-skip mode). CLN-03 is already done — `MetricPollJob` reads `device.CommunityString` directly, no fallback exists.

**Primary recommendation:** Implement all validation directly in `DeviceRegistry` (private helper shared by constructor + `ReloadAsync`) and `TenantVectorRegistry.Reload` (inline loops). No new services, no new validator classes.

## Standard Stack

This phase uses only what is already in the project. No new packages needed.

### Core (already present)
| Component | Location | Purpose |
|-----------|----------|---------|
| `CommunityStringHelper.TryExtractDeviceName` | `Pipeline/CommunityStringHelper.cs` | Validates `Simetra.*` format, extracts device name |
| `IOidMapService.ContainsMetricName` | `Pipeline/IOidMapService.cs` | Resolves MetricName existence check |
| `IDeviceRegistry.TryGetByIpPort` | `Pipeline/IDeviceRegistry.cs` | IP+Port existence check for tenant entries |
| `ILogger<T>` with structured logging | Throughout | `LogError` / `LogWarning` with named parameters |

### No New Packages Needed
All validation is pure C# logic using existing interfaces. No FluentValidation, no DataAnnotations changes.

## Architecture Patterns

### Existing Pattern: Per-Entry Skip in DeviceRegistry
The constructor and `ReloadAsync` already follow the skip-with-log pattern for invalid CommunityStrings:

```csharp
if (!CommunityStringHelper.TryExtractDeviceName(d.CommunityString, out var deviceName))
{
    _logger.LogError(
        "Device at {IpAddress}:{Port} has invalid CommunityString '{CommunityString}' -- skipping",
        d.IpAddress, d.Port, d.CommunityString);
    continue;
}
```

This is the exact pattern to replicate for all new skip cases. The `continue` is the key — loop keeps going, no throw.

### Current Duplicate IP+Port: Throw → Must Change to Skip+Error
The current constructor AND `ReloadAsync` both `throw new InvalidOperationException(...)` on duplicate IP+Port. Phase 34 changes these to:

```csharp
if (byIpPortBuilder.TryGetValue(ipPortKey, out var existing))
{
    _logger.LogError(
        "Device at {IpAddress}:{Port} (CommunityString '{CommunityString}') is a duplicate of existing device '{ExistingName}' -- skipping",
        d.IpAddress, d.Port, d.CommunityString, existing.Name);
    continue;
}
```

**Critical:** The existing test `Constructor_DuplicateIpPort_ThrowsInvalidOperationException` asserts the throw — it must be updated to assert Error log + skip instead.

### New: Duplicate CommunityString Warning (DEV-10)
After the IP+Port duplicate check passes, check for duplicate CommunityString. Both devices still load — Warning only:

```csharp
// Track CommunityStrings seen so far in this reload pass
var seenCommunityStrings = new Dictionary<string, string>(StringComparer.Ordinal); // CS -> first device name

// ... after successful IP+Port check ...
if (seenCommunityStrings.TryGetValue(d.CommunityString, out var priorDeviceName))
{
    _logger.LogWarning(
        "Devices[{Index}] CommunityString '{CommunityString}' also used by device '{PriorDevice}' -- both loaded (different IP+Port)",
        i, d.CommunityString, priorDeviceName);
}
else
{
    seenCommunityStrings[d.CommunityString] = deviceName;
}
```

Note: index tracking requires a `for` loop, not `foreach` (already the pattern in `AddSnmpScheduling`).

### New: Zero-OID Poll Group Skip (DEV-08)
In `BuildPollGroups` (or at registration time in `DynamicPollScheduler.ReconcileAsync`), skip poll group registration when zero OIDs resolved. The **device still loads** — only the zero-OID poll groups are skipped from Quartz job registration:

```csharp
return new MetricPollInfo(...) with { } // only include if resolvedOids.Count > 0
```

**Where to implement DEV-08:** `BuildPollGroups` already returns all poll groups including zero-OID ones. The cleanest implementation is to filter in `BuildPollGroups` — skip returning a `MetricPollInfo` for groups with zero resolved OIDs, logging an Info or Warning. This means `DynamicPollScheduler.ReconcileAsync` naturally sees no job for that group.

Alternative: filter in the caller. Either works; filtering in `BuildPollGroups` keeps the logic colocated with OID resolution.

### Tenant Validation Pattern in TenantVectorRegistry.Reload
The current `Reload` loop iterates `options.Tenants` and builds holders without any validation. Phase 34 adds a validation pass per entry, plus a post-loop TEN-13 gate per tenant.

Recommended structure:
```csharp
for (var i = 0; i < options.Tenants.Count; i++)
{
    var tenantOpts = options.Tenants[i];
    var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name) ? tenantOpts.Name : $"tenant-{i}";

    var metricHolders = new List<MetricSlotHolder>();
    var evaluateCount = 0;
    var resolvedCount = 0;

    for (var j = 0; j < tenantOpts.Metrics.Count; j++)
    {
        var metric = tenantOpts.Metrics[j];
        // 1. Structural validation
        if (string.IsNullOrWhiteSpace(metric.Ip)) { LogError(...); continue; }
        if (metric.Port is < 1 or > 65535) { LogError(...); continue; }
        if (string.IsNullOrWhiteSpace(metric.MetricName)) { LogError(...); continue; }
        // 2. Role validation
        if (metric.Role is not ("Evaluate" or "Resolved")) { LogError(...); continue; }
        // 3. MetricName resolution (TEN-05)
        if (!_oidMapService.ContainsMetricName(metric.MetricName)) { LogError(...); continue; }
        // 4. IP+Port existence (TEN-07)
        if (!_deviceRegistry.TryGetByIpPort(metric.Ip, metric.Port, out _)) { LogError(...); continue; }

        // Valid entry
        var resolvedIp = ResolveIp(metric.Ip);
        metricHolders.Add(new MetricSlotHolder(resolvedIp, metric.Port, metric.MetricName, ...));
        if (metric.Role == "Evaluate") evaluateCount++;
        else resolvedCount++;
    }

    // Command validation (TEN-13 contribution)
    var commandCount = 0;
    for (var k = 0; k < tenantOpts.Commands.Count; k++)
    {
        var cmd = tenantOpts.Commands[k];
        // 1. Structural validation
        if (string.IsNullOrWhiteSpace(cmd.Ip)) { LogError(...); continue; }
        if (cmd.Port is < 1 or > 65535) { LogError(...); continue; }
        if (string.IsNullOrWhiteSpace(cmd.CommandName)) { LogError(...); continue; }
        // 2. ValueType validation (TEN-03)
        if (cmd.ValueType is not ("Integer32" or "IpAddress" or "OctetString")) { LogError(...); continue; }
        // 3. Value non-empty
        if (string.IsNullOrWhiteSpace(cmd.Value)) { LogError(...); continue; }
        // 4. IP+Port existence (TEN-07)
        if (!_deviceRegistry.TryGetByIpPort(cmd.Ip, cmd.Port, out _)) { LogError(...); continue; }
        commandCount++;
    }

    // TEN-13: post-validation completeness gate
    var missing = new List<string>();
    if (resolvedCount == 0) missing.Add("no Resolved metrics");
    if (evaluateCount == 0) missing.Add("no Evaluate metrics");
    if (commandCount == 0) missing.Add("no commands");
    if (missing.Count > 0)
    {
        _logger.LogError(
            "Tenant '{TenantId}' skipped: {Reason}",
            tenantId, string.Join(", ", missing));
        continue;
    }

    // Add to priority buckets as before
    var tenant = new Tenant(tenantId, tenantOpts.Priority, metricHolders);
    ...
}
```

### New Dependency: IOidMapService in TenantVectorRegistry
Currently `TenantVectorRegistry` takes only `IDeviceRegistry` and `ILogger`. Phase 34 requires adding `IOidMapService` as a constructor parameter for MetricName validation (TEN-05).

```csharp
public TenantVectorRegistry(
    IDeviceRegistry deviceRegistry,
    IOidMapService oidMapService,   // NEW
    ILogger<TenantVectorRegistry> logger)
```

The DI registration in `ServiceCollectionExtensions.AddSnmpConfiguration` must be updated:
```csharp
services.AddSingleton<TenantVectorRegistry>(sp =>
    new TenantVectorRegistry(
        sp.GetRequiredService<IDeviceRegistry>(),
        sp.GetRequiredService<IOidMapService>(),  // NEW
        sp.GetRequiredService<ILogger<TenantVectorRegistry>>()));
```

All `TenantVectorRegistryTests` that use `CreateRegistry()` must be updated to pass a mock/stub `IOidMapService`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CommunityString format check | Custom regex/string logic | `CommunityStringHelper.TryExtractDeviceName` | Already exists, already tested |
| MetricName existence check | String lookup in map copy | `IOidMapService.ContainsMetricName` | Already thread-safe, hot-reloadable |
| IP+Port existence check | Cache of device IPs | `IDeviceRegistry.TryGetByIpPort` | Already the canonical lookup |
| Role/ValueType set membership | Switch/dictionary | `is ("A" or "B")` C# pattern | Simple, readable, no overhead |

**Key insight:** All validation logic in this phase should compose existing helpers rather than re-implementing format checks.

## Common Pitfalls

### Pitfall 1: Forgetting the Constructor/ReloadAsync Symmetry
**What goes wrong:** Adding validation to `ReloadAsync` but forgetting the constructor (or vice versa). Both code paths build the same `byIpPortBuilder` loop — they must be kept in sync.
**How to avoid:** Extract a private `BuildDeviceInfos(IList<DeviceOptions> devices)` helper that both the constructor and `ReloadAsync` call. Eliminates the duplication.
**Warning signs:** If you find yourself copying code between the two, stop and extract the helper.

### Pitfall 2: Breaking the Constructor_DuplicateIpPort Test
**What goes wrong:** The existing test `Constructor_DuplicateIpPort_ThrowsInvalidOperationException` in `DeviceRegistryTests` asserts `Assert.Throws<InvalidOperationException>`. Phase 34 changes the behavior to skip+Error — the test must be updated to assert the Error log and device count instead.
**How to avoid:** Update the test first (or simultaneously with the implementation). Running tests after changing only the impl will surface this immediately.

### Pitfall 3: TenantVectorRegistry Now Needs IOidMapService
**What goes wrong:** Forgetting to update `TenantVectorRegistryTests.CreateRegistry()` factory method — it currently takes no IOidMapService. All 35+ tests in that file will fail to compile.
**How to avoid:** Update `CreateRegistry()` to accept/pass a stub `IOidMapService` (using NSubstitute or a simple null-returning stub). Most tests don't care about MetricName validation, so a stub that returns `true` for `ContainsMetricName` is correct.

### Pitfall 4: TEN-13 Gate Fires Before Command Validation Runs
**What goes wrong:** Counting commands before the command validation loop, causing TEN-13 to see incorrect counts.
**How to avoid:** Always count only entries that passed ALL validation checks. Count at the point of `commandCount++` (after all checks), not at the loop entry.

### Pitfall 5: Duplicate CommunityString Index Tracking
**What goes wrong:** Using `foreach` on `devices` and trying to log the index — no index available.
**How to avoid:** The DeviceRegistry already uses `foreach` internally. For the duplicate CommunityString warning, track the devices seen in a dictionary keyed by CommunityString. The log message should include device name (derivable from `deviceName` already extracted), not the array index.

### Pitfall 6: DEV-08 Skips Jobs but Device Still Registers
**What goes wrong:** Skipping the device entirely when a poll group has zero OIDs — but devices with zero-OID poll groups must still register for trap reception.
**How to avoid:** Skip only the `MetricPollInfo` entries with zero OIDs from `BuildPollGroups`. The device itself always registers.

### Pitfall 7: IOidMapService is Empty at TenantVectorRegistry Reload Time
**What goes wrong:** During initial startup, `TenantVectorWatcherService` may run before `OidMapWatcherService` loads its map. All MetricName checks return `ContainsMetricName == false`, every metric entry is skipped, TEN-13 fires, all tenants dropped.
**Root cause:** Startup ordering. ConfigMap watchers run concurrently.
**How to avoid/accept:** This is an existing race condition documented in CS-07 (operator config ordering). The CONTEXT says: "Each file has independent watcher — no cross-watcher coupling. Operator responsible for alignment." The validation code should behave consistently regardless — if the OID map is empty, entries fail TEN-05 and get skipped. When the OID map loads and triggers a tenant reload, entries will pass. This is acceptable behavior per the phase decisions.

## Code Examples

### Validated Structural Checks (inline pattern)
```csharp
// Source: existing DeviceRegistry pattern, extended
if (string.IsNullOrWhiteSpace(metric.Ip))
{
    _logger.LogError(
        "Tenant '{TenantId}' Metrics[{Index}] has empty Ip -- skipping entry",
        tenantId, j);
    continue;
}
```

### Role Validation
```csharp
// Source: C# switch expression pattern
if (metric.Role is not ("Evaluate" or "Resolved"))
{
    _logger.LogError(
        "Tenant '{TenantId}' Metrics[{Index}] has invalid Role '{Role}' (must be 'Evaluate' or 'Resolved') -- skipping entry",
        tenantId, j, metric.Role);
    continue;
}
```

### ValueType Validation
```csharp
if (cmd.ValueType is not ("Integer32" or "IpAddress" or "OctetString"))
{
    _logger.LogError(
        "Tenant '{TenantId}' Commands[{Index}] has invalid ValueType '{ValueType}' (must be Integer32, IpAddress, or OctetString) -- skipping entry",
        tenantId, k, cmd.ValueType);
    continue;
}
```

### TEN-13 Completeness Gate
```csharp
// After both metric and command loops:
var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name) ? tenantOpts.Name : $"tenant-{i}";
var missing = new List<string>(3);
if (resolvedCount == 0) missing.Add("no Resolved metrics remaining after validation");
if (evaluateCount == 0) missing.Add("no Evaluate metrics remaining after validation");
if (commandCount == 0) missing.Add("no commands remaining after validation");

if (missing.Count > 0)
{
    _logger.LogError(
        "Tenant '{TenantId}' skipped: {Reason}",
        tenantId, string.Join("; ", missing));
    continue; // Skip to next tenant, don't add to priorityBuckets
}
```

### Duplicate CommunityString Warning (DEV-10)
```csharp
// Track in reload loop (not in BuildPollGroups):
var seenCommunityStrings = new Dictionary<string, string>(StringComparer.Ordinal);

// After extracting deviceName (CommunityString already validated):
if (seenCommunityStrings.TryGetValue(d.CommunityString, out var priorName))
{
    _logger.LogWarning(
        "Device '{DeviceName}' at {IpAddress}:{Port} has CommunityString '{CommunityString}' " +
        "also used by device '{PriorDevice}' -- both loaded (different IP+Port)",
        deviceName, d.IpAddress, d.Port, d.CommunityString, priorName);
}
seenCommunityStrings[d.CommunityString] = deviceName;
```

### DEV-08 Zero-OID Poll Group Skip in BuildPollGroups
```csharp
// In BuildPollGroups, after building resolvedOids:
if (resolvedOids.Count == 0)
{
    _logger.LogWarning(
        "Device '{DeviceName}' poll {PollIndex} has zero resolved OIDs -- skipping job registration",
        deviceName, index);
    return null; // Signal to caller to exclude this poll group
}
```
Then in the caller: `.Where(pg => pg is not null).Select(pg => pg!).ToList().AsReadOnly()`

Or, simpler: filter zero-OID results in the Select chain and log the warning there.

## State of the Art

| Old Approach | Phase 34 Approach | Change | Impact |
|--------------|-------------------|--------|--------|
| Duplicate IP+Port: `throw InvalidOperationException` (constructor + ReloadAsync) | Duplicate IP+Port: `LogError` + `continue` | Phase 34 | Reload survives bad config; tests must be updated |
| Zero-OID poll groups: registered as Quartz jobs (no-ops, just never fire SNMP GET) | Zero-OID poll groups: not registered at all | Phase 34 | Cleaner scheduler state |
| TenantVectorRegistry.Reload: no validation, all entries accepted | TenantVectorRegistry.Reload: per-entry validation with skip | Phase 34 | Bad config entries silently dropped become visible via Error logs |
| TenantVectorOptionsValidator: unconditional no-op | Remains unconditional no-op | No change | Validator is for startup-fail; runtime validation stays in registry |
| MetricPollJob: uses `device.CommunityString` directly | No change needed (CLN-03 already done in Phase 33) | Phase 33 | Already clean |

## CLN-03 Status (Already Done in Phase 33)

The requirement to "remove MetricPollJob CommunityString fallback" is **already complete**. Confirmed by reading `MetricPollJob.cs`:

```csharp
var communityStr = device.CommunityString;  // Line 86: reads directly from DeviceInfo
var community = new OctetString(communityStr);
```

No fallback logic exists. `DeviceInfo.CommunityString` is populated from `DeviceOptions.CommunityString` in `DeviceRegistry`. CLN-03 requires no code changes in Phase 34.

## Open Questions

1. **DEV-08 Scope: Startup-time Poll Groups Only or Also Reload?**
   - What we know: `BuildPollGroups` is called in both the constructor and `ReloadAsync`. Both paths build `MetricPollInfo` objects passed to `DynamicPollScheduler.ReconcileAsync`.
   - What's clear: Both paths should skip zero-OID groups consistently.
   - Recommendation: Filter in `BuildPollGroups` — it's the single source for poll group construction.

2. **Is There a `DevicesOptionsValidator` Change for CommunityString Format?**
   - The `DevicesOptionsValidator` already checks CommunityString format at startup (ValidateOnStart). The CONTEXT notes this is intentional: startup-fail for startup config, skip for runtime reload. No change needed to the validator.
   - However, the CONTEXT's "Claude's Discretion" includes: "Whether DevicesOptionsValidator gains CommunityString format check or it stays only in DeviceRegistry" — the answer from the codebase is it's **already in both** (validator fails at startup, registry skips at runtime). No duplication problem.

3. **TenantVectorWatcherService Calls `_validator.Validate()` Before `_registry.Reload()`**
   - The watcher calls the (currently no-op) validator, then calls `_registry.Reload()`. Phase 34 moves real validation into `Reload()`. This is correct per CONTEXT decision: "TenantVectorOptionsValidator stays minimal."
   - No change to `TenantVectorWatcherService` needed.

## Sources

### Primary (HIGH confidence)
- Direct codebase reading of all affected files — no external library research needed, this is pure application logic
- `DeviceRegistry.cs` — existing skip-with-log pattern, duplicate throw location
- `TenantVectorRegistry.cs` — current Reload loop structure, IOidMapService absence
- `CommunityStringHelper.cs` — existing validation helper
- `IOidMapService.cs` — `ContainsMetricName` already exists
- `MetricPollJob.cs` — confirmed CLN-03 already done
- `TenantVectorOptionsValidator.cs` — confirmed no-op, no change
- `DevicesOptionsValidator.cs` — confirmed CommunityString check already present at startup
- `ServiceCollectionExtensions.cs` — confirmed DI registration needs updating for IOidMapService

### Secondary (MEDIUM confidence)
- `TenantVectorRegistryTests.cs` — identified 35+ tests needing `IOidMapService` stub update
- `DeviceRegistryTests.cs` — identified test needing throw → skip behavioral update

## Metadata

**Confidence breakdown:**
- CLN-03 status: HIGH — directly verified in source
- Architecture (what to change and where): HIGH — all affected files read
- Test impact: HIGH — test files examined directly
- Startup ordering race (Pitfall 7): MEDIUM — based on service registration order analysis, not runtime observation

**Research date:** 2026-03-14
**Valid until:** 2026-04-14 (codebase-specific research, valid until Phase 35 changes same files)
