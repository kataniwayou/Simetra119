# Phase 56: Tenant Validation Hardening - Research (Comprehensive Re-audit)

**Researched:** 2026-03-18
**Domain:** Complete property-by-property validation audit of all tenant config classes
**Confidence:** HIGH (all findings from direct source code analysis)

## Summary

This is a **comprehensive re-research** expanding scope from the original 8 audit findings to cover EVERY property across ALL tenant configuration classes. For each property, this documents: type, default, current validation, pipeline trace of invalid values, and recommendations.

The current validation in `ValidateAndBuildTenants` (lines 83-368 of `TenantVectorWatcherService.cs`) is already robust for most structural properties on `MetricSlotOptions` and `CommandSlotOptions`. The `TenantVectorOptionsValidator` is a no-op (always returns `Success`), so ALL real validation happens in `ValidateAndBuildTenants`.

Key findings:
1. **TenantOptions-level properties** (`Priority`, `SuppressionWindowSeconds`, `Name`) have ZERO dedicated validation
2. **`SuppressionWindowSeconds <= 0` is a command storm risk** -- suppression never fires, commands sent every cycle
3. **Duplicate tenant names** cause suppression key collisions (aliased suppression windows)
4. **Duplicate entries within a tenant** (metric slots, command slots) are not detected
5. **Command IPs are not resolved** at load time (metrics are, commands are not)
6. **Threshold log has mismatched parameter names** (`{TenantName}` vs `{TenantId}`)
7. Per-metric and per-command structural validation is comprehensive and correct

**Primary recommendation:** Add validation for `SuppressionWindowSeconds`, duplicate tenant name detection, and command IP resolution. The rest is already solid.

---

## TenantVectorOptions (Top-Level Wrapper)

| Property | Type | Default | Current Validation | Effect of Invalid Value | Recommendation |
|----------|------|---------|-------------------|------------------------|----------------|
| `Tenants` | `List<TenantOptions>` | `[]` | `TenantVectorOptionsValidator.Validate()` is a **no-op** (always returns `Success`). Empty list is valid. JSON deserialization in `HandleConfigMapChangedAsync` catches `JsonException`. Null result from deserialization is checked (line 493-498). | Empty list = no tenants loaded, system idles. Null deserialization = reload skipped with LogWarning. Malformed JSON = reload skipped with LogError, previous config retained. | **OK as-is.** |

---

## TenantOptions

| Property | Type | Default | Current Validation | Effect of Invalid Value | Recommendation |
|----------|------|---------|-------------------|------------------------|----------------|
| `Name` | `string?` | `null` | **NONE.** If null/empty, auto-generated as `"tenant-{i}"` at line 94 (`ValidateAndBuildTenants`) and line 78 (`TenantVectorRegistry.Reload`). | Null/empty is safe (auto-ID used). **Duplicate names across tenants are NOT detected.** Suppression key in `SnapshotJob` line 163 is `"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}"`. Two tenants with same Name sharing a command target get identical suppression keys, aliasing each other's suppression windows. Also causes ambiguous log messages. | **ADD: Warn on duplicate tenant names.** Do not skip -- just LogWarning. |
| `Priority` | `int` | `0` | **NONE.** Used as `SortedDictionary` key in `TenantVectorRegistry.Reload` line 73. Lower value = higher priority = evaluated first. | Any int is valid. Negative values sort correctly. All tenants with same priority go into one `PriorityGroup` and are evaluated concurrently via `Task.WhenAll` in `SnapshotJob` line 70. If advance gate blocks on group N, groups N+1..M are skipped. No crash. | **OK as-is.** Design doc says "Any integer is valid (no range constraint)." |
| `SuppressionWindowSeconds` | `int` | `60` | **NONE.** Passed through to `Tenant` constructor (line 113 in Registry, line 24 in `Tenant.cs`). Used in `SuppressionCache.TrySuppress` (line 165 of `SnapshotJob`). | **COMMAND STORM if <= 0.** `SuppressionCache.TrySuppress` checks `now - lastStamp < TimeSpan.FromSeconds(windowSeconds)`. If `windowSeconds=0`: comparison is `age < 0 seconds`, always false, suppression NEVER fires. If negative: `TimeSpan.FromSeconds(negative)` creates negative TimeSpan, same result. Commands fire every snapshot cycle without limit. | **ADD: Validate >= 1. Skip tenant or clamp to 60 with LogError.** HIGHEST priority gap. |
| `Metrics` | `List<MetricSlotOptions>` | `[]` | Each entry validated individually. After validation, TEN-13 gate (line 344-355) requires >= 1 Evaluate + >= 1 Resolved metric + >= 1 command, or tenant is skipped. | Empty after validation = tenant skipped by TEN-13. Safe. | **OK as-is.** |
| `Commands` | `List<CommandSlotOptions>` | `[]` | Each entry validated individually. TEN-13 gate requires >= 1 command. | Empty after validation = tenant skipped. Safe. | **OK as-is.** |

---

## MetricSlotOptions

| Property | Type | Default | Current Validation | Effect of Invalid Value | Recommendation |
|----------|------|---------|-------------------|------------------------|----------------|
| `Ip` | `string` | `""` | Line 108: `IsNullOrWhiteSpace` -> skip with LogError. Line 183-188: `deviceRegistry.TryGetByIpPort` (TEN-07) -> skip if not found. Lines 240-248: resolved via `deviceRegistry.AllDevices` ConfigAddress match. | Empty = skipped. Not in device registry = skipped. Malformed IP that doesn't match = skipped by TEN-07. **If hostname matches TEN-07 but NOT AllDevices loop**: metric loads with raw hostname as routing key. Fan-out uses `msg.AgentIp.ToString()` (resolved IP). Result: routing key mismatch, metric never receives data -- **silent failure**. | **ADD: Warn if IP not resolved** (hostname remains after AllDevices loop). See Code Examples. |
| `Port` | `int` | `161` | Line 117: `< 1 or > 65535` -> skip with LogError. | Out of range = skipped. JSON `0` for missing int caught by `< 1`. Default 161 is valid. | **OK as-is.** |
| `MetricName` | `string` | `""` | Line 126: `IsNullOrWhiteSpace` -> skip. Lines 153-179: checked against `oidMapService.ContainsMetricName` with fallback to device aggregate metrics (TEN-05). | Empty = skipped. Not in OID map and not aggregate = skipped. | **OK as-is.** |
| `Role` | `string` | `""` | Line 135: must be exactly `"Evaluate"` or `"Resolved"` (case-sensitive string comparison). | Empty or other value = skipped with LogError. Note: case-sensitive. `"evaluate"` is rejected. This is intentional -- JSON `PropertyNameCaseInsensitive` only affects property NAMES, not values. | **OK as-is.** |
| `TimeSeriesSize` | `int` | `1` | Line 144: `< 1` -> skip with LogError. | 0 or negative = skipped. **No upper bound.** Huge values (e.g., 999999) accepted. `MetricSlotHolder.WriteValue` does `ImmutableArray.RemoveAt(0).Add(sample)` which is O(n). Large TimeSeriesSize = slow writes per sample. Also allocates large immutable arrays in memory. | **CONSIDER: Cap at 1000.** LOW priority -- unlikely in practice. |
| `IntervalSeconds` | `int` | `0` | **Not operator-validated.** System-resolved from device poll group at lines 201-235. If resolution fails (OID not in any poll group), remains 0. | `IntervalSeconds=0` excludes holder from staleness in `SnapshotJob.HasStaleness` line 209 (`holder.IntervalSeconds == 0 -> continue`). This is BY DESIGN for trap/command-sourced and unresolved metrics. But if resolution SHOULD have succeeded (operator config error), metric silently escapes staleness. No crash, no command storm. | **ADD: Warn if resolvedInterval=0** after resolution attempt. Not a skip -- informational. |
| `GraceMultiplier` | `double` | `2.0` | **Not operator-validated.** System-resolved from device poll group alongside IntervalSeconds. Falls back to 2.0 if unresolved. | Used in staleness: `IntervalSeconds * GraceMultiplier`. If IntervalSeconds=0, GraceMultiplier is irrelevant (holder skipped from staleness). Cannot be negative from operator config (system-resolved from device PollOptions which has its own validation). | **OK as-is.** System-resolved. |
| `Threshold` | `ThresholdOptions?` | `null` | Line 192-198: if both Min and Max set and Min > Max, threshold cleared to null with LogError. Metric still loads. | null = `IsViolated` returns true (always violated). This is BY DESIGN: "any value triggers." See ThresholdOptions table for full semantic matrix. **Note:** Log at line 195 uses `{TenantName}` and `{MetricIndex}` instead of the codebase-standard `{TenantId}` and `{Index}`. | **FIX: Correct log parameter names** at line 195. |

---

## CommandSlotOptions

| Property | Type | Default | Current Validation | Effect of Invalid Value | Recommendation |
|----------|------|---------|-------------------|------------------------|----------------|
| `Ip` | `string` | `""` | Line 265: `IsNullOrWhiteSpace` -> skip. Line 327: `deviceRegistry.TryGetByIpPort` (TEN-07) -> skip if not found. **NOT resolved** via AllDevices loop (unlike metrics). | Empty = skipped. Not in registry = skipped. **If Ip is a hostname**: passes TEN-07 (device registry matches by ConfigAddress) but `CommandRequest` carries the raw hostname. At execution time in `CommandWorkerService.ExecuteCommandAsync` line 112, `_deviceRegistry.TryGetByIpPort(req.Ip, req.Port, ...)` re-resolves. If device was renamed/removed between load and execution, command fails with LogWarning. The inconsistency with metric IP resolution is the real concern. | **ADD: Resolve command IPs** via the same AllDevices loop as metrics (lines 240-248). Keeps metric and command IPs consistent. |
| `Port` | `int` | `161` | Line 274: `< 1 or > 65535` -> skip with LogError. | Out of range = skipped. | **OK as-is.** |
| `CommandName` | `string` | `""` | Line 283: `IsNullOrWhiteSpace` -> skip. **NOT checked against command map at load time** (TEN-06: resolution deferred to execution). | Empty = skipped. Non-existent name passes validation. At execution in `CommandWorkerService` line 101-109: `ResolveCommandOid` returns null, command skipped with LogWarning. Suppression stamp is NOT set (check happens before `TrySuppress`), so next cycle retries and fails again -- infinite warning loop per cycle. | **CONSIDER: LogWarning at load time** if CommandName not in command map. Do not skip (map could change). LOW priority -- runtime handling is safe. |
| `Value` | `string` | `""` | Line 300: `IsNullOrWhiteSpace` -> skip. Lines 310-324: type-compatibility check: `Integer32` -> `int.TryParse`, `IpAddress` -> `IPAddress.TryParse`. `OctetString` accepts any non-empty string. | Empty = skipped. Type mismatch = skipped. At runtime, `SharpSnmpClient.ParseSnmpData` would throw on mismatch, but validation prevents this. | **OK as-is.** Comprehensive. |
| `ValueType` | `string` | `""` | Line 292: must be exactly `"Integer32"`, `"IpAddress"`, or `"OctetString"` (case-sensitive). | Empty or unknown = skipped. `SharpSnmpClient.ParseSnmpData` has a `_ => throw ArgumentException` default case, but this is unreachable due to validation. | **OK as-is.** |

---

## ThresholdOptions

| Property | Type | Default | Current Validation | Effect of Invalid Value | Recommendation |
|----------|------|---------|-------------------|------------------------|----------------|
| `Min` | `double?` | `null` | Cross-validated with Max: if both non-null and Min > Max, threshold cleared to null (line 192-198). | null = no lower bound. `NaN`: `System.Text.Json` does NOT support NaN/Infinity by default -> `JsonException` at deserialization, **entire ConfigMap reload aborted**. `double.MaxValue` would make Min unreachable -> always violated. `double.MinValue` would make Min always satisfied -> only Max matters. | **OK as-is.** JSON rejects NaN. Extreme values are operator error but not dangerous. |
| `Max` | `double?` | `null` | Same cross-validation as Min. | Same as Min (mirrored). `double.MinValue` on Max -> everything violates Max -> always violated. | **OK as-is.** |

### Threshold Semantic Matrix

| Min | Max | `IsViolated` Behavior | Design Intent |
|-----|-----|----------------------|---------------|
| null | null | Always true (violated) | "Any value triggers" |
| null | set | true if `value > Max` | Upper-bound-only check |
| set | null | true if `value < Min` | Lower-bound-only check |
| set | set, Min < Max | true if `value < Min OR value > Max`. Boundary values are IN-range (strict inequality). | Standard range check |
| set | set, Min == Max | true if `value == Min` (equality check) | Exact-match trigger |
| set | set, Min > Max | **Cleared to null at load time** -> always violated | Invalid config, logged as error |

---

## Cross-Property Interactions

| Interaction | Current Handling | Effect | Recommendation |
|-------------|-----------------|--------|----------------|
| **Threshold.Min > Threshold.Max** | Detected. Threshold cleared to null, metric loads. LogError. | Metric becomes "always violated." May trigger unexpected commands if all Evaluate metrics are also "always violated." | **OK as-is.** |
| **SuppressionWindowSeconds <= 0 + Commands exist** | NOT detected. | Commands fire every snapshot cycle. Command storm to devices. | **ADD: Validate SuppressionWindowSeconds >= 1.** |
| **All metrics same Role (all Evaluate OR all Resolved)** | Detected by TEN-13 gate. Tenant skipped. | Safe. | **OK as-is.** |
| **IntervalSeconds=0 + GraceMultiplier** | IntervalSeconds=0 exempts from staleness. GraceMultiplier irrelevant. | Correct for unresolved metrics. | **OK as-is.** |
| **Null threshold on Evaluate metric** | `IsViolated` returns true -> metric always violated. | If ALL Evaluate metrics have null threshold, they are ALL always violated -> commands fire every cycle (subject to suppression). With proper suppression, commands fire once per window. | **OK as-is.** This is by design -- "no threshold = always violated" is intentional semantic. |

---

## Cross-Entry Interactions (Duplicates)

| Duplicate Type | Current Handling | Effect | Recommendation |
|----------------|-----------------|--------|----------------|
| **Duplicate tenant Name** | NOT detected. Both load. | Suppression key collision: `"{tenantId}:{ip}:{port}:{cmd}"`. Two tenants sharing a command target alias each other's windows. Ambiguous logs. | **ADD: LogWarning on duplicate.** |
| **Duplicate metric slot within a tenant** (same Ip+Port+MetricName) | NOT detected. Both get separate `MetricSlotHolder` instances. Routing index maps to `IReadOnlyList<MetricSlotHolder>` -- both receive data. | Wastes memory. Both evaluate identically. Could confuse if one has threshold and other doesn't. | **ADD: LogWarning.** LOW priority. |
| **Duplicate metric slot across tenants** | By design. Routing index fans out to all matching holders. | Correct -- multiple tenants watching same metric is the core design. | **OK as-is.** |
| **Duplicate command slot within a tenant** (same Ip+Port+CommandName) | NOT detected. Both loaded. | In Tier 4: first command fires and sets suppression stamp. Second command in same loop iteration checks TrySuppress with same key -> suppressed. Net: only first fires per window. Minor waste but no harm. | **ADD: LogWarning.** LOW priority. |

---

## Cross-Config Interactions

| Interaction | Current Handling | Effect | Recommendation |
|-------------|-----------------|--------|----------------|
| **Metric references device not in DeviceRegistry** | TEN-07: entry skipped with LogError. | Safe. | **OK as-is.** |
| **MetricName not in OID map or aggregates** | TEN-05: entry skipped with LogError. | Safe. | **OK as-is.** |
| **CommandName not in command map** | TEN-06: deferred. Checked at execution time. | Safe at runtime (LogWarning + skip). Repeated warning per cycle. | **CONSIDER: LogWarning at load.** |
| **Device config changes after tenant load** | No re-validation. Commands: `ExecuteCommandAsync` re-resolves device at execution time (line 112). Metrics: hold stale resolved IPs but data just stops arriving (no crash). | Stale until next tenant reload. | **OK as-is.** Watch-based reload handles. |
| **Tenant config loaded before device config** | Possible on startup. `ValidateAndBuildTenants` checks DeviceRegistry which may be empty. All entries fail TEN-07. | Empty tenant vector until next reload. | **OK as-is.** Operational concern, not code fix. |

---

## Timing/Ordering Issues

| Issue | Current Handling | Effect | Recommendation |
|-------|-----------------|--------|----------------|
| **Concurrent reload requests** | `SemaphoreSlim _reloadLock` in watcher service (line 51). | Safe. | **OK as-is.** |
| **Registry swap during SnapshotJob** | `_registry.Groups` is volatile. Job reads once per cycle. | Safe -- consistent snapshot. | **OK as-is.** |
| **SuppressionCache stale entries** | Lazy expiry. Dead keys from deleted tenants persist. | No functional harm. Memory bounded (small string keys). | **OK as-is.** |

---

## Summary of All Gaps

### HIGH Priority

| # | Gap | Risk | Location | Recommendation |
|---|-----|------|----------|----------------|
| 1 | `SuppressionWindowSeconds` not validated | **Command storm** if <= 0 | `ValidateAndBuildTenants`, tenant level | Validate >= 1. Clamp to 60 or skip tenant. |
| 2 | Metric IP resolution silent failure | **Silent data loss** -- metric never receives data | After AllDevices loop (line 250) | LogWarning if hostname not resolved. |
| 3 | Command IPs not resolved at load time | **Inconsistency** with metric IP resolution | After command validation | Add same AllDevices resolution loop. |

### MEDIUM Priority

| # | Gap | Risk | Location | Recommendation |
|---|-----|------|----------|----------------|
| 4 | Duplicate tenant names not detected | Suppression key collision | Before tenant loop | LogWarning on duplicate. |
| 5 | `IntervalSeconds=0` after resolution attempt | Operator blind spot (excluded from staleness) | After IntervalSeconds resolution | LogWarning. |
| 6 | Threshold log parameter name mismatch | Bad structured log queries | Line 195 | Fix `{TenantName}` -> `{TenantId}`, `{MetricIndex}` -> `{Index}`. |

### LOW Priority

| # | Gap | Risk | Location | Recommendation |
|---|-----|------|----------|----------------|
| 7 | `TimeSeriesSize` no upper bound | Perf risk for huge values | Metric validation | Cap at 1000. |
| 8 | Duplicate metric slots within tenant | Wasted memory | After metric validation | LogWarning. |
| 9 | Duplicate command slots within tenant | Redundant commands | After command validation | LogWarning. |
| 10 | CommandName not checked at load time | Operator typo only caught at runtime | Command validation | LogWarning (not skip). |
| 11 | Comment step numbers duplicated | Code cleanliness | Lines 152, 182 | Renumber sequentially. |

---

## Architecture Patterns

### Existing Validation Structure (Do Not Change)
```
ValidateAndBuildTenants()
  for each tenant:
    // [INSERT: tenant-level checks here -- SuppressionWindowSeconds, etc.]
    for each metric:
      check 1: empty Ip -> skip
      check 2: port range -> skip
      check 3: empty MetricName -> skip
      check 4: Role validation -> skip
      check 5: TimeSeriesSize >= 1 -> skip
      check 6: MetricName in OID map/aggregates (TEN-05) -> skip
      check 7: IP+Port in DeviceRegistry (TEN-07) -> skip
      check 8: Threshold Min > Max -> clear threshold, don't skip
      resolve IntervalSeconds + GraceMultiplier from device
      resolve IP via AllDevices
      // [INSERT: post-resolution warnings here]
      add to cleanMetrics
    for each command:
      check 1: empty Ip -> skip
      check 2: port range -> skip
      check 3: empty CommandName -> skip
      check 4: ValueType validation -> skip
      check 5: empty Value -> skip
      check 6: Value+ValueType parse -> skip
      check 7: IP+Port in DeviceRegistry (TEN-07) -> skip
      // [INSERT: IP resolution + warnings here]
      add to cleanCommands
    TEN-13 completeness gate (Evaluate + Resolved + Commands)
    // [INSERT: SuppressionWindowSeconds clamp here]
    add to cleanTenants
```

### Pattern: Log-and-Skip (for invalid entries)
Existing pattern. LogError + `continue`. Used when entry cannot be processed.

### Pattern: Log-and-Clamp (for recoverable issues)
LogWarning + set safe default. Used for SuppressionWindowSeconds.

### Pattern: Pre-Loop Duplicate Detection
HashSet before/during loop. LogWarning on duplicate. Do NOT skip.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Duplicate detection | Custom O(n^2) comparison | `HashSet<T>.Add()` returns false | O(n), standard pattern |
| IP validation | Regex | `System.Net.IPAddress.TryParse()` | Already used in command validation |
| String comparison | Custom normalization | `StringComparer.OrdinalIgnoreCase` | Already used throughout codebase |

## Common Pitfalls

### Pitfall 1: Adding checks to TenantVectorOptionsValidator
**What goes wrong:** The `IValidateOptions<T>` validator returns Success/Failed for the ENTIRE config. Any failure rejects the whole reload.
**How to avoid:** Add per-entry checks in `ValidateAndBuildTenants` which uses skip-entry semantics.

### Pitfall 2: Skipping entries for LogWarning-level issues
**What goes wrong:** A warning-level finding (duplicate name, IntervalSeconds=0) causes an entry skip, removing a functional tenant.
**How to avoid:** LogWarning for advisory. LogError + skip only for entries that would cause runtime failures.

### Pitfall 3: Structured log parameter mismatches
**What goes wrong:** Using `{TenantName}` vs `{TenantId}` breaks structured log queries.
**How to avoid:** ALL tenant identification uses `{TenantId}` and `{Index}` consistently.

## Code Examples

### SuppressionWindowSeconds validation (tenant-level, before TEN-13 gate)
```csharp
// Before cleanTenants.Add, after TEN-13 gate passes:
if (tenantOpts.SuppressionWindowSeconds < 1)
{
    logger.LogWarning(
        "Tenant '{TenantId}' SuppressionWindowSeconds {Value} is invalid (< 1), clamped to default 60",
        tenantId, tenantOpts.SuppressionWindowSeconds);
    tenantOpts.SuppressionWindowSeconds = 60;
}
```

### Duplicate tenant name detection
```csharp
// Before main tenant loop:
var seenTenantNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

// Inside loop, after tenantId computed:
if (!string.IsNullOrWhiteSpace(tenantOpts.Name) && !seenTenantNames.Add(tenantOpts.Name))
{
    logger.LogWarning(
        "Tenant '{TenantId}' has duplicate Name -- suppression cache key collision risk",
        tenantId);
}
```

### IP resolution warning (after AllDevices loop for metrics)
```csharp
// After metric.Ip = resolvedIp (line 250):
if (resolvedIp == metric.Ip && !System.Net.IPAddress.TryParse(metric.Ip, out _))
{
    logger.LogWarning(
        "Tenant '{TenantId}' Metrics[{Index}] IP '{Ip}' was not resolved -- possible routing mismatch",
        tenantId, j, metric.Ip);
}
```

### Command IP resolution (mirror of metric resolution)
```csharp
// After command validation passes, before cleanCommands.Add(cmd):
var resolvedCmdIp = cmd.Ip;
foreach (var registeredDevice in deviceRegistry.AllDevices)
{
    if (string.Equals(registeredDevice.ConfigAddress, cmd.Ip, StringComparison.OrdinalIgnoreCase))
    {
        logger.LogDebug("Resolved tenant command IP {ConfigIp} -> {ResolvedIp}", cmd.Ip, registeredDevice.ResolvedIp);
        resolvedCmdIp = registeredDevice.ResolvedIp;
        break;
    }
}
cmd.Ip = resolvedCmdIp;
```

### Threshold log fix (line 195)
```csharp
// Change from:
logger.LogError(
    "Tenant '{TenantName}' Metrics[{MetricIndex}] threshold invalid: Min {Min} > Max {Max} -- threshold cleared, metric still loads",
    tenantId, j, thr.Min, thr.Max);
// To:
logger.LogError(
    "Tenant '{TenantId}' Metrics[{Index}] threshold invalid: Min {Min} > Max {Max} -- threshold cleared, metric still loads",
    tenantId, j, thr.Min, thr.Max);
```

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Configuration/TenantOptions.cs` -- all 5 properties audited
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` -- all 8 properties audited
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` -- all 5 properties audited
- `src/SnmpCollector/Configuration/ThresholdOptions.cs` -- both properties audited
- `src/SnmpCollector/Configuration/TenantVectorOptions.cs` -- wrapper class audited
- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` -- confirmed no-op
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` -- `ValidateAndBuildTenants` lines 83-368, `HandleConfigMapChangedAsync` lines 468-529
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` -- `Reload` method, routing index construction
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` -- constructor, WriteValue, TimeSeriesSize behavior
- `src/SnmpCollector/Pipeline/Tenant.cs` -- constructor, SuppressionWindowSeconds propagation
- `src/SnmpCollector/Pipeline/SuppressionCache.cs` -- `TrySuppress` TimeSpan comparison
- `src/SnmpCollector/Jobs/SnapshotJob.cs` -- all 4 tiers, staleness exclusion, suppression key format, IsViolated
- `src/SnmpCollector/Services/CommandWorkerService.cs` -- `ExecuteCommandAsync`, runtime device resolution
- `src/SnmpCollector/Pipeline/SharpSnmpClient.cs` -- `ParseSnmpData` switch expression

## Metadata

**Confidence breakdown:**
- Property inventory: HIGH -- all 4 config classes read line-by-line
- Current validation coverage: HIGH -- `ValidateAndBuildTenants` traced line-by-line
- Effect of invalid values: HIGH -- traced through SnapshotJob, CommandWorkerService, SuppressionCache, MetricSlotHolder
- Cross-entry/cross-config: HIGH -- routing index, suppression key, device registry interactions traced
- Gap recommendations: HIGH -- based on concrete code paths with specific line numbers

**Research date:** 2026-03-18
**Valid until:** Indefinite (internal codebase analysis, not library-version dependent)
