---
phase: quick/057-demo-aggregate-metrics-in-devices
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/config/devices.json
  - src/SnmpCollector/config/tenants.json
  - deploy/k8s/snmp-collector/simetra-devices.yaml
  - deploy/k8s/snmp-collector/simetra-tenants.yaml
  - deploy/k8s/production/configmap.yaml
autonomous: true

must_haves:
  truths:
    - NPB 10s poll group has AggregatedMetricName "npb_total_rx_octets" with Aggregator "sum" and MetricNames including rx_octets P1-P8
    - NPB 10s poll group has AggregatedMetricName "npb_total_tx_octets" with Aggregator "sum" and MetricNames including tx_octets P1-P8
    - OBP 10s poll group has AggregatedMetricName "obp_mean_power_L1" with Aggregator "mean" and MetricNames including r1-r4 power L1
    - Aggregate metric names appear in tenant Metrics[] arrays so tenant vector routing picks them up
    - No AggregatedMetricName collides with existing OID map MetricName entries
    - All three config locations (local, K8s standalone, production) have identical aggregate definitions
  artifacts:
    - src/SnmpCollector/config/devices.json
    - src/SnmpCollector/config/tenants.json
    - deploy/k8s/snmp-collector/simetra-devices.yaml
    - deploy/k8s/snmp-collector/simetra-tenants.yaml
    - deploy/k8s/production/configmap.yaml
  key_links:
    - AggregatedMetricName values must NOT exist in any oid_metric_map.json (collision = Error + skip)
    - Tenant Metrics[] entries for aggregate names must use the same Ip/Port as the device that owns the poll group
---

<objective>
Add aggregate metric examples to all devices.json and tenants.json config files so the user can see synthetic metrics on dashboards.

Purpose: Demonstrate v1.8 Combined Metrics end-to-end by adding real aggregate config to NPB (sum of rx/tx octets across ports) and OBP (mean of receiver powers on link 1).

Output: Updated devices.json (3 locations) and tenants.json (2 locations) with aggregate config and tenant routing entries.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@src/SnmpCollector/config/devices.json
@src/SnmpCollector/config/tenants.json
@deploy/k8s/snmp-collector/simetra-devices.yaml
@deploy/k8s/snmp-collector/simetra-tenants.yaml
@deploy/k8s/production/configmap.yaml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add aggregate config to devices.json (all 3 locations)</name>
  <files>
    src/SnmpCollector/config/devices.json
    deploy/k8s/snmp-collector/simetra-devices.yaml
    deploy/k8s/production/configmap.yaml
  </files>
  <action>
  In each devices.json, modify the existing poll groups (do NOT create new poll groups):

  **NPB device, 10s poll group** — add two aggregate definitions. The poll group already contains all port metrics. Add these fields to the poll group object:

  For `npb_total_rx_octets` — add a NEW poll group (separate from the existing 10s group) because a poll group can only have one AggregatedMetricName:
  ```json
  {
    "IntervalSeconds": 10,
    "MetricNames": [
      "npb_port_rx_octets_P1", "npb_port_rx_octets_P2",
      "npb_port_rx_octets_P3", "npb_port_rx_octets_P4",
      "npb_port_rx_octets_P5", "npb_port_rx_octets_P6",
      "npb_port_rx_octets_P7", "npb_port_rx_octets_P8"
    ],
    "AggregatedMetricName": "npb_total_rx_octets",
    "Aggregator": "sum"
  }
  ```

  For `npb_total_tx_octets` — add another NEW poll group:
  ```json
  {
    "IntervalSeconds": 10,
    "MetricNames": [
      "npb_port_tx_octets_P1", "npb_port_tx_octets_P2",
      "npb_port_tx_octets_P3", "npb_port_tx_octets_P4",
      "npb_port_tx_octets_P5", "npb_port_tx_octets_P6",
      "npb_port_tx_octets_P7", "npb_port_tx_octets_P8"
    ],
    "AggregatedMetricName": "npb_total_tx_octets",
    "Aggregator": "sum"
  }
  ```

  **OBP device, 10s poll group** — add a NEW poll group for mean power:
  ```json
  {
    "IntervalSeconds": 10,
    "MetricNames": [
      "obp_r1_power_L1", "obp_r2_power_L1",
      "obp_r3_power_L1", "obp_r4_power_L1"
    ],
    "AggregatedMetricName": "obp_mean_power_L1",
    "Aggregator": "mean"
  }
  ```

  Insert these new poll groups AFTER the existing 10s poll group for each device (before the 30s/60s groups). The existing 10s poll groups remain unchanged — the aggregate poll groups are separate entries in the Polls array.

  IMPORTANT: The IpAddress differs across config locations:
  - Local: `127.0.0.1` with ports 10161 (OBP) / 10162 (NPB)
  - K8s standalone + production: DNS names with port 161

  Apply the same aggregate definitions to all 3 files, using the correct IpAddress/Port for each.
  </action>
  <verify>
  Validate JSON syntax in all 3 files:
  - `python -m json.tool src/SnmpCollector/config/devices.json`
  - Visually confirm the K8s YAML files have valid embedded JSON
  - Confirm `npb_total_rx_octets`, `npb_total_tx_octets`, `obp_mean_power_L1` do NOT appear in any oid_metric_map.json (grep the codebase)
  </verify>
  <done>
  All 3 devices.json locations have 3 new aggregate poll groups (2 NPB sum, 1 OBP mean) with correct MetricNames, AggregatedMetricName, and Aggregator fields.
  </done>
</task>

<task type="auto">
  <name>Task 2: Register aggregate metrics in tenants.json (2 locations)</name>
  <files>
    src/SnmpCollector/config/tenants.json
    deploy/k8s/snmp-collector/simetra-tenants.yaml
  </files>
  <action>
  Add aggregate metric entries to tenant Metrics[] arrays so tenant vector routing picks them up.

  **Local tenants.json** (`src/SnmpCollector/config/tenants.json`):
  - In Priority 2 tenant (NPB), add to Metrics[]:
    ```json
    { "Ip": "127.0.0.1", "Port": 10162, "MetricName": "npb_total_rx_octets", "Role": "Evaluate" },
    { "Ip": "127.0.0.1", "Port": 10162, "MetricName": "npb_total_tx_octets", "Role": "Evaluate" }
    ```
  - In Priority 1 tenant (OBP), add to Metrics[]:
    ```json
    { "Ip": "127.0.0.1", "Port": 10161, "MetricName": "obp_mean_power_L1", "Role": "Evaluate" }
    ```

  **K8s standalone tenants.yaml** (`deploy/k8s/snmp-collector/simetra-tenants.yaml`):
  - In Priority 1 tenant (NPB), add to Metrics[]:
    ```json
    { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_total_rx_octets", "Role": "Evaluate" },
    { "Ip": "npb-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "npb_total_tx_octets", "Role": "Evaluate" }
    ```
  - In Priority 3 tenant (OBP), add to Metrics[]:
    ```json
    { "Ip": "obp-simulator.simetra.svc.cluster.local", "Port": 161, "MetricName": "obp_mean_power_L1", "Role": "Evaluate" }
    ```

  Place new entries at the END of each tenant's Metrics[] array.
  </action>
  <verify>
  - `python -m json.tool src/SnmpCollector/config/tenants.json`
  - Visually confirm the K8s YAML tenants file has valid embedded JSON
  - Confirm each aggregate metric name appears in at least one tenant's Metrics[] array
  </verify>
  <done>
  All 3 aggregate metric names (`npb_total_rx_octets`, `npb_total_tx_octets`, `obp_mean_power_L1`) are registered in tenant Metrics[] arrays in both local and K8s standalone config, with correct Ip/Port matching their device.
  </done>
</task>

</tasks>

<verification>
1. JSON validity: `python -m json.tool` passes on both local config files
2. No OID map collision: `grep -r "npb_total_rx_octets\|npb_total_tx_octets\|obp_mean_power_L1" src/SnmpCollector/config/oid_metric_map.json deploy/k8s/` returns only devices.json and tenants.json hits (NOT oid_metric_map)
3. All 3 aggregate names appear in devices.json across all 3 locations
4. All 3 aggregate names appear in tenants.json across both tenant config locations
5. Build passes: `dotnet build src/SnmpCollector/SnmpCollector.csproj`
6. Tests pass: `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj`
</verification>

<success_criteria>
- 3 new aggregate poll groups in devices.json (all 3 config locations)
- 3 new tenant metric entries in tenants.json (both config locations)
- All existing tests still pass
- No OID map name collisions
</success_criteria>

<output>
After completion, create `.planning/quick/057-demo-aggregate-metrics-in-devices/057-SUMMARY.md`
</output>
