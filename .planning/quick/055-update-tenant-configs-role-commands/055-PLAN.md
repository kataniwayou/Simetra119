---
phase: quick
plan: 055
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/config/tenants.json
  - deploy/k8s/snmp-collector/simetra-tenants.yaml
  - deploy/k8s/production/configmap.yaml
  - tests/e2e/scenarios/28-tenantvector-routing.sh
autonomous: true

must_haves:
  truths:
    - "Every metric entry has a Role field set to Evaluate or Resolved"
    - "Every tenant has at least one Resolved metric and one Evaluate metric"
    - "Every tenant has a Commands array with at least one command entry"
    - "Command entries have shape { Ip, Port, CommandName, Value, ValueType }"
    - "Local dev tenants.json keeps { Tenants: { Tenants: [...] } } wrapper"
    - "K8s ConfigMap keeps bare { Tenants: [...] } format"
  artifacts:
    - path: "src/SnmpCollector/config/tenants.json"
      provides: "Local dev tenant config with Role and Commands"
      contains: "Role"
    - path: "deploy/k8s/snmp-collector/simetra-tenants.yaml"
      provides: "K8s standalone tenant config with Role and Commands"
      contains: "Commands"
    - path: "deploy/k8s/production/configmap.yaml"
      provides: "Production tenant config with Role and Commands"
      contains: "Role"
    - path: "tests/e2e/scenarios/28-tenantvector-routing.sh"
      provides: "E2E test heredocs with Role and Commands"
      contains: "Role"
---

<objective>
Add Role ("Evaluate" or "Resolved") to every metric entry and Commands[] to every tenant across all four tenant config files — satisfying TEN-12 and TEN-13 validation rules.

Purpose: The TenantVectorWatcherService validation (TEN-12, TEN-13) now requires every metric to declare its Role and every tenant to have at least one command. Current config files lack both fields, so they will fail validation on next reload.
Output: All four tenant config locations updated with Role on every metric and Commands on every tenant.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@src/SnmpCollector/config/tenants.json
@src/SnmpCollector/config/oid_command_map.json
@deploy/k8s/snmp-collector/simetra-tenants.yaml
@deploy/k8s/production/configmap.yaml
@tests/e2e/scenarios/28-tenantvector-routing.sh
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add Role and Commands to all tenant config files</name>
  <files>
    src/SnmpCollector/config/tenants.json
    deploy/k8s/snmp-collector/simetra-tenants.yaml
    deploy/k8s/production/configmap.yaml
    tests/e2e/scenarios/28-tenantvector-routing.sh
  </files>
  <action>
Apply the same logical changes to all four files. Each file has a different wrapper format — preserve each file's existing structure exactly.

**Role assignment rules (apply consistently across all files):**
- Status/state metrics -> "Resolved": obp_link_state_*, obp_channel_*, npb_port_status_*
- Performance/utilization/traffic metrics -> "Evaluate": npb_cpu_util, npb_mem_util, npb_sys_temp, npb_port_rx_*, npb_port_tx_*, obp_r*_power_*

Every metric entry gets `"Role": "Evaluate"` or `"Role": "Resolved"` added as a field. Verify each tenant ends up with at least one of each Role.

**Commands rules (apply to every tenant):**
- Use command names from oid_command_map.json: obp_set_bypass_L1-L4 (for OBP device tenants) and npb_reset_counters_P1-P8 (for NPB device tenants)
- Match Ip and Port to the tenant's existing metric Ip/Port
- Command entry shape: `{ "Ip": "<same as metrics>", "Port": <same as metrics>, "CommandName": "<from command map>", "Value": "1", "ValueType": "Integer32" }`
- Each tenant needs at least one command

**File 1: src/SnmpCollector/config/tenants.json** (local dev, IConfiguration wrapper)
- Tenant 1 (Priority 1): metric obp_link_state_L1 at 127.0.0.1:10161 -> Role="Resolved". Add command obp_set_bypass_L1 at same Ip/Port.
  - This tenant has only one metric, so it needs both roles. Add a second metric: `{ "Ip": "127.0.0.1", "Port": 10161, "MetricName": "obp_r1_power_L1", "Role": "Evaluate" }` to satisfy TEN-13.
- Tenant 2 (Priority 2): metric npb_cpu_util at 127.0.0.1:10162 -> Role="Evaluate". Add command npb_reset_counters_P1 at same Ip/Port.
  - This tenant has only one metric (Evaluate). Add: `{ "Ip": "127.0.0.1", "Port": 10162, "MetricName": "npb_port_status_P1", "Role": "Resolved" }` to satisfy TEN-13.
- Preserve `{ "Tenants": { "Tenants": [...] } }` wrapper. Preserve existing TimeSeriesSize on npb_cpu_util.

**File 2: deploy/k8s/snmp-collector/simetra-tenants.yaml** (K8s standalone ConfigMap)
- Tenant 1 (Priority 1, NPB): npb_port_status_P1 -> Resolved, npb_port_rx_octets_P1 -> Evaluate, npb_port_tx_octets_P1 -> Evaluate, npb_cpu_util -> Evaluate. Add command npb_reset_counters_P1. Ip: npb-simulator.simetra.svc.cluster.local, Port: 161.
- Tenant 2 (Priority 2, NPB): npb_mem_util -> Evaluate, npb_sys_temp -> Evaluate, npb_port_rx_packets_P1 -> Evaluate, npb_port_tx_packets_P1 -> Evaluate. Needs a Resolved metric — add `{ "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_port_status_P2", "Role": "Resolved" }`. Add command npb_reset_counters_P2.
- Tenant 3 (Priority 3, OBP): obp_channel_L1 -> Resolved, obp_r1_power_L1 -> Evaluate, obp_r2_power_L1 -> Evaluate, obp_channel_L2 -> Resolved. Add command obp_set_bypass_L1. Ip: obp-simulator.simetra.svc.cluster.local, Port: 161. Preserve existing TimeSeriesSize on obp_r1_power_L1.
- Preserve bare `{ "Tenants": [...] }` format.

**File 3: deploy/k8s/production/configmap.yaml** (production, embedded in multi-doc YAML)
- Only modify the simetra-tenants ConfigMap section (starts at the `tenants.json: |` block under `name: simetra-tenants`). Do NOT touch the other ConfigMaps (snmp-collector-config, simetra-oid-metric-map, simetra-devices, simetra-oid-command-map).
- Apply same Role assignments as File 2 (identical tenant structure). Add same Commands.
- Preserve all YAML comments in the simetra-tenants section.

**File 4: tests/e2e/scenarios/28-tenantvector-routing.sh** (inline heredocs)
- Two heredocs: the initial 3-tenant ConfigMap (around line 101-147) and the hot-reload 4-tenant ConfigMap.
- Initial 3-tenant: apply same Roles and Commands as File 2/3.
- Hot-reload 4-tenant: same first 3 tenants, plus Tenant 4 (Priority 4, OBP): obp_r3_power_L1 -> Evaluate, obp_r4_power_L1 -> Evaluate. Needs a Resolved metric — add `{ "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_link_state_L1", "Role": "Resolved" }`. Add command obp_set_bypass_L3.
- The hot-reload test checks for `tenants=4` in logs. Adding metrics/commands to existing tenants does not change tenant count, so the test assertion remains valid. Tenant 4 going from 2 metrics to 3 changes slot count but not tenant count — no test fixup needed.
  </action>
  <verify>
1. Parse each JSON block to verify valid JSON (use `python3 -c "import json; json.load(open('...'))"` for tenants.json; visually verify YAML-embedded JSON is well-formed).
2. Grep all four files for `"Role"` — every metric line must have it.
3. Grep all four files for `"Commands"` — every tenant must have it.
4. Verify TEN-13: each tenant has at least one `"Resolved"` metric, at least one `"Evaluate"` metric, and at least one command entry.
5. `dotnet build src/SnmpCollector/SnmpCollector.csproj` — must compile (config is runtime-loaded, but ensures no accidental file corruption).
  </verify>
  <done>All four tenant config files have Role on every metric entry and Commands on every tenant. TEN-12 (Role required) and TEN-13 (both Roles + commands per tenant) satisfied. JSON valid in all locations. Existing TimeSeriesSize values preserved.</done>
</task>

</tasks>

<verification>
1. `python3 -c "import json; json.load(open('src/SnmpCollector/config/tenants.json'))"` succeeds
2. Every metric entry in all four files has `"Role": "Evaluate"` or `"Role": "Resolved"`
3. Every tenant in all four files has a `"Commands": [...]` array with at least one entry
4. Every command entry has all five fields: Ip, Port, CommandName, Value, ValueType
5. Each tenant has at least one Resolved and one Evaluate metric (TEN-13)
6. Local dev tenants.json preserves `{ "Tenants": { "Tenants": [...] } }` wrapper
7. K8s ConfigMaps preserve bare `{ "Tenants": [...] }` format
8. `dotnet build src/SnmpCollector/SnmpCollector.csproj` succeeds
</verification>

<success_criteria>
- All four tenant config files pass TEN-12 validation (Role on every metric)
- All four tenant config files pass TEN-13 validation (both Roles + commands per tenant)
- Command entries use valid CommandName values from oid_command_map.json
- No other config sections or files are modified
- JSON is valid and parseable in all locations
</success_criteria>

<output>
After completion, create `.planning/quick/055-update-tenant-configs-role-commands/055-SUMMARY.md`
</output>
