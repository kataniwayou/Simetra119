# Phase 56: Tenant Validation Hardening - Research

**Researched:** 2026-03-18
**Domain:** C# validation logic in TenantVectorWatcherService.ValidateAndBuildTenants
**Confidence:** HIGH

## Summary

This phase addresses 8 validation audit findings in the `ValidateAndBuildTenants` method (lines 83-368 of `TenantVectorWatcherService.cs`). All changes are confined to a single static method and its unit test file. No new libraries, no architecture changes -- purely additive validation logic and log corrections within an already well-structured validation pipeline.

The existing code follows a clear pattern: sequential per-entry validation checks with `continue` to skip invalid entries. New checks slot into this pattern naturally. The test file already has 25+ tests following a consistent helper-based pattern with NSubstitute mocks.

**Primary recommendation:** Add each finding as an isolated check within the existing validation loop, following the established log-and-skip or log-and-clamp pattern. Each finding maps to exactly one code change + one test.

## Standard Stack

No new libraries needed. Everything uses what is already in the project:

### Core (Already Present)
| Library | Purpose | Used For |
|---------|---------|----------|
| Microsoft.Extensions.Logging | Structured logging | All Warning/Error logs |
| NSubstitute | Mocking | Test doubles for IOidMapService, IDeviceRegistry |
| xUnit | Test framework | All validation tests |

### Supporting (Already Present)
| Library | Purpose | Used For |
|---------|---------|----------|
| System.Net.IPAddress | IP parsing | Existing command IP validation |
| System.Collections.Frozen | FrozenDictionary | Routing index (not changed) |

**Installation:** None needed. All dependencies already present.

## Architecture Patterns

### Existing Validation Structure (Do Not Change)
```
ValidateAndBuildTenants()
  for each tenant:
    for each metric:
      check 1 → skip on fail
      check 2 → skip on fail
      ...
      check N (threshold) → clamp, don't skip
      resolve IntervalSeconds
      resolve IP
      add to cleanMetrics
    for each command:
      check 1 → skip on fail
      ...
      add to cleanCommands
    TEN-13 completeness gate
    add to cleanTenants
```

### Pattern: Log-and-Skip (for invalid entries)
**What:** Log at Warning/Error level, then `continue` to skip the entry.
**When to use:** When the entry cannot be meaningfully processed.
**Example (existing pattern):**
```csharp
if (string.IsNullOrWhiteSpace(metric.Ip))
{
    logger.LogError(
        "Tenant '{TenantId}' Metrics[{Index}] skipped: Ip is empty",
        tenantId, j);
    continue;
}
```

### Pattern: Log-and-Clamp (for recoverable issues)
**What:** Log a warning, set a safe default, and continue processing.
**When to use:** When the entry is usable but a value is out of acceptable range.
**Example (to use for SuppressionWindowSeconds):**
```csharp
if (tenantOpts.SuppressionWindowSeconds <= 0)
{
    logger.LogWarning(
        "Tenant '{TenantId}' SuppressionWindowSeconds {Value} is invalid (<= 0), clamped to default 60",
        tenantId, tenantOpts.SuppressionWindowSeconds);
    tenantOpts.SuppressionWindowSeconds = 60;
}
```

### Pattern: Pre-Loop Cross-Entry Detection (for duplicates)
**What:** Build a HashSet before or during the loop to detect duplicates.
**When to use:** When uniqueness constraints span multiple entries.
**Example (for duplicate tenant names):**
```csharp
var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < options.Tenants.Count; i++)
{
    var name = options.Tenants[i].Name;
    if (!string.IsNullOrWhiteSpace(name) && !seenNames.Add(name))
    {
        logger.LogWarning(
            "Tenant '{TenantId}' has duplicate Name -- suppression cache key collision risk",
            name);
    }
}
```

### Pattern: IP Resolution with Warning on Failure
**What:** After the AllDevices loop, check if the IP actually changed. If not and the original was not already an IP, log a warning.
**When to use:** When hostname-to-IP resolution is attempted but may fail silently.

### Anti-Patterns to Avoid
- **Changing existing Error logs to Warning:** Existing validation errors that cause entry skip must remain at Error level. New "soft" warnings (duplicate detection, IntervalSeconds=0) use Warning level since they don't skip.
- **Adding new skip semantics for findings marked as Warning:** Findings 1-4 from the audit produce warnings but do NOT skip entries (except finding 1 which is about detection, not skipping).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Duplicate detection | Custom loop comparing all pairs | `HashSet<T>.Add()` returns false on duplicate | O(n) vs O(n^2), standard pattern |
| IP validation | Regex | `System.Net.IPAddress.TryParse()` | Already used in command validation (line 318) |
| String comparison | Custom normalization | `StringComparer.OrdinalIgnoreCase` | Already used throughout codebase |

## Common Pitfalls

### Pitfall 1: Structured Log Parameter Name Mismatch
**What goes wrong:** Using `{TenantName}` in one log and `{TenantId}` in another for the same value breaks structured log queries.
**Why it happens:** Copy-paste from different parts of the codebase.
**How to avoid:** ALL tenant identification in ValidateAndBuildTenants uses `{TenantId}` and `{Index}` -- never `{TenantName}` or `{MetricIndex}`.
**Warning signs:** Finding #6 is exactly this bug on line 195.

### Pitfall 2: IP Resolution Silent Failure
**What goes wrong:** When a hostname in metric.Ip doesn't match any `ConfigAddress` in `deviceRegistry.AllDevices`, the metric loads with the raw hostname as its routing key. But fan-out uses `msg.AgentIp.ToString()` which is the resolved IP from the SNMP response. Result: routing key mismatch, metric never receives data.
**Why it happens:** The AllDevices loop (lines 240-248) just breaks on first match. No match = no change = silent.
**How to avoid:** After the loop, check if `resolvedIp == metric.Ip` AND `metric.Ip` is not a valid IP address. If so, log Warning.
**Detection logic:**
```csharp
if (resolvedIp == metric.Ip && !System.Net.IPAddress.TryParse(metric.Ip, out _))
{
    logger.LogWarning(
        "Tenant '{TenantId}' Metrics[{Index}] IP '{Ip}' could not be resolved to an IP address -- routing mismatch risk",
        tenantId, j, metric.Ip);
}
```

### Pitfall 3: IntervalSeconds=0 Meaning
**What goes wrong:** A resolved IntervalSeconds of 0 means the metric was not found in any poll group. This silently excludes the metric from staleness checks with no operator visibility.
**Why it happens:** The fallback default is 0, and no warning is logged.
**How to avoid:** After IntervalSeconds resolution (after line 233), if `resolvedInterval == 0`, log Warning.

### Pitfall 4: Duplicate Tenant Names and Suppression Cache
**What goes wrong:** The suppression key is `"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}"`. If two tenants have the same Name, they get the same Id, and their suppression keys collide. One tenant's command suppresses the other's.
**Why it happens:** No uniqueness check on tenant Name.
**How to avoid:** Check for duplicate non-null Names at the start of the outer tenant loop.

### Pitfall 5: Command IP Not Resolved
**What goes wrong:** Metric IPs go through the AllDevices resolution loop (lines 240-248) but command IPs do not. If a command uses a hostname, it stays as a hostname while the pipeline expects resolved IPs.
**Why it happens:** The resolution code block was only added to the metric loop, not the command loop.
**How to avoid:** Add the same AllDevices resolution loop after command validation passes (before `cleanCommands.Add(cmd)`).

### Pitfall 6: Comment Step Numbers
**What goes wrong:** Two checks are labeled "// 6." in the metric validation (lines 152 and 182).
**Why it happens:** A check was inserted without renumbering.
**How to avoid:** Renumber sequentially: steps 1-7 for metrics, 1-7 for commands.

## Code Examples

### Finding 1: IP Resolution Warning
```csharp
// After line 250 (metric.Ip = resolvedIp), before cleanMetrics.Add:
if (resolvedIp == metric.Ip && !System.Net.IPAddress.TryParse(metric.Ip, out _))
{
    logger.LogWarning(
        "Tenant '{TenantId}' Metrics[{Index}] IP '{Ip}' was not resolved -- possible routing mismatch",
        tenantId, j, metric.Ip);
}
```

### Finding 2: IntervalSeconds=0 Warning
```csharp
// After line 235 (metric.IntervalSeconds = resolvedInterval):
if (resolvedInterval == 0)
{
    logger.LogWarning(
        "Tenant '{TenantId}' Metrics[{Index}] IntervalSeconds is 0 (metric not in any poll group) -- excluded from staleness check",
        tenantId, j);
}
```

### Finding 3: Duplicate Tenant Name Detection
```csharp
// Before the main tenant loop (before line 91):
var seenTenantNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
// Inside the loop, after tenantId is computed (after line 96):
if (!string.IsNullOrWhiteSpace(tenantOpts.Name) && !seenTenantNames.Add(tenantOpts.Name))
{
    logger.LogWarning(
        "Tenant '{TenantId}' has duplicate Name -- suppression cache key collision risk",
        tenantId);
}
```

### Finding 4: Duplicate Metric Detection (same Ip+Port+MetricName within tenant)
```csharp
// Inside the metric loop, after all skip-checks pass but before adding to cleanMetrics:
var metricKey = $"{metric.Ip}:{metric.Port}:{metric.MetricName}";
// (use a HashSet<string> declared before the metric loop)
if (!seenMetricKeys.Add(metricKey))
{
    logger.LogWarning(
        "Tenant '{TenantId}' Metrics[{Index}] duplicate (Ip={Ip}, Port={Port}, MetricName={MetricName}) -- double-write risk",
        tenantId, j, metric.Ip, metric.Port, metric.MetricName);
}
```
Note: This warns but does NOT skip -- the audit classifies this as Low severity, and skipping would change runtime behavior.

### Finding 5: SuppressionWindowSeconds Clamping
```csharp
// After TEN-13 gate passes, before adding to cleanTenants (before line 357):
if (tenantOpts.SuppressionWindowSeconds <= 0)
{
    logger.LogWarning(
        "Tenant '{TenantId}' SuppressionWindowSeconds {Value} is invalid (<= 0), clamped to default 60",
        tenantId, tenantOpts.SuppressionWindowSeconds);
    tenantOpts.SuppressionWindowSeconds = 60;
}
```

### Finding 6: Threshold Log Parameter Fix
```csharp
// Line 195 -- change from:
logger.LogError(
    "Tenant '{TenantName}' Metrics[{MetricIndex}] threshold invalid: ...",
    tenantId, j, thr.Min, thr.Max);
// To:
logger.LogError(
    "Tenant '{TenantId}' Metrics[{Index}] threshold invalid: ...",
    tenantId, j, thr.Min, thr.Max);
```

### Finding 7: Comment Renumbering
Lines 152 and 182 both say "// 6." -- renumber line 182 to "// 7." and subsequent steps accordingly.

### Finding 8: Command IP Resolution
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

if (resolvedCmdIp == cmd.Ip && !System.Net.IPAddress.TryParse(cmd.Ip, out _))
{
    logger.LogWarning(
        "Tenant '{TenantId}' Commands[{Index}] IP '{Ip}' was not resolved -- possible routing issue",
        tenantId, k, cmd.Ip);
}

cmd.Ip = resolvedCmdIp;
```

## Insertion Points Summary

Precise locations for each change in `TenantVectorWatcherService.cs`:

| Finding | Severity | Location | Action |
|---------|----------|----------|--------|
| #1 IP resolution silent | HIGH | After line 250 | Add Warning if hostname not resolved |
| #2 IntervalSeconds=0 | MEDIUM | After line 235 | Add Warning if resolvedInterval == 0 |
| #3 Duplicate tenant Name | MEDIUM | Before line 91 (HashSet) + after line 96 (check) | Detect+warn duplicate Names |
| #4 Duplicate metric in tenant | LOW | Before metric loop (HashSet) + after all checks pass | Detect+warn duplicate Ip+Port+MetricName |
| #5 SuppressionWindow <= 0 | LOW | Before line 357 (cleanTenants.Add) | Clamp to 60 + Warning |
| #6 Threshold log params | LOW | Line 195 | Fix `{TenantName}` -> `{TenantId}`, `{MetricIndex}` -> `{Index}` |
| #7 Comment step numbers | LOW | Lines 152, 182 | Renumber to sequential |
| #8 Command IP not resolved | MEDIUM | After line 340 (before cleanCommands.Add) | Add same resolution loop as metrics |

## Test Patterns

Each finding needs one or more tests in `TenantVectorWatcherValidationTests.cs`. The test pattern is established:

```csharp
[Fact]
public void DescriptiveName_ExpectedBehavior()
{
    // Arrange: use CreateValidTenant() or build custom TenantOptions
    // Use CreatePassthroughOidMapService() and CreatePassthroughDeviceRegistry()
    // For Warning-only checks: use a real logger or capture logs

    // Act:
    var result = TenantVectorWatcherService.ValidateAndBuildTenants(
        Wrap(tenant), oidMapService, deviceRegistry, logger);

    // Assert: check result.Tenants content
}
```

**Log capture for Warning assertions:** Current tests use `NullLogger.Instance` which discards logs. For findings that only produce warnings (no behavioral change), tests should verify the behavioral outcome (e.g., SuppressionWindowSeconds is clamped to 60). For warn-only findings (duplicates, IntervalSeconds=0), the test verifies the entry still survives (not skipped).

### Test matrix:

| Finding | Test Name | Assertion |
|---------|-----------|-----------|
| #1 | `UnresolvedHostname_MetricSurvivesWithWarning` | Metric survives, Ip unchanged |
| #2 | `IntervalSecondsZero_MetricSurvivesWithWarning` | Metric survives, IntervalSeconds==0 |
| #3 | `DuplicateTenantNames_BothSurviveWithWarning` | Both tenants survive |
| #4 | `DuplicateMetricInTenant_BothSurviveWithWarning` | Both metrics survive |
| #5 | `SuppressionWindowZero_ClampedToDefault60` | SuppressionWindowSeconds==60 in output |
| #5 | `SuppressionWindowNegative_ClampedToDefault60` | SuppressionWindowSeconds==60 in output |
| #6 | (no behavioral test needed -- parameter name is log-only) | Covered by code review |
| #7 | (no test needed -- comments only) | Covered by code review |
| #8 | `CommandIp_ResolvedViaDeviceRegistryAllDevices` | cmd.Ip changed to resolved IP |
| #8 | `CommandIp_UnresolvedHostname_SurvivesWithWarning` | cmd.Ip unchanged, command survives |

## Open Questions

1. **Finding #4: Should duplicate metrics within a tenant skip the duplicate?**
   - What we know: Audit says LOW severity, warns about "double-write risk"
   - What's unclear: Whether warn-only or warn-and-skip is preferred
   - Recommendation: Warn-only (do not skip) to minimize behavioral change. The routing index already handles multiple holders per routing key.

2. **Finding #1: Should unresolved hostname cause a skip?**
   - What we know: Audit says HIGH because it causes routing mismatch
   - What's unclear: Whether to warn-only or warn-and-skip
   - Recommendation: Warn-only for now. The entry already passes TEN-07 (device registry check), so it exists. The resolution failure is a config asymmetry, not a hard error.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` lines 83-368 -- current validation logic
- `tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs` -- 25+ existing tests
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` -- routing uses `msg.AgentIp.ToString()` (line 49)
- `src/SnmpCollector/Pipeline/RoutingKey.cs` -- routing key is `(Ip, Port, MetricName)`
- `src/SnmpCollector/Jobs/SnapshotJob.cs` line 163 -- suppression key format: `"{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}"`
- `src/SnmpCollector/Configuration/TenantOptions.cs` -- SuppressionWindowSeconds default is 60

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- no new dependencies, all code is in-repo
- Architecture: HIGH -- changes follow established validation patterns exactly
- Pitfalls: HIGH -- all findings verified by reading actual source code
- Code examples: HIGH -- derived from actual codebase patterns, not hypothetical

**Research date:** 2026-03-18
**Valid until:** No expiry -- this is internal codebase research, not library version research
