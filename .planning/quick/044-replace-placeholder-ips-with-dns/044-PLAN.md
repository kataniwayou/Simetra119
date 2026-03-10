---
phase: Q044
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
  - deploy/k8s/snmp-collector/simetra-tenantvector.yaml
  - deploy/k8s/production/configmap.yaml
  - tests/e2e/scenarios/28-tenantvector-routing.sh
  - tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
  - .planning/STATE.md
autonomous: true

must_haves:
  truths:
    - "Tenant vector ConfigMaps use DNS names, not PLACEHOLDER tokens"
    - "TenantVectorRegistry resolves DNS names to IPs via DeviceRegistry at Reload() time"
    - "TryRoute still matches on resolved IPs (routing unchanged)"
    - "E2e scenario 28 applies ConfigMap directly without sed substitution"
    - "Existing tests still pass; new test proves DNS-to-IP resolution"
  artifacts:
    - path: "src/SnmpCollector/Pipeline/TenantVectorRegistry.cs"
      provides: "ResolveIp() helper using IDeviceRegistry.AllDevices"
      contains: "ResolveIp"
    - path: "deploy/k8s/snmp-collector/simetra-tenantvector.yaml"
      provides: "Dev ConfigMap with DNS names"
      contains: "npb-simulator.simetra.svc.cluster.local"
    - path: "deploy/k8s/production/configmap.yaml"
      provides: "Production ConfigMap with DNS names"
      contains: "npb-simulator.simetra.svc.cluster.local"
    - path: "tests/e2e/scenarios/28-tenantvector-routing.sh"
      provides: "E2e scenario without ClusterIP derivation"
  key_links:
    - from: "TenantVectorRegistry.Reload()"
      to: "IDeviceRegistry.AllDevices"
      via: "ResolveIp() iterates AllDevices matching ConfigAddress"
      pattern: "device\\.ConfigAddress.*configIp"
    - from: "MetricSlotHolder constructor"
      to: "ResolveIp() output"
      via: "resolvedIp passed as first argument"
      pattern: "new MetricSlotHolder\\(resolvedIp"
---

<objective>
Replace PLACEHOLDER_NPB_IP / PLACEHOLDER_OBP_IP in tenant vector ConfigMaps with actual DNS names. Add DNS-to-IP resolution in TenantVectorRegistry.Reload() so routing keys use resolved IPs while config uses human-readable DNS names. Remove the fragile sed-substitution pattern from e2e scenario 28.

Purpose: Eliminate deploy-time IP substitution that breaks when ClusterIPs change. Operators use the same DNS names in tenant vector config as in device config.
Output: Working DNS-based tenant vector config with resolution at Reload() time.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
@src/SnmpCollector/Pipeline/IDeviceRegistry.cs
@deploy/k8s/snmp-collector/simetra-tenantvector.yaml
@deploy/k8s/production/configmap.yaml
@tests/e2e/scenarios/28-tenantvector-routing.sh
@tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add ResolveIp() to TenantVectorRegistry and update Reload()</name>
  <files>
    src/SnmpCollector/Pipeline/TenantVectorRegistry.cs
    tests/SnmpCollector.Tests/Pipeline/TenantVectorRegistryTests.cs
  </files>
  <action>
    Add a private helper method `ResolveIp(string configIp)` to TenantVectorRegistry:

    ```csharp
    private string ResolveIp(string configIp)
    {
        foreach (var device in _deviceRegistry.AllDevices)
        {
            if (string.Equals(device.ConfigAddress, configIp, StringComparison.OrdinalIgnoreCase))
                return device.ResolvedIp;
        }
        return configIp;
    }
    ```

    In Reload(), change the per-metric loop body (lines 86-108) so that:
    1. `DeriveIntervalSeconds` still receives the RAW `metric.Ip` (DNS name) — it calls `TryGetByIpPort` which matches on ConfigAddress.
    2. `MetricSlotHolder` receives the RESOLVED IP — call `ResolveIp(metric.Ip)` and pass the result.
    3. Carry-over lookup key uses the resolved IP (since old holders already store resolved IPs).

    Updated loop body:
    ```csharp
    foreach (var metric in tenantOpts.Metrics)
    {
        var resolvedIp = ResolveIp(metric.Ip);
        var derivedInterval = DeriveIntervalSeconds(metric.Ip, metric.Port, metric.MetricName);
        var newHolder = new MetricSlotHolder(
            resolvedIp,
            metric.Port,
            metric.MetricName,
            derivedInterval);

        var lookupKey = new RoutingKey(resolvedIp, metric.Port, metric.MetricName);
        if (oldSlotLookup.TryGetValue(lookupKey, out var oldHolder))
        {
            var existingSlot = oldHolder.ReadSlot();
            if (existingSlot is not null)
            {
                newHolder.WriteValue(existingSlot.Value, existingSlot.StringValue, existingSlot.TypeCode);
                carriedOver++;
            }
        }

        holders.Add(newHolder);
        totalSlots++;
    }
    ```

    Add a log line in ResolveIp when a DNS name is resolved, at Debug level:
    ```csharp
    _logger.LogDebug("Resolved tenant metric IP {ConfigIp} -> {ResolvedIp}", configIp, device.ResolvedIp);
    ```

    For tests: Add one new test `Reload_DnsName_ResolvedViaDeviceRegistry` that:
    - Creates a mock IDeviceRegistry where AllDevices returns one DeviceInfo with ConfigAddress="dns.test.local" and ResolvedIp="10.0.0.99", Port=161
    - TryGetByIpPort returns true for ("dns.test.local", 161) with that same DeviceInfo
    - Reloads with Ip="dns.test.local", Port=161, MetricName="test_metric"
    - Asserts TryRoute("10.0.0.99", 161, "test_metric") returns true (resolved IP used for routing)
    - Asserts TryRoute("dns.test.local", 161, "test_metric") returns false (raw DNS NOT in routing index)

    Existing tests use direct IPs like "10.0.0.1" — these still work because ResolveIp falls back to returning the input when no device matches on ConfigAddress. Verify all existing tests still pass.
  </action>
  <verify>
    Run `dotnet test tests/SnmpCollector.Tests/ --filter TenantVectorRegistry` — all tests pass including new DNS resolution test.
    Run `dotnet build src/SnmpCollector/` — builds without warnings.
  </verify>
  <done>
    ResolveIp() maps DNS config addresses to resolved IPs via DeviceRegistry.AllDevices.
    MetricSlotHolder and routing index use resolved IPs.
    DeriveIntervalSeconds still uses raw config address for DeviceRegistry lookup.
    New test proves DNS -> resolved IP routing. All existing tests pass.
  </done>
</task>

<task type="auto">
  <name>Task 2: Replace placeholders in ConfigMaps and simplify e2e scenario 28</name>
  <files>
    deploy/k8s/snmp-collector/simetra-tenantvector.yaml
    deploy/k8s/production/configmap.yaml
    tests/e2e/scenarios/28-tenantvector-routing.sh
    .planning/STATE.md
  </files>
  <action>
    **simetra-tenantvector.yaml (dev):**
    Replace all `PLACEHOLDER_NPB_IP` with `npb-simulator.simetra.svc.cluster.local`.
    Replace all `PLACEHOLDER_OBP_IP` with `obp-simulator.simetra.svc.cluster.local`.

    **production/configmap.yaml:**
    In the simetra-tenantvector section (lines 406-463), make the same replacements.
    Update the comment block above the tenantvector.json data to remove any mention of PLACEHOLDER substitution. Update the Ip field docs to say: "Ip - Device address (DNS name or IP, must match a device in simetra-devices)".

    **tests/e2e/scenarios/28-tenantvector-routing.sh:**

    Remove lines 9-20 entirely (ClusterIP derivation via kubectl get svc, the empty-check + record_fail block).
    Remove line 22 (the NPB/OBP ClusterIP log line).

    Change lines 30-35 from the sed-piped apply to a direct `kubectl apply -f "$TENANTVECTOR_YAML"`.

    In the hot-reload heredoc (sub-scenario 28d, lines 120-166):
    Replace all `${NPB_IP}` with `npb-simulator.simetra.svc.cluster.local`.
    Replace all `${OBP_IP}` with `obp-simulator.simetra.svc.cluster.local`.

    In the cleanup section (lines 194-205):
    Simplify the fallback restore: instead of sed-substituting placeholders, just `kubectl apply -f "$TENANTVECTOR_YAML"` directly (since the file now has real DNS names, not placeholders).

    Update the script header comment (line 2) to remove mention of "real ClusterIPs".

    **STATE.md:**
    In the "Key Architectural Facts" section, find and replace the line about PLACEHOLDER substitution:
    - Remove: "simetra-tenantvector ConfigMap committed with PLACEHOLDER_NPB_IP / PLACEHOLDER_OBP_IP -- e2e script substitutes real ClusterIPs at deploy-time via kubectl get svc (D29-01)"
    - Add: "simetra-tenantvector ConfigMap uses DNS names (same as simetra-devices); TenantVectorRegistry.ResolveIp() maps ConfigAddress to ResolvedIp via IDeviceRegistry at Reload() time (Q044)"
  </action>
  <verify>
    Confirm no PLACEHOLDER_NPB_IP or PLACEHOLDER_OBP_IP remain anywhere:
    `grep -r "PLACEHOLDER_NPB_IP\|PLACEHOLDER_OBP_IP" deploy/ tests/` returns no results.

    Confirm DNS names are present:
    `grep "npb-simulator.simetra.svc.cluster.local" deploy/k8s/snmp-collector/simetra-tenantvector.yaml` shows hits.
    `grep "npb-simulator.simetra.svc.cluster.local" deploy/k8s/production/configmap.yaml` shows hits.

    Confirm scenario 28 has no kubectl get svc for ClusterIP derivation:
    `grep "kubectl get svc.*clusterIP" tests/e2e/scenarios/28-tenantvector-routing.sh` returns no results.

    `dotnet build src/SnmpCollector/` still builds.
  </verify>
  <done>
    All ConfigMaps use DNS names instead of PLACEHOLDER tokens.
    E2e scenario 28 applies ConfigMap directly without sed substitution.
    STATE.md updated to reflect the new pattern.
    No PLACEHOLDER references remain in the codebase.
  </done>
</task>

</tasks>

<verification>
- `dotnet test tests/SnmpCollector.Tests/ --filter TenantVectorRegistry` — all pass
- `dotnet build src/SnmpCollector/` — clean build
- `grep -r "PLACEHOLDER" deploy/ tests/` — no results
- DNS names present in both dev and production ConfigMaps
- Scenario 28 has no ClusterIP derivation or sed substitution
</verification>

<success_criteria>
- TenantVectorRegistry.ResolveIp() maps DNS config addresses to resolved IPs using DeviceRegistry.AllDevices
- MetricSlotHolder stores resolved IPs for routing; DeriveIntervalSeconds uses raw config address
- All ConfigMaps use DNS names (npb-simulator.simetra.svc.cluster.local, obp-simulator.simetra.svc.cluster.local)
- E2e scenario 28 works without ClusterIP derivation or sed substitution
- All unit tests pass including new DNS resolution test
- STATE.md reflects the new architectural pattern
</success_criteria>

<output>
After completion, create `.planning/quick/044-replace-placeholder-ips-with-dns/044-SUMMARY.md`
</output>
