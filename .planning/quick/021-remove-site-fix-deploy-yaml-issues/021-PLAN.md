---
phase: quick-021
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  # Code rename
  - src/SnmpCollector/Configuration/SiteOptions.cs
  - src/SnmpCollector/Configuration/PodIdentityOptions.cs
  - src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs
  - src/SnmpCollector/Configuration/Validators/PodIdentityOptionsValidator.cs
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  - src/SnmpCollector/Telemetry/K8sLeaseElection.cs
  - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Behaviors/ExceptionBehaviorTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Behaviors/LoggingBehaviorTests.cs
  # Deploy YAML
  - deploy/k8s/snmp-collector/configmap.yaml
  - deploy/k8s/snmp-collector/service.yaml
  - deploy/k8s/production/configmap.yaml
  # Deletions
  - deploy/k8s/production/namespace.yaml
  - deploy/k8s/production/rbac.yaml
  - deploy/k8s/production/serviceaccount.yaml
  - deploy/grafana/provisioning/datasources/prometheus.yaml
autonomous: true

must_haves:
  truths:
    - "SiteOptions class no longer exists; replaced by PodIdentityOptions with only PodIdentity property"
    - "All C# code compiles and tests pass after rename"
    - "Production ConfigMap name is snmp-collector-config (matches deployment projected volumes)"
    - "Production ConfigMap has correct MetricPollOptions schema (Oids string array + IntervalSeconds)"
    - "Production ConfigMap has Lease section"
    - "Dev service exposes UDP port 10162 for SNMP traps"
    - "No duplicate deploy files remain"
  artifacts:
    - path: "src/SnmpCollector/Configuration/PodIdentityOptions.cs"
      provides: "Renamed options class with PodIdentity only"
      contains: "class PodIdentityOptions"
    - path: "src/SnmpCollector/Configuration/Validators/PodIdentityOptionsValidator.cs"
      provides: "Renamed validator"
      contains: "class PodIdentityOptionsValidator"
  key_links:
    - from: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      to: "PodIdentityOptions"
      via: "DI binding from PodIdentity config section"
      pattern: "PodIdentityOptions\\.SectionName"
    - from: "src/SnmpCollector/Telemetry/K8sLeaseElection.cs"
      to: "PodIdentityOptions"
      via: "IOptions constructor injection"
      pattern: "IOptions<PodIdentityOptions>"
---

<objective>
Rename SiteOptions to PodIdentityOptions (removing the unused Site.Name property) and fix all deploy YAML issues found in audit.

Purpose: Clean up misleading naming and fix production ConfigMap schema mismatches that would cause runtime failures.
Output: Renamed C# class + corrected deploy manifests + deleted duplicate files.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/ROADMAP.md
@src/SnmpCollector/Configuration/SiteOptions.cs
@src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
@src/SnmpCollector/Telemetry/K8sLeaseElection.cs
@src/SnmpCollector/Configuration/MetricPollOptions.cs
@src/SnmpCollector/Configuration/DeviceOptions.cs
@deploy/k8s/snmp-collector/configmap.yaml
@deploy/k8s/production/configmap.yaml
@deploy/k8s/snmp-collector/service.yaml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Rename SiteOptions to PodIdentityOptions across all C# code</name>
  <files>
    src/SnmpCollector/Configuration/PodIdentityOptions.cs
    src/SnmpCollector/Configuration/SiteOptions.cs
    src/SnmpCollector/Configuration/Validators/PodIdentityOptionsValidator.cs
    src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs
    src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    src/SnmpCollector/Telemetry/K8sLeaseElection.cs
    tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
    tests/SnmpCollector.Tests/Pipeline/Behaviors/ExceptionBehaviorTests.cs
    tests/SnmpCollector.Tests/Pipeline/Behaviors/LoggingBehaviorTests.cs
  </files>
  <action>
    1. Create `src/SnmpCollector/Configuration/PodIdentityOptions.cs`:
       - Class name: `PodIdentityOptions`
       - `SectionName = "PodIdentity"`
       - ONLY property: `public string? PodIdentity { get; set; }` (remove `Name` property entirely)
       - Keep the XML doc comment about pod identity defaulting to HOSTNAME env var

    2. Create `src/SnmpCollector/Configuration/Validators/PodIdentityOptionsValidator.cs`:
       - Class name: `PodIdentityOptionsValidator`
       - Implements `IValidateOptions<PodIdentityOptions>`
       - Same body as current validator (returns Success -- no validation needed)

    3. Delete `src/SnmpCollector/Configuration/SiteOptions.cs` (use `git rm`)

    4. Delete `src/SnmpCollector/Configuration/Validators/SiteOptionsValidator.cs` (use `git rm`)

    5. Update `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs`:
       - Replace ALL `SiteOptions` references with `PodIdentityOptions`
       - Replace `SiteOptionsValidator` with `PodIdentityOptionsValidator`
       - The `.Bind(configuration.GetSection(SiteOptions.SectionName))` becomes `.Bind(configuration.GetSection(PodIdentityOptions.SectionName))`
       - The `PostConfigure<SiteOptions>` becomes `PostConfigure<PodIdentityOptions>`
       - Update XML doc comments: replace "SiteOptions" with "PodIdentityOptions" in the Phase 1 options list

    6. Update `src/SnmpCollector/Telemetry/K8sLeaseElection.cs`:
       - Replace `SiteOptions` with `PodIdentityOptions` in field type, constructor parameter, XML doc
       - Field `_siteOptions` rename to `_podIdentityOptions`
       - Usage: `_podIdentityOptions.PodIdentity` (same property name, just different field)

    7. Update test files -- in ALL THREE test files, replace:
       - `new SiteOptions { Name = "test-site" }` with `new PodIdentityOptions { PodIdentity = "test-pod" }`
       - Add/update the using statement if namespace differs (it doesn't -- same namespace)
       - Note: tests were using `SiteOptions.Name` which is being removed. The tests only need it for DI registration -- the actual value is used by SnmpConsoleFormatter via IServiceProvider. Using PodIdentity with a test value is correct.
  </action>
  <verify>
    Run `dotnet build src/SnmpCollector/SnmpCollector.sln` -- zero errors.
    Run `dotnet test tests/SnmpCollector.Tests/` -- all tests pass.
    Grep for "SiteOptions" in src/ and tests/ -- zero matches (confirm complete rename).
  </verify>
  <done>SiteOptions fully replaced by PodIdentityOptions. Name property removed. All code compiles and tests pass.</done>
</task>

<task type="auto">
  <name>Task 2: Fix dev ConfigMap and service YAML</name>
  <files>
    deploy/k8s/snmp-collector/configmap.yaml
    deploy/k8s/snmp-collector/service.yaml
  </files>
  <action>
    1. In `deploy/k8s/snmp-collector/configmap.yaml`:
       - Rename the `"Site"` JSON section to `"PodIdentity"` and remove the `"Name"` key:
         FROM: `"Site": { "Name": "site-lab-k8s" }`
         TO: `"PodIdentity": {}`
         (PodIdentity value is auto-populated via PostConfigure from HOSTNAME env var)
       - Remove the `"OidMap": {}` line (OidMapWatcherService loads from simetra-oidmaps ConfigMap)
       - Remove the `"Devices": []` line (DeviceWatcherService loads from simetra-devices ConfigMap)

    2. In `deploy/k8s/snmp-collector/service.yaml`:
       - Add UDP port 10162 for SNMP trap reception. Add this port entry:
         ```yaml
         - name: snmp-trap
           port: 10162
           protocol: UDP
           targetPort: 10162
         ```
  </action>
  <verify>
    Verify configmap.yaml has no "Site", "OidMap", or "Devices" keys.
    Verify service.yaml has both TCP 8080 (health) and UDP 10162 (snmp-trap) ports.
  </verify>
  <done>Dev ConfigMap uses PodIdentity section, no redundant OidMap/Devices keys. Dev service exposes SNMP trap UDP port.</done>
</task>

<task type="auto">
  <name>Task 3: Fix production ConfigMap</name>
  <files>deploy/k8s/production/configmap.yaml</files>
  <action>
    Fix ALL issues in the production ConfigMap at `deploy/k8s/production/configmap.yaml`:

    1. **Fix ConfigMap name** (HIGH): Change `metadata.name` from `simetra-config` to `snmp-collector-config` (must match what deployment projected volumes reference).

    2. **Rename Site to PodIdentity** in appsettings JSON:
       FROM: `"Site": { "Name": "REPLACE_ME_SITE_NAME" }`
       TO: `"PodIdentity": {}`
       (auto-populated from HOSTNAME env var)

    3. **Remove CommunityString from SnmpListener** (MEDIUM): `CommunityString` belongs on DeviceOptions, not SnmpListenerOptions. Remove `"CommunityString": "REPLACE_ME_COMMUNITY_STRING"` line from SnmpListener section. Keep `Version` -- it IS a valid property on SnmpListenerOptions.

    4. **Fix ServiceName** (MEDIUM): Change `"ServiceName": "simetra-supervisor"` to `"ServiceName": "snmp-collector"`.

    5. **Add Lease section** (MEDIUM): Add after Otlp section:
       ```json
       "Lease": {
         "Name": "snmp-collector-leader",
         "Namespace": "simetra",
         "DurationSeconds": 15,
         "RenewIntervalSeconds": 10
       }
       ```

    6. **Fix simetra-devices schema** (HIGH): The current devices.json in the production template uses fictitious MetricPoll properties (MetricName, MetricType, object Oids with Oid/PropertyName/Role, StaticLabels). Replace with the correct MetricPollOptions schema. Each MetricPoll entry should have:
       - `"Oids": ["string", "string"]` (flat array of OID strings)
       - `"IntervalSeconds": 30`
       No MetricName, MetricType, StaticLabels, or object Oid entries.
       Also add the valid device properties: `Port` (int, default 161) and optional `CommunityString`.

       Example correct device entry:
       ```json
       {
         "Name": "REPLACE_ME_DEVICE_NAME",
         "IpAddress": "REPLACE_ME_DEVICE_IP",
         "Port": 161,
         "MetricPolls": [
           {
             "Oids": ["1.3.6.1.2.1.1.3.0"],
             "IntervalSeconds": 30
           }
         ]
       }
       ```

    7. **Update YAML comments**: Remove references to `Site.Name`, `SnmpListener.CommunityString`. Update `Otlp.ServiceName` default comment to say `"snmp-collector"`. Add brief Lease field documentation. Remove `DeviceType` from device comments (not a valid property).
  </action>
  <verify>
    Verify metadata.name is `snmp-collector-config`.
    Verify JSON has `"PodIdentity": {}` (not Site).
    Verify SnmpListener has only BindAddress, Port, Version (no CommunityString).
    Verify ServiceName is "snmp-collector".
    Verify Lease section exists with all 4 fields.
    Verify devices.json MetricPolls use flat Oids array and IntervalSeconds only.
  </verify>
  <done>Production ConfigMap matches C# schema exactly. Name matches deployment reference. All placeholder comments are accurate.</done>
</task>

<task type="auto">
  <name>Task 4: Delete duplicate deploy files</name>
  <files>
    deploy/k8s/production/namespace.yaml
    deploy/k8s/production/rbac.yaml
    deploy/k8s/production/serviceaccount.yaml
    deploy/grafana/provisioning/datasources/prometheus.yaml
  </files>
  <action>
    Delete these duplicate/redundant files using `git rm`:

    1. `deploy/k8s/production/namespace.yaml` -- identical to `deploy/k8s/namespace.yaml`
    2. `deploy/k8s/production/rbac.yaml` -- identical to base version
    3. `deploy/k8s/production/serviceaccount.yaml` -- identical to `deploy/k8s/serviceaccount.yaml`
    4. `deploy/grafana/provisioning/datasources/prometheus.yaml` -- superseded by `simetra-prometheus.yaml` which has more complete config (httpMethod, prometheusType, timeInterval)

    Use `git rm` for each file so they are staged for the commit.
  </action>
  <verify>
    Verify all 4 files are deleted: `ls` each path should fail.
    Verify `deploy/grafana/provisioning/datasources/simetra-prometheus.yaml` still exists.
    Verify `deploy/k8s/namespace.yaml` and `deploy/k8s/serviceaccount.yaml` still exist (the non-duplicate originals).
  </verify>
  <done>All 4 duplicate files removed. Single source of truth for namespace, RBAC, serviceaccount, and Grafana datasource.</done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.sln` -- zero errors, zero warnings about SiteOptions
2. `dotnet test tests/SnmpCollector.Tests/` -- all tests pass
3. `grep -r "SiteOptions" src/ tests/` -- zero matches
4. `grep -r '"Site"' deploy/` -- zero matches in ConfigMap JSON sections
5. Production configmap.yaml metadata.name == "snmp-collector-config"
6. Dev service.yaml has UDP 10162 port
</verification>

<success_criteria>
- SiteOptions fully renamed to PodIdentityOptions with Name property removed
- All C# code compiles and all tests pass
- Dev ConfigMap: PodIdentity section, no OidMap/Devices stubs
- Dev service: UDP 10162 exposed
- Production ConfigMap: correct name, correct schema, Lease section present, ServiceName fixed
- 4 duplicate files deleted
</success_criteria>

<output>
After completion, create `.planning/quick/021-remove-site-fix-deploy-yaml-issues/021-SUMMARY.md`
</output>
