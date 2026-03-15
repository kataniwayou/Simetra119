# 058 — GraceMultiplier & Device-Resolved IntervalSeconds

## Objective

Add `GraceMultiplier` (double, default 2.0) to device poll groups and resolve both `IntervalSeconds` and `GraceMultiplier` from the device's poll group at tenant load time. This eliminates the need for operators to manually set `IntervalSeconds` in tenant config — it is now derived from the device config. `GraceMultiplier` is stored on `MetricSlotHolder` for future stale metric detection (no runtime logic this task).

## Why

Stale metric detection needs to know how long to wait before declaring a metric stale. The formula is `IntervalSeconds * GraceMultiplier`. Both values live on the device poll group definition, so they should be resolved at tenant load time rather than duplicated in tenant config.

---

## Task 1 — Add GraceMultiplier to PollOptions, MetricPollInfo, MetricSlotHolder

**Estimated time:** ~8 min

### Context files
- @src/SnmpCollector/Configuration/PollOptions.cs
- @src/SnmpCollector/Pipeline/MetricPollInfo.cs
- @src/SnmpCollector/Pipeline/MetricSlotHolder.cs
- @src/SnmpCollector/Services/DeviceWatcherService.cs (BuildPollGroups — wire GraceMultiplier to MetricPollInfo)

### Must-haves

1. **PollOptions.GraceMultiplier** — Add `public double GraceMultiplier { get; set; } = 2.0;` with XML doc: "Grace multiplier for stale detection. Stale threshold = IntervalSeconds * GraceMultiplier. Defaults to 2.0."
2. **MetricPollInfo.GraceMultiplier** — Add `double GraceMultiplier = 2.0` as a constructor parameter (after `TimeoutMultiplier`). This is a `sealed record` — just add the param.
3. **MetricSlotHolder.GraceMultiplier** — Add `public double GraceMultiplier { get; }` property (immutable, set in constructor). Add constructor parameter `double graceMultiplier = 2.0` after `timeSeriesSize`. Store it: `GraceMultiplier = graceMultiplier;`
4. **DeviceWatcherService.BuildPollGroups** — In the `new MetricPollInfo(...)` constructor call (~line 402), pass `GraceMultiplier: poll.GraceMultiplier`.
5. **Device config files** — Add `"GraceMultiplier": 2.0` to every poll group object in all 3 device config locations:
   - `src/SnmpCollector/config/devices.json` (local dev, 2 devices)
   - `deploy/k8s/snmp-collector/simetra-devices.yaml` (K8s dev, 3 devices)
   - `deploy/k8s/production/configmap.yaml` (production, under `simetra-devices` ConfigMap, 3 devices)

### Must-NOT-haves
- Do NOT add GraceMultiplier to `MetricSlotOptions` (it is resolved, not operator-set)
- Do NOT add GraceMultiplier to `CopyFrom` (it is config identity, not runtime state)
- `GraceMultiplier` is NOT in CopyFrom — same pattern as Threshold

### Build verification
```
dotnet build src/SnmpCollector/SnmpCollector.csproj
dotnet build tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj
dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj
```
All existing tests must pass (MetricSlotHolder constructor has default param, no breakage).

---

## Task 2 — Resolve IntervalSeconds + GraceMultiplier from Device in ValidateAndBuildTenants

**Depends on:** Task 1

**Estimated time:** ~12 min

### Context files
- @src/SnmpCollector/Services/TenantVectorWatcherService.cs (ValidateAndBuildTenants — resolution logic)
- @src/SnmpCollector/Pipeline/TenantVectorRegistry.cs (Reload — pass GraceMultiplier to holder)
- @src/SnmpCollector/Pipeline/IOidMapService.cs (ResolveToOid — for OID lookup)
- @src/SnmpCollector/Pipeline/MetricPollInfo.cs (Oids + AggregatedMetrics for matching)
- @src/SnmpCollector/Pipeline/DeviceInfo.cs (PollGroups)
- @tests/SnmpCollector.Tests/Services/TenantVectorWatcherValidationTests.cs

### Must-haves

#### 2a. ValidateAndBuildTenants — resolve IntervalSeconds + GraceMultiplier

After TEN-07 check passes (line 153), change `out _` to `out var device` to capture the `DeviceInfo`. Then, after all validation checks pass (after threshold validation, before IP resolution, ~line 170), add resolution logic:

```
// Resolve IntervalSeconds + GraceMultiplier from device poll group.
var resolvedInterval = 0;
var resolvedGrace = 2.0;
var oid = oidMapService.ResolveToOid(metric.MetricName);
if (oid is not null && device is not null)
{
    foreach (var pg in device.PollGroups)
    {
        if (pg.Oids.Contains(oid))
        {
            resolvedInterval = pg.IntervalSeconds;
            resolvedGrace = pg.GraceMultiplier;
            break;
        }
    }
}

// Fallback: check aggregated metrics (MetricName won't have an OID entry).
if (resolvedInterval == 0 && device is not null)
{
    foreach (var pg in device.PollGroups)
    {
        foreach (var agg in pg.AggregatedMetrics)
        {
            if (string.Equals(agg.MetricName, metric.MetricName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedInterval = pg.IntervalSeconds;
                resolvedGrace = pg.GraceMultiplier;
                goto resolved;
            }
        }
    }
    resolved:;
}

metric.IntervalSeconds = resolvedInterval;
```

Also add a new `GraceMultiplier` property to `MetricSlotOptions`:
- Wait, no. The planning context says NOT to add GraceMultiplier to MetricSlotOptions. But we need to carry it through to TenantVectorRegistry.Reload. We need a way to pass it.
- **Correction:** Add `public double GraceMultiplier { get; set; } = 2.0;` to `MetricSlotOptions` as a resolved (not operator-set) field. XML doc: "Resolved grace multiplier from the device's poll group. Not operator-set; populated at tenant load time."

#### 2b. TenantVectorRegistry.Reload — pass GraceMultiplier to MetricSlotHolder

In `Reload` (~line 104), update the `new MetricSlotHolder(...)` call to include `graceMultiplier: metric.GraceMultiplier`:
```csharp
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.TimeSeriesSize,
    metric.Threshold,
    metric.GraceMultiplier);
```

Wait — the MetricSlotHolder constructor currently has `threshold` as the last optional parameter. The new constructor signature from Task 1 is: `(string ip, int port, string metricName, int intervalSeconds, int timeSeriesSize = 1, ThresholdOptions? threshold = null, double graceMultiplier = 2.0)`. The call site in Reload passes `metric.Threshold` positionally — adding `graceMultiplier` after threshold is fine since it has a default.

Actually, reorder: put `graceMultiplier` before `threshold` to keep non-nullable params before nullable ones. Constructor: `(string ip, int port, string metricName, int intervalSeconds, int timeSeriesSize = 1, double graceMultiplier = 2.0, ThresholdOptions? threshold = null)`.

This means the Reload call site becomes:
```csharp
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.TimeSeriesSize,
    metric.GraceMultiplier,
    metric.Threshold);
```

The existing call uses named parameter `threshold:` — check the exact current syntax. If it uses positional, reorder. If named, just add `graceMultiplier:`.

Current call (line 104-110):
```csharp
var newHolder = new MetricSlotHolder(
    metric.Ip,
    metric.Port,
    metric.MetricName,
    metric.IntervalSeconds,
    metric.TimeSeriesSize,
    metric.Threshold);
```
This is positional. So we insert `metric.GraceMultiplier` between `metric.TimeSeriesSize` and `metric.Threshold`.

#### 2c. Tenant config files — remove manually-set IntervalSeconds

Remove any `"IntervalSeconds"` entries from tenant config files (they are now resolved from devices):
- `src/SnmpCollector/config/tenants.json` — check if any entries have IntervalSeconds (they don't currently, so no change needed)
- `deploy/k8s/snmp-collector/simetra-tenants.yaml` — same check
- `deploy/k8s/production/configmap.yaml` (tenants section) — same check

**Result:** No IntervalSeconds in tenant configs currently, so no removals needed. Just confirm.

#### 2d. Tests — add resolution tests

Add the following tests to `TenantVectorWatcherValidationTests.cs`:

1. **IntervalSeconds_ResolvedFromDevicePollGroup** — Create a `DeviceInfo` with a poll group containing OID "1.2.3" at IntervalSeconds=10. Set up `oidMapService.ResolveToOid("m1")` to return "1.2.3". Set up `deviceRegistry.TryGetByIpPort` to return the DeviceInfo. Assert `result.Tenants[0].Metrics[0].IntervalSeconds == 10`.

2. **GraceMultiplier_ResolvedFromDevicePollGroup** — Same setup but poll group has GraceMultiplier=3.0. Assert `result.Tenants[0].Metrics[0].GraceMultiplier == 3.0`.

3. **MetricNameNotInAnyPollGroup_DefaultsPreserved** — Device exists but MetricName's OID is not in any poll group. Assert IntervalSeconds=0, GraceMultiplier=2.0.

4. **AggregatedMetricName_ResolvedFromPollGroup** — Device has a poll group with AggregatedMetrics containing MetricName "agg_metric" at IntervalSeconds=15. The tenant metric uses "agg_metric". Assert IntervalSeconds=15 resolved.

### Must-NOT-haves
- No runtime stale detection logic
- Do NOT log warning if MetricName not found in any poll group (IntervalSeconds=0 is fine as default — structural only)

### Build verification
```
dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj
```
All existing + 4 new tests must pass.

---

## Verification checklist

- [ ] `PollOptions.GraceMultiplier` exists with default 2.0
- [ ] `MetricPollInfo` record has `GraceMultiplier` parameter with default 2.0
- [ ] `MetricSlotHolder.GraceMultiplier` is a public get-only property, set in constructor
- [ ] `MetricSlotHolder.CopyFrom` does NOT copy GraceMultiplier
- [ ] `MetricSlotOptions.GraceMultiplier` exists (resolved field, default 2.0)
- [ ] `ValidateAndBuildTenants` resolves IntervalSeconds + GraceMultiplier from device poll group via OID lookup
- [ ] Aggregated metrics are also resolved (fallback to AggregatedMetrics search)
- [ ] `TenantVectorRegistry.Reload` passes GraceMultiplier to MetricSlotHolder constructor
- [ ] All 3 device config files have `"GraceMultiplier": 2.0` on every poll group
- [ ] No IntervalSeconds in tenant config files (resolved from devices)
- [ ] 4 new unit tests pass
- [ ] All existing tests pass
- [ ] `dotnet build` clean
