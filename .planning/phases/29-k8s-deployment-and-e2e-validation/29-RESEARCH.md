# Phase 29: K8s Deployment and E2E Validation - Research

**Researched:** 2026-03-10
**Domain:** K8s ConfigMap deployment, tenantvector config population, E2E bash scripting
**Confidence:** HIGH

## Summary

Phase 29 deploys the simetra-tenantvector ConfigMap to the cluster, mounts it in deployment.yaml, populates test tenant configuration, and verifies end-to-end that SNMP data flows into tenant slots. The codebase is almost entirely ready — the ConfigMap manifest already exists in production (`deploy/k8s/production/configmap.yaml` lines 261–293), the watcher service is built and wired, and the e2e test harness is well established.

Three concrete tasks remain: (1) add simetra-tenantvector as a 4th projected source in both deployment.yaml files, (2) populate the `simetra-tenantvector` ConfigMap with 3 test tenants using real K8s metrics, and (3) add a new e2e scenario (script `28-*.sh`) that deploys, waits, checks the Prometheus counter, verifies watcher reload logs, tests hot-reload, and reports pass/fail.

**Critical routing constraint discovered:** `MetricSlotOptions.Ip` must be a valid IPv4 address (enforced by `TenantVectorOptionsValidator` via `IPAddress.TryParse`). The routing key uses `msg.AgentIp.ToString()` — the actual IP in `SnmpOidReceived`. For **poll** events this is `device.ResolvedIp` (the K8s Service ClusterIP resolved at startup). For **trap** events this is the sending pod's IP (unstable across restarts). Therefore, only poll-originated metrics can be reliably routed through tenantvector config using static IPs. The "npb-trap" tenant in the CONTEXT.md must use poll metrics, not trap-originated ones.

**Primary recommendation:** Use only poll-sourced metrics in all three test tenants. Determine K8s Service ClusterIPs at plan-time using `kubectl get svc` and embed them in the tenantvector ConfigMap. The e2e script should derive the ClusterIP dynamically at runtime via `kubectl get svc npb-simulator -n simetra -o jsonpath='{.spec.clusterIP}'` so the script doesn't hard-code IPs.

## Standard Stack

### Core

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| `kubectl apply` | K8s 1.x | Deploy/update ConfigMaps and Deployment | Already used throughout the project |
| `kubectl logs` | K8s 1.x | Verify watcher reload messages in pods | Used by existing e2e scenarios 24 and 25 |
| `kubectl describe` | K8s 1.x | Verify volume mounts include simetra-tenantvector | Used in context for mount verification |
| Prometheus HTTP API | 2.x | Verify `snmp_tenantvector_routed_total` counter | Used by all existing e2e scenarios via prometheus.sh |
| bash + jq | existing | E2E scenario scripting | All existing scenarios use this stack |

### Supporting

| Tool | Version | Purpose | When to Use |
|------|---------|---------|-------------|
| `kubectl get svc ... -o jsonpath` | K8s 1.x | Derive ClusterIP at runtime in e2e script | To avoid hard-coding IPs in tenantvector ConfigMap |
| `kubectl rollout restart` | K8s 1.x | Restart deployment after ConfigMap update | Standard restart for config changes |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Static ClusterIP in tenantvector JSON | DNS hostname | Validator rejects DNS names — `IPAddress.TryParse` fails; must use IPs |
| Static ClusterIP in tenantvector JSON | Placeholder with sed replacement | Works for e2e script but less clean than dynamic kubectl lookup |

## Architecture Patterns

### Recommended Project Structure

Files to create or modify:

```
deploy/k8s/snmp-collector/
└── deployment.yaml              # ADD simetra-tenantvector as 4th projected source

deploy/k8s/production/
├── deployment.yaml              # ADD simetra-tenantvector as 4th projected source
└── configmap.yaml               # POPULATE simetra-tenantvector with 3 test tenants

tests/e2e/scenarios/
└── 28-tenantvector-routing.sh   # NEW e2e scenario
```

### Pattern 1: Projected Volume — 4th ConfigMap Source

The current `volumes` section in both deployment.yaml files ends with:

```yaml
      volumes:
      - name: config
        projected:
          sources:
          - configMap:
              name: snmp-collector-config
          - configMap:
              name: simetra-oidmaps
          - configMap:
              name: simetra-devices
```

Add `simetra-tenantvector` as a 4th source:

```yaml
          - configMap:
              name: simetra-tenantvector
```

No other changes to the deployment are required — the `volumeMount` already mounts the entire `/app/config` directory via the `config` projected volume.

### Pattern 2: simetra-tenantvector ConfigMap — 3 Test Tenants

The tenantvector.json must use bare `TenantVectorOptions` format (no section wrapper). All `Ip` values must be valid IPv4 addresses matching the K8s Service ClusterIPs for npb-simulator and obp-simulator.

**Critical constraint:** `Ip` must be the resolved ClusterIP, not a DNS hostname. The validator (`TenantVectorOptionsValidator`) calls `IPAddress.TryParse` and rejects DNS names. The routing key in `TenantVectorFanOutBehavior` uses `msg.AgentIp.ToString()` which is `device.ResolvedIp` (the IP `DeviceRegistry` resolves via `Dns.GetHostAddresses` at startup). For polls, `device.ResolvedIp` = K8s Service ClusterIP.

**Trap routing is not feasible with static config:** For traps, `AgentIp` is the sending pod's IP address (set from `result.RemoteEndPoint.Address.MapToIPv4()` in `SnmpTrapListenerService`). Pod IPs change on restart and are not stable. Therefore the "npb-trap" tenant described in CONTEXT.md must use NPB metrics that arrive via **poll**, not trap.

**Tenant design for 3 tenants (all poll-sourced):**

Tenant 1 — `npb-trap` (Priority 1): Despite the name, use NPB poll metrics. Suggested: `npb_port_status_P1`, `npb_port_rx_octets_P1`, `npb_port_tx_octets_P1`, `npb_cpu_util`. These are polled at 10s intervals from npb-simulator. Source metric evidence will appear quickly.

Tenant 2 — `npb-poll` (Priority 2): Different NPB poll metrics. Suggested: `npb_mem_util`, `npb_sys_temp`, `npb_port_rx_packets_P1`, `npb_port_tx_packets_P1`. Also polled at 10s.

Tenant 3 — `obp-poll` (Priority 3): OBP poll metrics. Suggested: `obp_channel_L1`, `obp_r1_power_L1`, `obp_r2_power_L1`, `obp_channel_L2`. Polled at 10s.

**Format:**

```json
{
  "Tenants": [
    {
      "Id": "npb-trap",
      "Priority": 1,
      "Metrics": [
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_port_status_P1", "IntervalSeconds": 10 },
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_port_rx_octets_P1", "IntervalSeconds": 10 },
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_port_tx_octets_P1", "IntervalSeconds": 10 },
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_cpu_util", "IntervalSeconds": 10 }
      ]
    },
    {
      "Id": "npb-poll",
      "Priority": 2,
      "Metrics": [
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_mem_util", "IntervalSeconds": 10 },
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_sys_temp", "IntervalSeconds": 10 },
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_port_rx_packets_P1", "IntervalSeconds": 10 },
        { "Ip": "<NPB_CLUSTERIP>", "Port": 161, "MetricName": "npb_port_tx_packets_P1", "IntervalSeconds": 10 }
      ]
    },
    {
      "Id": "obp-poll",
      "Priority": 3,
      "Metrics": [
        { "Ip": "<OBP_CLUSTERIP>", "Port": 161, "MetricName": "obp_channel_L1", "IntervalSeconds": 10 },
        { "Ip": "<OBP_CLUSTERIP>", "Port": 161, "MetricName": "obp_r1_power_L1", "IntervalSeconds": 10 },
        { "Ip": "<OBP_CLUSTERIP>", "Port": 161, "MetricName": "obp_r2_power_L1", "IntervalSeconds": 10 },
        { "Ip": "<OBP_CLUSTERIP>", "Port": 161, "MetricName": "obp_channel_L2", "IntervalSeconds": 10 }
      ]
    }
  ]
}
```

The ClusterIP values must be substituted at plan time (or dynamically in the e2e script — see Pattern 4 below).

**Planner note:** The production configmap.yaml (`deploy/k8s/production/configmap.yaml`) already has the simetra-tenantvector ConfigMap at lines 261–293 with `{ "Tenants": [] }`. This must be populated with the 3 test tenants above. The simetra-devices ConfigMap in production (`configmap.yaml` lines 221–259) still has `REPLACE_ME` placeholders. This phase should also replace those with the K8s DNS entries from `deploy/k8s/snmp-collector/simetra-devices.yaml` (which already has correct entries with `npb-simulator.simetra.svc.cluster.local`, `obp-simulator.simetra.svc.cluster.local`, and `e2e-simulator.simetra.svc.cluster.local`).

### Pattern 3: Log Messages to Check in E2E Script

The watcher and registry emit specific log messages that the e2e script must grep for:

| Event | Log message | Source |
|-------|-------------|--------|
| Initial load | `"TenantVectorWatcher initial load complete"` | `TenantVectorWatcherService.cs:73-76` |
| Watch event received | `"TenantVectorWatcher received {EventType} event for {ConfigMap}"` | `TenantVectorWatcherService.cs:107-110` |
| Reload complete | `"Tenant vector reload complete for {ConfigMap}/{Key}"` | `TenantVectorWatcherService.cs:211-214` |
| Registry diff | `"TenantVectorRegistry reloaded: tenants={N}, slots={N}, added=[...], removed=[...], unchanged=[...]"` | `TenantVectorRegistry.cs:173-181` |

For initial load verification: grep for `"TenantVectorWatcher initial load complete"` or `"Tenant vector reload complete"`.
For hot-reload verification: apply modified ConfigMap, grep for `"TenantVectorWatcher received Modified event"` and `"added: [new-tenant-id]"` in the registry diff log.

### Pattern 4: E2E Script Structure

Follow the exact pattern of scenarios 24 and 25. Key structure:

```bash
# Scenario 28: TenantVector routing — fan-out counter > 0
SCENARIO_NAME="TenantVector routing: snmp_tenantvector_routed_total increments"
METRIC="snmp_tenantvector_routed_total"

# Step 1: Verify ConfigMap volume mount
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
POD1=$(echo "$PODS" | awk '{print $1}')
if kubectl describe pod "$POD1" -n simetra | grep "simetra-tenantvector" > /dev/null 2>&1; then
    MOUNT_OK=1
fi

# Step 2: Wait for initial load log
for POD in $PODS; do
    if kubectl logs "$POD" -n simetra --since=300s 2>/dev/null | grep "TenantVectorWatcher initial load complete" > /dev/null 2>&1; then
        INITIAL_LOAD_OK=1
        break
    fi
done

# Step 3: Poll Prometheus counter
BEFORE=$(snapshot_counter "$METRIC" "")
poll_until 90 5 "$METRIC" "" "$BEFORE" || true
AFTER=$(query_counter "$METRIC" "")
DELTA=$((AFTER - BEFORE))

# Step 4: Hot-reload test — apply modified ConfigMap with 4th tenant
# Build modified ConfigMap inline (adds "obp-poll-2" tenant)
kubectl apply -f - <<EOF
...4-tenant ConfigMap...
EOF
sleep 15
# Check for reload log with added tenant
for POD in $PODS; do
    if kubectl logs "$POD" -n simetra --since=30s 2>/dev/null | grep "added=\[obp-poll-2\]" > /dev/null 2>&1; then
        RELOAD_OK=1; break
    fi
done
# Restore original tenantvector ConfigMap
kubectl apply -f deploy/k8s/production/configmap.yaml -n simetra || \
    kubectl apply -f deploy/k8s/snmp-collector/simetra-tenantvector.yaml -n simetra
```

The e2e script should derive ClusterIPs dynamically:
```bash
NPB_IP=$(kubectl get svc npb-simulator -n simetra -o jsonpath='{.spec.clusterIP}')
OBP_IP=$(kubectl get svc obp-simulator -n simetra -o jsonpath='{.spec.clusterIP}')
```

This avoids hard-coding IPs while keeping the tenantvector ConfigMap in repo with the correct IPs (set at apply-time via the script).

### Pattern 5: Separate simetra-tenantvector ConfigMap File

Rather than keeping the tenantvector ConfigMap only in the monolithic `production/configmap.yaml`, the e2e scenario should be able to reference it independently. Options:
1. Apply the relevant section of `production/configmap.yaml` using `kubectl apply -f` with the full file
2. Create a separate `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` for dev use (mirroring the simetra-devices pattern)

**Recommendation:** Create `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` with the 3-tenant test config. This file is what gets applied in the e2e scenario and can be restored after hot-reload test. Use the same file structure as `simetra-devices.yaml`.

### Anti-Patterns to Avoid

- **DNS hostnames in MetricSlotOptions.Ip**: The validator calls `IPAddress.TryParse` — DNS names fail validation. Use ClusterIP.
- **Trap-sourced metrics in tenantvector for static config**: Trap `AgentIp` is the sender pod IP, which changes on restart. Only poll-sourced metrics have stable routing via service ClusterIP.
- **Hard-coding ClusterIPs in committed ConfigMap files**: ClusterIPs are assigned by K8s at service creation and differ between clusters. Either derive them at deploy-time via `kubectl get svc` or document that the IP must be updated per-cluster.
- **Forgetting to apply simetra-tenantvector before running e2e**: The ConfigMap must be applied and the deployment must be restarted before the routing counter can increment.
- **Not adding simetra-tenantvector to projected volume**: The watcher reads from `/app/config/tenantvector.json` — if the ConfigMap is not mounted, the initial load silently fails.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ClusterIP lookup | Hard-code in yaml | `kubectl get svc -o jsonpath '{.spec.clusterIP}'` | IPs differ per cluster; static values break portability |
| ConfigMap apply | Custom YAML builder | Inline heredoc with `kubectl apply -f -` | Already pattern established in scenario 06 |
| Restore after hot-reload | Custom restore logic | `kubectl apply -f deploy/k8s/snmp-collector/simetra-tenantvector.yaml` | ConfigMap file is the source of truth |
| Log search across pods | Single pod log check | Loop over all pods matching label selector | Existing pattern in scenarios 24 and 25 |

## Common Pitfalls

### Pitfall 1: DNS Hostname in Ip Field Fails Validator

**What goes wrong:** Putting `npb-simulator.simetra.svc.cluster.local` in `MetricSlotOptions.Ip` causes `TenantVectorOptionsValidator` to fail with `"Ip '...' is not a valid IP address"`. The watcher logs a validation error, the registry is not reloaded, and `snmp_tenantvector_routed_total` stays at 0.

**Why it happens:** The CONTEXT.md says "IPs must match the device IpAddress in simetra-devices (K8s DNS names)" — but the validator enforces `IPAddress.TryParse`. The routing key also needs the actual resolved IP that appears in `SnmpOidReceived.AgentIp`.

**How to avoid:** Use the K8s Service ClusterIP (e.g., `10.96.x.x`) for all `MetricSlotOptions.Ip` values. Derive it with `kubectl get svc`.

**Warning signs:** `TenantVectorRegistry reloaded: tenants=0, slots=0` or validation error log after ConfigMap apply.

### Pitfall 2: Both deployment.yaml Files Must Be Updated

**What goes wrong:** Only updating `deploy/k8s/snmp-collector/deployment.yaml` but forgetting `deploy/k8s/production/deployment.yaml`. The production deployment.yaml is at `deploy/k8s/production/deployment.yaml` and is a separate file from the dev one. Both files currently have only 3 ConfigMap sources.

**Why it happens:** Two deployment.yaml files exist for dev and production contexts.

**How to avoid:** Update both files. Verify by searching for `simetra-tenantvector` in both locations after the edit.

### Pitfall 3: simetra-devices REPLACE_ME Still in Production configmap.yaml

**What goes wrong:** The production `configmap.yaml` still has `REPLACE_ME_DEVICE_NAME` and `REPLACE_ME_DEVICE_IP`. If this file is applied in production, the DeviceWatcherService will load these invalid device entries and DNS resolution will fail.

**Why it happens:** Phase 29 CONTEXT.md says to populate simetra-devices — this refers to the production configmap, not the dev simetra-devices.yaml (which is already correct).

**How to avoid:** Replace the REPLACE_ME placeholders in `deploy/k8s/production/configmap.yaml` simetra-devices section with the same DNS entries from `deploy/k8s/snmp-collector/simetra-devices.yaml`.

### Pitfall 4: Routing Counter Only Increments for Resolved OIDs

**What goes wrong:** `snmp_tenantvector_routed_total` stays at 0 even though polls are running. The `TenantVectorFanOutBehavior` skips routing for OIDs where `MetricName` is null or "unknown" — this means if the OID map isn't loaded, no routing happens. Also, routing requires `_deviceRegistry.TryGetDeviceByName` to succeed.

**Why it happens:** The `TenantVectorFanOutBehavior.Handle` has an explicit guard: `if (metricName is not null && metricName != OidMapService.Unknown)`. If the OID map hasn't been loaded by the time polls fire, the behavior passes through silently.

**How to avoid:** Ensure OID map is loaded before checking the counter. Wait a minimum of 30 seconds after deployment restart before asserting the counter. Use `poll_until` with 90s timeout as in other scenarios.

### Pitfall 5: Trap AgentIp ≠ Service ClusterIP

**What goes wrong:** Including NPB port_status metrics in a "npb-trap" tenant expecting trap-originated data to route — but trap varbinds have `AgentIp` = pod IP (e.g., `10.244.x.x`), not the service ClusterIP (e.g., `10.96.x.x`). The routing key `(podIP, 161, "npb_port_status_P1")` will never match the slot `(clusterIP, 161, "npb_port_status_P1")`.

**Why it happens:** `SnmpTrapListenerService.ProcessDatagram` uses `result.RemoteEndPoint.Address.MapToIPv4()` as `AgentIp`. Pod IPs come from the pod CIDR, not the service CIDR.

**How to avoid:** Use poll-sourced metrics for all tenants. The same metric name (e.g., `npb_port_status_P1`) appears in both poll and trap paths — but for routing purposes, only poll events have the stable ClusterIP.

### Pitfall 6: Hot-Reload Test Fails Because ConfigMap Apply Requires Registry

**What goes wrong:** The hot-reload scenario creates a ConfigMap with a 4th tenant that includes new `MetricName` values. If those MetricNames aren't in the OID map, the validator blocks the reload and the diff log never shows "added: [obp-poll-2]".

**Why it happens:** `TenantVectorOptionsValidator` checks `_oidMapService.ContainsMetricName` for each MetricName.

**How to avoid:** For the 4th tenant in hot-reload test, use MetricNames that are already confirmed to be in the OID map. Suggested: use a subset of existing OBP metrics (e.g., `obp_r3_power_L1`, `obp_r4_power_L1`, `obp_link_state_L1`, `obp_link_state_L2`) already present in `simetra-oidmaps`.

## Code Examples

### Deployment.yaml — Add 4th Projected Source

```yaml
# Source: deploy/k8s/snmp-collector/deployment.yaml + deploy/k8s/production/deployment.yaml
      volumes:
      - name: config
        projected:
          sources:
          - configMap:
              name: snmp-collector-config
          - configMap:
              name: simetra-oidmaps
          - configMap:
              name: simetra-devices
          - configMap:
              name: simetra-tenantvector    # ADD THIS
```

### E2E Script — Dynamic ClusterIP Lookup

```bash
# Source: pattern from scenario 06 (kubectl get svc pattern)
NPB_IP=$(kubectl get svc npb-simulator -n simetra -o jsonpath='{.spec.clusterIP}')
OBP_IP=$(kubectl get svc obp-simulator -n simetra -o jsonpath='{.spec.clusterIP}')
```

### E2E Script — kubectl describe for Volume Mount Verification

```bash
# Source: pattern using kubectl describe to check volume
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
POD1=$(echo "$PODS" | awk '{print $1}')
if kubectl describe pod "$POD1" -n simetra | grep "simetra-tenantvector" > /dev/null 2>&1; then
    FOUND_MOUNT=1
fi
```

### E2E Script — Hot-Reload with Inline ConfigMap Apply

```bash
# Source: pattern from scenario 06 (heredoc apply)
NPB_IP=$(kubectl get svc npb-simulator -n simetra -o jsonpath='{.spec.clusterIP}')
OBP_IP=$(kubectl get svc obp-simulator -n simetra -o jsonpath='{.spec.clusterIP}')

HOT_RELOAD_CM=$(mktemp)
cat > "$HOT_RELOAD_CM" <<CMEOF
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-tenantvector
  namespace: simetra
data:
  tenantvector.json: |
    {
      "Tenants": [
        ... (original 3 tenants) ...,
        {
          "Id": "obp-poll-2",
          "Priority": 4,
          "Metrics": [
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_r3_power_L1", "IntervalSeconds": 10 },
            { "Ip": "${OBP_IP}", "Port": 161, "MetricName": "obp_r4_power_L1", "IntervalSeconds": 10 }
          ]
        }
      ]
    }
CMEOF

kubectl apply -f "$HOT_RELOAD_CM"
rm -f "$HOT_RELOAD_CM"
```

### Log Search — Reload Diff for Added Tenant

```bash
# Source: pattern from scenarios 24 and 25 (log grep avoiding -q SIGPIPE)
for POD in $PODS; do
    if kubectl logs "$POD" -n simetra --since=60s 2>/dev/null | grep "added=\[obp-poll-2\]" > /dev/null 2>&1; then
        RELOAD_FOUND=1
        EVIDENCE="pod=${POD} saw 'added=[obp-poll-2]' in TenantVectorRegistry reloaded log"
        break
    fi
done
```

### E2E Script — Restore ConfigMap After Hot-Reload

```bash
# Restore by re-applying the known-good tenantvector file (if it exists as separate file)
# OR by re-applying the section from production configmap.yaml
kubectl apply -f "$SCRIPT_DIR/../../deploy/k8s/snmp-collector/simetra-tenantvector.yaml" || \
    log_warn "Could not restore tenantvector ConfigMap — manual restore required"
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| REPLACE_ME placeholders in production configmap | Populated with real simulator DNS entries | Phase 29 | simetra-devices becomes fully functional in production |
| simetra-tenantvector with `{ "Tenants": [] }` | Populated with 3 test tenants | Phase 29 | Routing counter will increment; E2E validation possible |
| 3-source projected volume | 4-source projected volume (+ simetra-tenantvector) | Phase 29 | TenantVectorWatcherService can read its ConfigMap in K8s |

## Open Questions

1. **ClusterIP stability across cluster resets**
   - What we know: K8s Services retain their ClusterIP as long as the Service object exists. ClusterIPs can change if the cluster is torn down and recreated (e.g., `kind` cluster restart).
   - What's unclear: Should the production tenantvector ConfigMap use ClusterIPs directly, or should there be a mechanism to update them?
   - Recommendation: For this test environment, embed current ClusterIPs at plan-time in the ConfigMap. The e2e script derives IPs dynamically at runtime via `kubectl get svc`. Document in a comment that IPs must match the current cluster.

2. **Separate simetra-tenantvector.yaml vs. embedded in production/configmap.yaml**
   - What we know: The production `configmap.yaml` embeds all ConfigMaps in one file. The dev `deploy/k8s/snmp-collector/` directory has separate files per ConfigMap.
   - What's unclear: Should we create `deploy/k8s/snmp-collector/simetra-tenantvector.yaml`?
   - Recommendation: Yes, create `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` for the dev workflow, mirroring the pattern used for `simetra-devices.yaml`. The e2e script references this file for restore operations.

3. **E2E scenario numbering**
   - What we know: Existing scenarios go up to `27-watcher-reconnect.sh`. New scenario is the next in sequence.
   - Recommendation: Use `28-tenantvector-routing.sh` as the filename.

4. **report.sh category update**
   - What we know: `tests/e2e/lib/report.sh` has category ranges with fixed indices. Adding scenario 28 extends "Watcher Resilience" category or needs a new "Tenant Vector" category.
   - Recommendation: Add a new "Tenant Vector" category covering scenarios 28+. Update `_REPORT_CATEGORIES` in `report.sh`.

## Sources

### Primary (HIGH confidence)

- `src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs` — IP validation rule (Rule 3: `IPAddress.TryParse`)
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — routing key uses `msg.AgentIp.ToString()` + `device.Port`
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs:137` — trap AgentIp set from `result.RemoteEndPoint.Address.MapToIPv4()` (pod IP)
- `src/SnmpCollector/Jobs/MetricPollJob.cs:172` — poll AgentIp set from `IPAddress.Parse(device.ResolvedIp)` (service ClusterIP)
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs:49` — DNS resolution via `Dns.GetHostAddresses` at startup
- `src/SnmpCollector/Services/TenantVectorWatcherService.cs` — log message strings for e2e grep
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs:173-181` — structured diff log format `"TenantVectorRegistry reloaded: tenants={N}..."`
- `deploy/k8s/snmp-collector/deployment.yaml` — current 3-source projected volume pattern
- `deploy/k8s/production/deployment.yaml` — also has 3-source projected volume (same change needed)
- `deploy/k8s/production/configmap.yaml:261-293` — simetra-tenantvector already exists with empty Tenants
- `deploy/k8s/production/configmap.yaml:221-259` — simetra-devices has REPLACE_ME placeholders
- `deploy/k8s/snmp-collector/simetra-devices.yaml` — correct DNS entries (npb-simulator.simetra.svc.cluster.local, obp-simulator.simetra.svc.cluster.local)
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` — metric names for tenant slot configuration
- `tests/e2e/scenarios/24-oidmap-watcher-log.sh` — e2e scenario pattern (multi-pod log search)
- `tests/e2e/scenarios/25-device-watcher-log.sh` — e2e scenario pattern (ConfigMap apply + log check)
- `tests/e2e/scenarios/06-poll-unreachable.sh` — inline heredoc ConfigMap apply pattern
- `tests/e2e/lib/prometheus.sh` — `snapshot_counter`, `poll_until`, `get_evidence`
- `tests/e2e/lib/common.sh` — `record_pass`, `record_fail`, `assert_delta_gt`
- `tests/e2e/lib/kubectl.sh` — `snapshot_configmaps`, `restore_configmaps`, `save_configmap`
- `tests/e2e/lib/report.sh` — `_REPORT_CATEGORIES` (needs update for scenario 28)

### Secondary (MEDIUM confidence)

None needed — all findings verified directly from codebase.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — kubectl, bash, and Prometheus patterns directly observed in existing scenarios
- Architecture: HIGH — routing constraint verified from direct code inspection of validator, fan-out behavior, trap listener, and poll job
- Pitfalls: HIGH — all discovered from direct code inspection with exact file/line references

**Research date:** 2026-03-10
**Valid until:** Until TenantVectorOptionsValidator or TenantVectorFanOutBehavior are changed
