---
phase: quick
plan: 042
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/MetricSlotOptions.cs
  - src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
  - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
  - src/SnmpCollector/config/tenantvector.json
  - deploy/k8s/production/configmap.yaml
  - deploy/k8s/snmp-collector/simetra-tenantvector.yaml
  - tests/e2e/scenarios/28-tenantvector-routing.sh
  - tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
  - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
  - tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs
autonomous: true

must_haves:
  truths:
    - "IntervalSeconds no longer appears in any tenant vector config (JSON, YAML, e2e inline)"
    - "TenantVectorRegistry derives IntervalSeconds from DeviceRegistry + OidMapService at Reload time"
    - "Validator no longer rejects config missing IntervalSeconds"
    - "All unit tests pass with IntervalSeconds removed from config model"
    - "Solution builds clean with no warnings"
  artifacts:
    - path: "src/SnmpCollector/Configuration/MetricSlotOptions.cs"
      provides: "Config model without IntervalSeconds property"
    - path: "src/SnmpCollector/Pipeline/TenantVectorRegistry.cs"
      provides: "DeriveIntervalSeconds helper using IDeviceRegistry + IOidMapService"
      contains: "DeriveIntervalSeconds"
  key_links:
    - from: "src/SnmpCollector/Pipeline/TenantVectorRegistry.cs"
      to: "IDeviceRegistry.TryGetByIpPort"
      via: "constructor injection + DeriveIntervalSeconds"
      pattern: "_deviceRegistry\\.TryGetByIpPort"
    - from: "src/SnmpCollector/Pipeline/TenantVectorRegistry.cs"
      to: "IOidMapService.Resolve"
      via: "constructor injection + DeriveIntervalSeconds"
      pattern: "_oidMapService\\.Resolve"
---

<objective>
Remove IntervalSeconds from tenant vector configuration (MetricSlotOptions) and derive it
at TenantVectorRegistry.Reload() time from DeviceRegistry poll groups via OidMapService OID
resolution. Operators no longer specify IntervalSeconds in tenantvector config -- the system
derives it from the device poll definitions already present in DeviceRegistry.

Purpose: Eliminate redundant config that duplicates information already in device definitions,
reducing operator error surface and simplifying tenant vector configuration.

Output: Clean build, all tests pass, IntervalSeconds removed from all config/test data,
TenantVectorRegistry derives it automatically.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Configuration/MetricSlotOptions.cs
@src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
@src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
@src/SnmpCollector/Pipeline/MetricSlotHolder.cs
@src/SnmpCollector/Pipeline/IOidMapService.cs
@src/SnmpCollector/Pipeline/IDeviceRegistry.cs
@tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
@tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
@tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
@tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Remove IntervalSeconds from config model, validator, and all config files</name>
  <files>
    src/SnmpCollector/Configuration/MetricSlotOptions.cs
    src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
    src/SnmpCollector/config/tenantvector.json
    deploy/k8s/production/configmap.yaml
    deploy/k8s/snmp-collector/simetra-tenantvector.yaml
    tests/e2e/scenarios/28-tenantvector-routing.sh
  </files>
  <action>
    1. **MetricSlotOptions.cs** -- Remove the `IntervalSeconds` property entirely (line 29) and its XML doc comment (lines 26-28). The class should only have Ip, Port, MetricName.

    2. **TenantVectorOptionsValidator.cs** -- Remove Rule 7 (lines 103-107) that validates `IntervalSeconds > 0`. Update the comment on Rule 8 from "Rule 8" to "Rule 7" for consistency. Also update the doc comment on line 428 of the production configmap.yaml (step 5 below).

    3. **tenantvector.json** (local dev config) -- Remove `"IntervalSeconds": 10` and `"IntervalSeconds": 30` from each metric entry. The entries should only have Ip, Port, MetricName. Result format per metric: `{ "Ip": "127.0.0.1", "Port": 10161, "MetricName": "obp_link_state_L1" }`.

    4. **deploy/k8s/production/configmap.yaml** -- In the simetra-tenantvector section (starts at line 406), remove `"IntervalSeconds": 10` from every metric entry in the inline JSON. Also update the comment block (line 428) to remove the line mentioning IntervalSeconds.

    5. **deploy/k8s/snmp-collector/simetra-tenantvector.yaml** -- Remove `"IntervalSeconds": 10` from every metric entry.

    6. **tests/e2e/scenarios/28-tenantvector-routing.sh** -- Remove `"IntervalSeconds": 10` from all inline JSON metric entries (lines 134-165 in the heredoc for the 4-tenant hot-reload test). There are about 14 occurrences across the inline YAML/JSON.
  </action>
  <verify>
    Run `grep -r "IntervalSeconds" src/SnmpCollector/Configuration/ deploy/k8s/ src/SnmpCollector/config/tenantvector.json tests/e2e/scenarios/28-tenantvector-routing.sh` and confirm zero matches.
  </verify>
  <done>IntervalSeconds removed from MetricSlotOptions model, validator Rule 7, and all config/deployment/e2e files.</done>
</task>

<task type="auto">
  <name>Task 2: Wire TenantVectorRegistry to derive IntervalSeconds and update all tests</name>
  <files>
    src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
    tests/SnmpCollector.Tests/Configuration/TenantVectorOptionsValidatorTests.cs
    tests/SnmpCollector.Tests/Pipeline/Behaviors/TenantVectorFanOutBehaviorTests.cs
    tests/SnmpCollector.Tests/Pipeline/MetricSlotHolderTests.cs
  </files>
  <action>
    **TenantVectorRegistry.cs changes:**

    1. Add `using SnmpCollector.Pipeline;` if not already present (for IDeviceRegistry, IOidMapService).

    2. Add two readonly fields:
       ```csharp
       private readonly IDeviceRegistry _deviceRegistry;
       private readonly IOidMapService _oidMapService;
       ```

    3. Update constructor to accept the new dependencies:
       ```csharp
       public TenantVectorRegistry(
           IDeviceRegistry deviceRegistry,
           IOidMapService oidMapService,
           ILogger<TenantVectorRegistry> logger)
       {
           _deviceRegistry = deviceRegistry;
           _oidMapService = oidMapService;
           _logger = logger;
       }
       ```

    4. In Reload() method, change lines 100-104 from:
       ```csharp
       var newHolder = new MetricSlotHolder(
           metric.Ip, metric.Port, metric.MetricName, metric.IntervalSeconds);
       ```
       to:
       ```csharp
       var derivedInterval = DeriveIntervalSeconds(metric.Ip, metric.Port, metric.MetricName);
       var newHolder = new MetricSlotHolder(
           metric.Ip, metric.Port, metric.MetricName, derivedInterval);
       ```

    5. Add private helper method (place after Reload, before StringTupleComparer):
       ```csharp
       private int DeriveIntervalSeconds(string ip, int port, string metricName)
       {
           if (!_deviceRegistry.TryGetByIpPort(ip, port, out var device))
               return 0;

           foreach (var pollGroup in device.PollGroups)
           {
               foreach (var oid in pollGroup.Oids)
               {
                   if (string.Equals(_oidMapService.Resolve(oid), metricName, StringComparison.OrdinalIgnoreCase))
                       return pollGroup.IntervalSeconds;
               }
           }
           return 0;
       }
       ```

    **TenantVectorRegistryTests.cs changes:**

    1. Add NSubstitute using: `using NSubstitute;`

    2. Update `CreateRegistry()` helper to inject mock dependencies:
       ```csharp
       private static TenantVectorRegistry CreateRegistry()
       {
           var deviceRegistry = Substitute.For<IDeviceRegistry>();
           var oidMapService = Substitute.For<IOidMapService>();
           return new TenantVectorRegistry(deviceRegistry, oidMapService,
               NullLogger<TenantVectorRegistry>.Instance);
       }
       ```
       Note: The mocks return default (false/null) which means DeriveIntervalSeconds returns 0 for all calls. This is fine -- existing tests do not care about IntervalSeconds values except lines 112/116 in `Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots`.

    3. Update `CreateOptions` parameter tuple to remove the `interval` field. Change signature from:
       ```csharp
       params (string tenantId, int priority, string ip, int port, string metricName, int interval)[] metrics
       ```
       to:
       ```csharp
       params (string tenantId, int priority, string ip, int port, string metricName)[] metrics
       ```
       Remove `IntervalSeconds = interval` from the MetricSlotOptions construction inside. Remove all 6th tuple element from every call site (remove the `, 30`, `, 60` etc. from every CreateOptions call throughout the file).

    4. In test `Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots` (lines 108-116): Remove the two `Assert.Equal` lines that check IntervalSeconds (lines 112 and 116), since with mocked dependencies the derived value will be 0 and checking a specific config-driven value is no longer meaningful.

    5. In `Reload_LogsDiffInformation` test (line 351-352): Update the `new TenantVectorRegistry(logger)` call to also pass mock IDeviceRegistry and IOidMapService:
       ```csharp
       var registry = new TenantVectorRegistry(
           Substitute.For<IDeviceRegistry>(),
           Substitute.For<IOidMapService>(),
           logger);
       ```

    **TenantVectorOptionsValidatorTests.cs changes:**

    1. Remove `IntervalSeconds = 10` from ValidOptions() (line 46).
    2. Remove `IntervalSeconds = 30` from Validate_MultipleTenants_ReturnsSuccess (line 77).
    3. Remove `IntervalSeconds = 10` from Validate_CrossTenantOverlap_ReturnsSuccess (lines 113, 137).
    4. Remove `IntervalSeconds = 15` from Validate_DuplicateMetricWithinTenant_Fails (line 323).
    5. Remove `IntervalSeconds = 0` from Validate_MultipleErrorsCollected_ReportsAll (line 348) and update the assertion from `>= 3` to `>= 2` since one fewer error (was 5 potential errors: empty ID, empty IP, port 0, empty metric name, interval 0 -- now 4).
    6. Delete test methods `Validate_IntervalSecondsZero_Fails` (lines 292-300) and `Validate_IntervalSecondsNegative_Fails` (lines 302-311) entirely.

    **TenantVectorFanOutBehaviorTests.cs changes:**

    1. In all `CreateRegistryWith*` helper methods (CreateRegistryWithRoute, CreateRegistryWithTwoTenants, CreateRegistryWithThreeTenants), the `new TenantVectorRegistry(NullLogger...)` calls need IDeviceRegistry and IOidMapService. Add `using NSubstitute;` and change each to:
       ```csharp
       var registry = new TenantVectorRegistry(
           Substitute.For<IDeviceRegistry>(),
           Substitute.For<IOidMapService>(),
           NullLogger<TenantVectorRegistry>.Instance);
       ```
    2. Remove `IntervalSeconds = 30` from every `new MetricSlotOptions { ... }` in these helpers (appears ~6 times).
    3. In test `SkipsWhenNoMatchingRoute` (line 203), update the direct `new TenantVectorRegistry(NullLogger...)` call similarly.

    **MetricSlotHolderTests.cs changes:**

    1. MetricSlotHolder constructor signature is UNCHANGED (still takes intervalSeconds param). The `CreateHolder` helper and `Constructor_SetsMetadataProperties` test remain as-is. No changes needed to this file.

    Actually -- confirm MetricSlotHolder.cs itself needs NO changes. It keeps IntervalSeconds as a property populated by TenantVectorRegistry, not config.
  </action>
  <verify>
    Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- must succeed with no errors.
    Run `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests must pass.
    Run `grep -rn "IntervalSeconds" tests/` and confirm only MetricSlotHolderTests.cs references remain (those are testing the runtime property, not config).
  </verify>
  <done>
    TenantVectorRegistry injects IDeviceRegistry + IOidMapService and derives IntervalSeconds via DeriveIntervalSeconds helper. All unit tests pass. No config file references IntervalSeconds. MetricSlotHolder retains IntervalSeconds as a system-populated runtime property.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- clean build, no errors or warnings
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests pass
3. `grep -rn "IntervalSeconds" src/SnmpCollector/Configuration/` -- zero matches
4. `grep -rn "IntervalSeconds" deploy/` -- zero matches
5. `grep -rn "IntervalSeconds" src/SnmpCollector/config/tenantvector.json` -- zero matches
6. `grep -rn "IntervalSeconds" tests/e2e/` -- zero matches
7. `grep -rn "IntervalSeconds" src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` -- matches only DeriveIntervalSeconds method
8. `grep -rn "IntervalSeconds" src/SnmpCollector/Pipeline/MetricSlotHolder.cs` -- property still present (runtime, not config)
</verification>

<success_criteria>
- IntervalSeconds removed from MetricSlotOptions config model
- Validator no longer validates IntervalSeconds
- TenantVectorRegistry.Reload() derives IntervalSeconds from DeviceRegistry + OidMapService
- All JSON/YAML config files have IntervalSeconds removed
- All e2e inline JSON has IntervalSeconds removed
- Solution builds clean, all unit tests pass
- MetricSlotHolder.IntervalSeconds property retained as system-populated runtime value
</success_criteria>

<output>
After completion, create `.planning/quick/042-remove-intervalseconds-from-tenant-config/042-SUMMARY.md`
</output>
