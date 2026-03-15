# Phase 43: Heartbeat Cleanup - Research

**Researched:** 2026-03-15
**Domain:** Internal C# refactoring — TenantVectorRegistry, TenantVectorFanOutBehavior, and their unit tests
**Confidence:** HIGH

## Summary

Phase 43 is a pure in-codebase refactoring: remove two blocks of hardcoded heartbeat plumbing that were added when the heartbeat pipeline path needed special treatment. The decisions are locked — there are no library choices or patterns to discover externally. All research is based on reading the actual source files.

The heartbeat tenant was inserted into `TenantVectorRegistry.Reload` as a synthetic `int.MinValue`-priority group. The heartbeat bypass was added to `TenantVectorFanOutBehavior` because "Simetra" is not in `DeviceRegistry`, so the normal `TryGetDeviceByName` path would skip it. After this phase, both special-cases are deleted. The behavior reverts to the natural path: "Simetra" not in `DeviceRegistry` → `TryGetDeviceByName` returns false → fan-out is silently skipped. `ILivenessVectorService.Stamp()` in `HeartbeatJob.finally` is completely unrelated and stays unchanged.

**Primary recommendation:** Delete the three heartbeat code blocks (registry injection, registry bypass, and `TenantCount +1`) and delete/rewrite the five heartbeat-specific tests; adjust count assertions in the remaining registry tests.

## Standard Stack

No new libraries. This phase touches only existing production and test code.

### Files Changed

| File | What Changes |
|------|-------------|
| `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` | Remove lines 75-89 (heartbeat holder construction + `priorityBuckets[int.MinValue]`) and `totalSlots++` for it; change `TenantCount = survivingTenantCount + 1` → `TenantCount = survivingTenantCount` |
| `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` | Remove lines 47-59 (heartbeat-bypass `if` block and its `else` keyword, collapsing to a single `if (_deviceRegistry.TryGetDeviceByName(...))`) |
| `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` | Delete section 9 (5 heartbeat tests). Fix count assertions that assumed `+1` for heartbeat. |
| `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` | No heartbeat-specific tests exist here — no heartbeat bypass tests were written. Verify no assertions depend on heartbeat behavior. |

### Files That Do NOT Change

| File | Why |
|------|-----|
| `src/SnmpCollector/Jobs/HeartbeatJob.cs` | `_liveness.Stamp(jobKey)` in `finally` is unrelated; stays unchanged |
| `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` | Constants (`HeartbeatDeviceName`, `HeartbeatOid`, `DefaultIntervalSeconds`) are used by `HeartbeatJob` and `CommunityStringHelper`; do not delete |

## Architecture Patterns

### Pattern 1: Remove Hardcoded Heartbeat from TenantVectorRegistry.Reload

**What:** Lines 75-89 create a synthetic heartbeat `MetricSlotHolder` and inject it into `priorityBuckets[int.MinValue]`. Lines 172-173 compensate with `TenantCount = survivingTenantCount + 1` and `totalSlots++`.

**Exact blocks to delete:**

```csharp
// Lines 75-89 — DELETE ENTIRELY
var heartbeatHolder = new MetricSlotHolder("127.0.0.1", 0, "Heartbeat", HeartbeatJobOptions.DefaultIntervalSeconds);
var heartbeatKey = new RoutingKey("127.0.0.1", 0, "Heartbeat");
if (oldSlotLookup.TryGetValue(heartbeatKey, out var oldHeartbeatHolder))
{
    if (oldHeartbeatHolder.ReadSlot() is not null)
    {
        heartbeatHolder.CopyFrom(oldHeartbeatHolder);
        carriedOver++;
    }
}

var heartbeatTenant = new Tenant("heartbeat", int.MinValue, new[] { heartbeatHolder });
priorityBuckets[int.MinValue] = new List<Tenant> { heartbeatTenant };
totalSlots++;
```

```csharp
// Line 173 — CHANGE
TenantCount = survivingTenantCount + 1;  // OLD
TenantCount = survivingTenantCount;       // NEW
```

### Pattern 2: Remove Heartbeat Bypass from TenantVectorFanOutBehavior

**What:** Lines 47-59 are an `if` block that checks `msg.DeviceName == "Simetra"` and routes directly to `("127.0.0.1", 0, "Heartbeat")`, bypassing `DeviceRegistry`. The normal device-registry path is the `else if` branch. After deletion, the `else if` becomes a plain `if`.

**Exact block to delete (lines 47-59):**

```csharp
// DELETE this entire if block
if (string.Equals(msg.DeviceName, HeartbeatJobOptions.HeartbeatDeviceName, StringComparison.Ordinal))
{
    if (_registry.TryRoute("127.0.0.1", 0, metricName, out var heartbeatHolders))
    {
        foreach (var holder in heartbeatHolders)
        {
            holder.WriteValue(msg.ExtractedValue, msg.ExtractedStringValue, msg.TypeCode, msg.Source);
            _pipelineMetrics.IncrementTenantVectorRouted(msg.DeviceName!);
        }
    }
}
// CHANGE: "else if" → "if"
else if (_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device))
```

After deletion the outer structure becomes:

```csharp
if (metricName is not null && metricName != OidMapService.Unknown)
{
    try
    {
        if (_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device))
        {
            // existing device-registry fan-out unchanged
        }
    }
    catch (Exception ex) { ... }
}
```

### Pattern 3: Test Adjustments in TenantVectorRegistryTests

**Section 9 (lines 443–517) — DELETE all 5 tests:**
- `Reload_EmptyConfig_HeartbeatTenantExists`
- `Reload_WithConfigTenants_HeartbeatIsFirstGroup`
- `Reload_HeartbeatRouting_TryRouteFindsHeartbeat`
- `Reload_HeartbeatValueCarriedOver`
- (The fifth test listed in section 9 header is missing from the file — only 4 heartbeat tests appear; the section header says "5 tests" but the file shows 4. Confirm exact count before deleting.)

**Count assertion fixes in remaining tests** — these pass today because `TenantCount` and `Groups.Count` and `SlotCount` include the heartbeat. After removal, each assertion drops by 1:

| Test | Old Assert | New Assert |
|------|-----------|-----------|
| `Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots` | `registry.Groups.Count == 2` (heartbeat + 1) | `registry.Groups.Count == 1` |
| `Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots` | `registry.Groups[0].Priority == int.MinValue` (heartbeat) | `registry.Groups[0].Priority == 1` |
| `Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots` | `var group = registry.Groups[1]` | `var group = registry.Groups[0]` |
| `Reload_SingleTenant_CountsAreCorrect` | `TenantCount == 2`, `SlotCount == 4` | `TenantCount == 1`, `SlotCount == 3` |
| `Reload_MultiplePriorities_GroupsSortedAscending` | `Groups.Count == 4`, `Groups[0].Priority == int.MinValue`, then 1/5/10 | `Groups.Count == 3`, priorities 1/5/10 at indices 0/1/2 |
| `Reload_SamePriority_TenantsInSameGroup` | `Groups.Count == 2`, `Groups[0].Priority == int.MinValue` | `Groups.Count == 1`, `Groups[0].Priority == 5` |
| `Reload_WithConfigTenants_HeartbeatIsFirstGroup` | deleted (it IS one of the heartbeat tests) | N/A |
| `Reload_TenantWithName_UsesNameAsId` | `registry.Groups[1]` (index 0 = heartbeat) | `registry.Groups[0]` |
| `Reload_TenantWithoutName_UsesAutoGeneratedId` | `registry.Groups[1]` | `registry.Groups[0]` |

### Anti-Patterns to Avoid

- **Deleting `HeartbeatJobOptions.DefaultIntervalSeconds`**: Only the registry used it to build the synthetic holder. After removal it is unused in the registry, but it is still a public constant — leave it in place. The const is in the same class as `HeartbeatDeviceName` which `HeartbeatJob` still uses.
- **Removing `HeartbeatDeviceName` from `HeartbeatJobOptions`**: Still referenced in `HeartbeatJob` constructor (`CommunityStringHelper.DeriveFromDeviceName(HeartbeatJobOptions.HeartbeatDeviceName)`) and potentially in OID resolution. Do not delete.
- **Leaving the `else if` keyword**: After deleting the heartbeat bypass block, `else if` must become `if` — forgetting this leaves a dangling `else` that won't compile.
- **Forgetting `totalSlots++`**: The `totalSlots++` immediately after the `priorityBuckets[int.MinValue]` assignment must also be removed, or `SlotCount` will be inflated by 1 forever.

## Don't Hand-Roll

This phase is purely subtractive. No new solutions needed. Nothing to build.

## Common Pitfalls

### Pitfall 1: Off-by-one in SlotCount
**What goes wrong:** Deleting the heartbeat holder injection but forgetting the `totalSlots++` that follows it leaves `SlotCount` 1 higher than actual.
**Why it happens:** The `totalSlots++` sits on a separate line after the `priorityBuckets` assignment, easy to miss.
**How to avoid:** Delete lines 75-89 as a unit including the `totalSlots++` at line 89.
**Warning signs:** `SlotCount` assertions in tests will fail with off-by-one.

### Pitfall 2: Dangling `else if` won't compile
**What goes wrong:** Deleting the heartbeat `if` block but leaving `else if` on the device-registry branch causes a compile error (`else` without `if`).
**Why it happens:** The device-registry path was written as the `else if` branch of the heartbeat check.
**How to avoid:** Change `else if` to `if` when removing the preceding `if` block.
**Warning signs:** Build error `CS1003 Syntax error, 'if' expected` or similar.

### Pitfall 3: Test section 9 count mismatch
**What goes wrong:** The comment says "5 tests" but the file only contains 4 heartbeat test methods. Searching by count causes confusion.
**Why it happens:** The fifth test (`Reload_HeartbeatValueCarriedOver` at line 496) is present — the section header is accurate. A careful count confirms all 4 are in the range 443–517. Actually re-read: the section has 4 `[Fact]` methods (lines 444, 469, 483, 496). The header says "(5 tests)" — one test may have been merged or the comment predates a consolidation. Delete all `[Fact]` methods within section 9 regardless of count.
**How to avoid:** Delete by method name, not by count.

### Pitfall 4: HeartbeatJobOptions import unused in TenantVectorRegistry
**What goes wrong:** After removing the heartbeat holder construction, the `using` for `HeartbeatJobOptions.DefaultIntervalSeconds` disappears but the namespace is still imported. No compile error, but a warning or style issue.
**Why it happens:** `HeartbeatJobOptions` is only referenced in the deleted block.
**How to avoid:** Check whether `HeartbeatJobOptions` appears anywhere else in `TenantVectorRegistry.cs` after the deletion. It does not — remove the reference. The `Configuration` namespace using directive (`using SnmpCollector.Configuration;`) is likely shared with other types (`TenantVectorOptions`, `TenantOptions`, etc.) — do not remove the using directive, only ensure `HeartbeatJobOptions` itself is no longer referenced.

## Code Examples

### After Deletion — TenantVectorRegistry.Reload (Step 3 section, simplified)

```csharp
// Step 3: Build new MetricSlotHolders, carrying over old values where metric matches.
int carriedOver = 0;
int totalSlots = 0;
int survivingTenantCount = 0;

var priorityBuckets = new SortedDictionary<int, List<Tenant>>();

for (var i = 0; i < options.Tenants.Count; i++)
{
    // ... existing loop unchanged ...
    survivingTenantCount++;
}

// Step 6: Update counts before volatile swap.
TenantCount = survivingTenantCount;   // was: survivingTenantCount + 1
SlotCount = totalSlots;
```

### After Deletion — TenantVectorFanOutBehavior.Handle (inner try block)

```csharp
try
{
    if (_deviceRegistry.TryGetDeviceByName(msg.DeviceName!, out var device))
    {
        var ip = msg.AgentIp.ToString();
        if (_registry.TryRoute(ip, device.Port, metricName, out var holders))
        {
            foreach (var holder in holders)
            {
                holder.WriteValue(msg.ExtractedValue, msg.ExtractedStringValue, msg.TypeCode, msg.Source);
                _pipelineMetrics.IncrementTenantVectorRouted(msg.DeviceName!);
                if (holder.TimeSeriesSize > 1)
                    _logger.LogDebug(...);
            }
        }
    }
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "TenantVectorFanOut exception for {DeviceName}", msg.DeviceName);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|-----------------|--------------|--------|
| Heartbeat injected as synthetic tenant at int.MinValue | Heartbeat skipped naturally (not in DeviceRegistry) | Phase 43 | Removes special-case plumbing; DeviceRegistry is now the single gating mechanism for all devices |
| `TenantCount = survivingTenantCount + 1` | `TenantCount = survivingTenantCount` | Phase 43 | Count accurately reflects configured tenants only |

## Open Questions

None. All changes are fully specified by the locked decisions and confirmed by source inspection.

## Sources

### Primary (HIGH confidence)

- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — lines 75-89 (heartbeat injection), line 173 (TenantCount +1)
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — lines 47-59 (heartbeat bypass)
- `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs` — section 9 (lines 443-517), count assertions throughout
- `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs` — confirmed no heartbeat bypass tests exist
- `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` — `DefaultIntervalSeconds`, `HeartbeatDeviceName` constants
- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — `_liveness.Stamp(jobKey)` in `finally` confirmed unchanged

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no external libraries involved
- Architecture: HIGH — directly read from source files
- Pitfalls: HIGH — derived from static analysis of the code being deleted

**Research date:** 2026-03-15
**Valid until:** Stable until production code changes; no external dependencies.
