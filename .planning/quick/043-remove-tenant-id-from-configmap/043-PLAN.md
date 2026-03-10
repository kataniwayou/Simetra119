---
phase: quick-043
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/TenantOptions.cs
  - src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
  - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
  - src/SnmpCollector/config/tenantvector.json
  - deploy/k8s/production/configmap.yaml
  - deploy/k8s/snmp-collector/simetra-tenantvector.yaml
  - tests/e2e/scenarios/28-tenantvector-routing.sh
  - tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
  - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
autonomous: true

must_haves:
  truths:
    - "TenantOptions no longer has an Id property — operators do not specify Id in JSON config"
    - "TenantVectorRegistry auto-generates tenant-{index} Ids at Reload() time"
    - "Carry-over uses (ip, port, metricName) key — no tenantId in lookup"
    - "Validator no longer checks for Id required or duplicate Ids"
    - "All unit tests pass with no Id in TenantOptions test data"
    - "All config files (local JSON, dev ConfigMap, prod ConfigMap) have no Id field"
    - "E2e scenario 28 has no Id field in inline JSON"
  artifacts:
    - path: "src/SnmpCollector/Configuration/TenantOptions.cs"
      provides: "TenantOptions without Id property"
    - path: "src/SnmpCollector/Pipeline/TenantVectorRegistry.cs"
      provides: "Auto-generated tenant Ids, simplified carry-over key, simplified diff log"
  key_links:
    - from: "TenantVectorRegistry.Reload()"
      to: "new Tenant(tenantId, ...)"
      via: "auto-generated tenant-{i} string"
      pattern: 'tenant-\{i\}'
---

<objective>
Remove operator-facing `Id` field from TenantOptions (ConfigMap config model). Auto-generate `tenant-{index}` Ids at TenantVectorRegistry.Reload() time. Simplify carry-over key from 4-tuple to 3-tuple (ip, port, metricName). Simplify diff logging to count-based (added/removed/unchanged tenant-name diff is meaningless with positional Ids). Remove Id validation rules. Strip Id from all config files and test data.

Purpose: Reduces operator config burden — one less field to manage per tenant, eliminates a class of config errors (duplicate/missing Id).
Output: Cleaner TenantOptions, simplified registry reload, updated tests and configs.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Configuration/TenantOptions.cs
@src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
@src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
@src/SnmpCollector/Pipeline/Tenant.cs
@src/SnmpCollector/config/tenantvector.json
@deploy/k8s/production/configmap.yaml
@deploy/k8s/snmp-collector/simetra-tenantvector.yaml
@tests/e2e/scenarios/28-tenantvector-routing.sh
@tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
@tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
@tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Remove Id from TenantOptions, validator, and TenantVectorRegistry</name>
  <files>
    src/SnmpCollector/Configuration/TenantOptions.cs
    src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
    src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
  </files>
  <action>
**TenantOptions.cs:**
- Remove the `Id` property entirely (line 12). Remove the XML doc comment for Id (lines 9-11).
- Update the class-level doc comment to remove "unique ID" mention — change to "Each tenant has a priority for scheduling and a list of metric slots."

**TenantVectorOptionsValidator.cs:**
- Remove the `seenIds` HashSet declaration (line 36).
- Remove Rule 1 block: the `if (string.IsNullOrWhiteSpace(tenant.Id))` block (lines 43-46).
- Remove Rule 2 block: the `else { if (!seenIds.Add(tenant.Id)) ... }` block (lines 47-54).
- In Rule 7 duplicate metric error message (line 109), replace `within tenant '{tenant.Id}'` with `within Tenants[{i}]` since there's no Id anymore.
- Renumber rule comments: Rule 3 becomes Rule 1, Rule 4 becomes Rule 2, etc. (optional but clean).

**TenantVectorRegistry.cs — Reload() method:**

1. **Auto-generate Id:** Change `foreach (var tenantOpts in options.Tenants)` (line 101) to a `for` loop:
   ```csharp
   for (var i = 0; i < options.Tenants.Count; i++)
   {
       var tenantOpts = options.Tenants[i];
       var tenantId = $"tenant-{i}";
   ```

2. **Simplify carry-over key** (Step 2, lines 67-80): Change the 4-tuple to a 3-tuple using RoutingKey which already exists:
   ```csharp
   // Step 2: Build lookup for carry-over: (ip, port, metricName) -> holder.
   var oldSlotLookup = new Dictionary<RoutingKey, MetricSlotHolder>(RoutingKeyComparer.Instance);
   foreach (var group in oldGroups)
       foreach (var tenant in group.Tenants)
           foreach (var holder in tenant.Holders)
               oldSlotLookup[new RoutingKey(holder.Ip, holder.Port, holder.MetricName)] = holder;
   ```
   Note: when multiple tenants have the same (ip, port, metricName), later entries overwrite earlier ones — this is fine because fan-out writes the same value to all matching holders, so any old holder's value is equivalent.

3. **Update carry-over lookup** (line 115): Use RoutingKey:
   ```csharp
   var lookupKey = new RoutingKey(metric.Ip, metric.Port, metric.MetricName);
   ```

4. **Use auto-generated Id for Tenant constructor** (line 130):
   ```csharp
   var tenant = new Tenant(tenantId, tenantOpts.Priority, holders);
   ```

5. **Simplify diff logging** (Step 3, lines 83-92 and Step 9, lines 181-189): Remove the oldTenantIds/newTenantIds/addedTenants/removedTenants/unchangedTenants computation entirely. Replace the log message with count-based:
   ```csharp
   _logger.LogInformation(
       "TenantVectorRegistry reloaded: tenants={TenantCount}, slots={SlotCount}, carried_over={CarriedOver}",
       TenantCount,
       SlotCount,
       carriedOver);
   ```

6. **Remove StringTupleComparer** (lines 212-233): Delete the entire `StringTupleComparer` nested class — it's no longer used since carry-over now uses RoutingKey + RoutingKeyComparer.

7. Update the XML doc comment on Reload() Step 2 to reflect the simplified key.
  </action>
  <verify>
    Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` — must compile with no errors. No references to `TenantOptions.Id` or `StringTupleComparer` should remain in the source (except `Tenant.Id` which stays).
  </verify>
  <done>
    TenantOptions has no Id property. Validator has no Id rules. Registry auto-generates tenant-{index} Ids, uses (ip, port, metricName) carry-over key, logs count-based diff. StringTupleComparer is deleted. Project compiles clean.
  </done>
</task>

<task type="auto">
  <name>Task 2: Update all config files, tests, and e2e scenarios</name>
  <files>
    src/SnmpCollector/config/tenantvector.json
    deploy/k8s/production/configmap.yaml
    deploy/k8s/snmp-collector/simetra-tenantvector.yaml
    tests/e2e/scenarios/28-tenantvector-routing.sh
    tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
    tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
  </files>
  <action>
**Config files — remove `"Id": "..."` lines:**

1. `src/SnmpCollector/config/tenantvector.json` — Remove `"Id": "fiber-monitor"` (line 5) and `"Id": "traffic-baseline"` (line 15). Just leave Priority and Metrics.

2. `deploy/k8s/production/configmap.yaml` — In the `simetra-tenantvector` data section (starting around line 433), remove all `"Id": "npb-trap"`, `"Id": "npb-poll"`, `"Id": "obp-poll"` lines from each tenant object. Also update the header comment block (lines 419-420) to remove "Id" from the field list: delete the line `#   Id             - Unique tenant identifier (e.g., "fiber-monitor")`.

3. `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` — Remove all `"Id": "..."` lines from each tenant object. Same as above.

**E2e scenario 28:**

4. `tests/e2e/scenarios/28-tenantvector-routing.sh` — Remove all `"Id": "..."` lines from both inline JSON blocks:
   - The 4-tenant hot-reload block (lines 129-169): remove `"Id": "npb-trap"`, `"Id": "npb-poll"`, `"Id": "obp-poll"`, `"Id": "obp-poll-2"` from each tenant.
   - In sub-scenario 28d verification (lines 181-187): The test currently checks for `"added"` and `"obp-poll-2"` in logs. Since diff logging now only emits counts (no tenant names), change the verification to check for the reload log with `tenants=4`:
     ```bash
     if echo "$POD_LOGS" | grep "reloaded" > /dev/null 2>&1 && echo "$POD_LOGS" | grep "tenants=4" > /dev/null 2>&1; then
         FOUND_RELOAD=1
         RELOAD_EVIDENCE="pod=${POD} logged reload with tenants=4"
         break
     fi
     ```
   - Update the failure message to match: `"No pod logged 'reloaded' with 'tenants=4' within 30s window"`.

**Unit tests:**

5. `tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs`:
   - Remove `Id = "test-tenant"` from `ValidOptions()` helper (line 36).
   - Remove `Id = "second-tenant"` from `Validate_MultipleTenants_ReturnsSuccess` (line 66).
   - Remove `Id = "empty-metrics"` from `Validate_EmptyMetricsArray_ReturnsSuccess` (line 93).
   - Remove `Id = "tenant-a"` and `Id = "tenant-b"` from `Validate_CrossTenantOverlap_ReturnsSuccess` (lines 121, 127).
   - Delete `Validate_EmptyTenantId_Fails` test entirely (lines 167-176).
   - Delete `Validate_WhitespaceTenantId_Fails` test entirely (lines 178-187).
   - Delete `Validate_DuplicateTenantIds_Fails` test entirely (lines 189-203).
   - Delete `Validate_DuplicateTenantIdsCaseInsensitive_Fails` test entirely (lines 205-220).
   - In `Validate_DuplicateMetricWithinTenant_Fails` (line 303): the assertion checks for "duplicate metric slot" which still exists — keep it.
   - In `Validate_MultipleErrorsCollected_ReportsAll` (lines 307-333): Remove `Id = ""` from the test data (line 315). Update the expected error count: previously 4 errors (empty Id + empty IP + invalid port + empty metric name), now 3 errors (empty IP + invalid port + empty metric name). Change `Assert.True(result.Failures.Count() >= 3` to `>= 3` — this is already correct, keep as-is. But the comment "Error 1: empty ID" should be removed.
   - In `Validate_ErrorMessages_ContainPathContext` (line 365): Remove `Id` if it had one — but looking at the test, it uses `ValidOptions()` which we already fixed.

6. `tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs`:
   - `CreateOptions` helper (lines 27-57): Remove `tenantId` parameter from the tuple. Change signature to `params (int priority, string ip, int port, string metricName)[] metrics`. Remove `tenantId` from the foreach destructuring. Remove `tenantMap` keying by tenantId — instead, use a simple list approach since tenants no longer have operator-assigned Ids. Group by priority to maintain same behavior: each unique priority gets its own tenant entry, OR simpler: each call to `CreateOptions` creates one tenant per entry in the params array if they have different (priority) values, otherwise groups them.

   Actually, the simplest approach: Since TenantOptions no longer has Id, the helper should group metrics into tenants by some other key. The existing tests use tenantId to group — we need to preserve that grouping. Use an index-based approach: add a `tenantIndex` parameter instead, or group by a `(int tenantIndex, int priority)` tuple. Simplest: change to `params (int tenantIndex, int priority, string ip, int port, string metricName)[] metrics` and group by `tenantIndex`:

   ```csharp
   private static TenantVectorOptions CreateOptions(
       params (int tenantIndex, int priority, string ip, int port, string metricName)[] metrics)
   {
       var tenantMap = new Dictionary<int, (int priority, List<MetricSlotOptions> slots)>();
       foreach (var (tenantIndex, priority, ip, port, metricName) in metrics)
       {
           if (!tenantMap.TryGetValue(tenantIndex, out var entry))
           {
               entry = (priority, new List<MetricSlotOptions>());
               tenantMap[tenantIndex] = entry;
           }
           entry.slots.Add(new MetricSlotOptions { Ip = ip, Port = port, MetricName = metricName });
       }
       return new TenantVectorOptions
       {
           Tenants = tenantMap.OrderBy(kvp => kvp.Key).Select(kvp => new TenantOptions
           {
               Priority = kvp.Value.priority,
               Metrics = kvp.Value.slots
           }).ToList()
       };
   }
   ```

   Then update ALL call sites. Replace tenant name strings with integer indices:
   - `"tenant-a"` -> `0`
   - `"tenant-b"` -> `1`
   - `"tenant-c"` -> `2`
   - `"tenant-high"` -> `0`, `"tenant-low"` -> `1`, `"tenant-mid"` -> `2`

   For example: `CreateOptions(("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"))` becomes `CreateOptions((0, 1, "10.0.0.1", 161, "hrProcessorLoad"))`.

   - In `Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots` (line 108): Change `Assert.Equal("tenant-a", tenant.Id)` to `Assert.Equal("tenant-0", tenant.Id)` since auto-generated Ids are now `tenant-{index}`.

   - In `Reload_LogsDiffInformation` (lines 348-371): The log format changed — no more `added=`, `removed=`, `unchanged=` fields. Update assertions: remove checks for those fields. Keep checks for `"reloaded"`, `"tenants="`, `"slots="`. Also check for `"carried_over="`.

7. `tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs`:
   - In `CreateRegistryWithRoute` (lines 305-327): Remove `Id = "tenant-a"` from TenantOptions (line 317).
   - In `CreateRegistryWithTwoTenants` (lines 329-360): Remove `Id = "tenant-a"` (line 341) and `Id = "tenant-b"` (line 351).
   - In `CreateRegistryWithThreeTenants` (lines 362-402): Remove `Id = "tenant-a"` (line 375), `Id = "tenant-b"` (line 387), `Id = "tenant-c"` (line 397).
  </action>
  <verify>
    Run `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` — all tests must pass. Grep for `"Id"` in all modified config/test files to confirm no stale Id references remain (excluding `Tenant.Id` runtime class and unrelated fields).
  </verify>
  <done>
    All config files have no Id field in tenant entries. All unit tests pass without Id in test data. E2e scenario 28 uses count-based reload verification. No compilation errors, no test failures.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles clean
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` all tests pass
3. `grep -rn '"Id"' src/SnmpCollector/config/tenantvector.json deploy/k8s/snmp-collector/simetra-tenantvector.yaml deploy/k8s/production/configmap.yaml` returns no matches in tenant entries
4. `grep -n 'tenant.Id\|tenantOpts.Id\|StringTupleComparer' src/SnmpCollector/` returns no matches (only `Tenant.Id` in Tenant.cs is expected)
</verification>

<success_criteria>
- TenantOptions.cs has no Id property
- TenantVectorOptionsValidator has no Id validation rules
- TenantVectorRegistry auto-generates tenant-{index} Ids, uses 3-tuple carry-over, logs count-based diff
- StringTupleComparer class is deleted
- All 3 config files (tenantvector.json, dev ConfigMap, prod ConfigMap) have no Id in tenant entries
- All unit tests pass
- E2e scenario 28 inline JSON has no Id fields, verification uses count-based check
</success_criteria>

<output>
After completion, create `.planning/quick/043-remove-tenant-id-from-configmap/043-SUMMARY.md`
</output>
