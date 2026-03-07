# Phase 14: K8s Integration and E2E - Research

**Researched:** 2026-03-07
**Domain:** Kubernetes ConfigMap configuration, SNMP metric pipeline E2E verification
**Confidence:** HIGH

## Summary

This phase connects existing simulator pods to the existing snmp-collector deployment via ConfigMap configuration and verifies end-to-end metric flow through Prometheus. The infrastructure (simulators, snmp-collector, OTel Collector, Prometheus) is already deployed in K8s. The work is: (1) create a `devices.json` ConfigMap key with MetricPoll entries for both OBP and NPB devices covering all 92 OIDs, (2) update the snmp-collector deployment to use directory-mount instead of subPath so auto-scan can discover `devices.json`, (3) extend `Program.cs` auto-scan to load `devices.json`, and (4) write a `verify-e2e.sh` script that queries Prometheus to confirm polled and trap metrics arrive correctly.

The existing codebase already handles the full pipeline: MetricPollJob polls devices, SnmpTrapListenerService receives traps, OidMapService resolves OID names, OtelMetricHandler records `snmp_gauge`/`snmp_info` metrics via OpenTelemetry, which flows through OTel Collector's prometheusremotewrite exporter to Prometheus. No new code paths are needed -- only configuration and verification.

**Primary recommendation:** Focus on getting the ConfigMap right (devices.json with all 92 OIDs, correct DNS addresses, correct community strings) and fixing the snmp-collector volume mount to directory-mount for auto-scan support. The E2E script is straightforward Prometheus API queries.

## Standard Stack

### Core (Already Deployed)
| Component | Version/Image | Purpose | Status |
|-----------|--------------|---------|--------|
| snmp-collector | local build | Polls devices, receives traps, emits OTel metrics | Deployed, 3 replicas |
| obp-simulator | local build | Serves 24 OBP OIDs + StateChange traps | Deployed, 1 replica |
| npb-simulator | local build | Serves 68 NPB OIDs + portLinkChange traps | Deployed, 1 replica |
| OTel Collector | otel/opentelemetry-collector-contrib:0.120.0 | Receives OTLP, writes to Prometheus | Deployed |
| Prometheus | prom/prometheus:v3.2.1 | Stores metrics, exposes query API | Deployed |

### Verification Script Tools
| Tool | Purpose | Why |
|------|---------|-----|
| kubectl port-forward | Access Prometheus from local machine | Standard K8s pattern, no NodePort needed |
| curl + Prometheus HTTP API | Query for metric existence and labels | Simple, scriptable, no extra dependencies |
| jq | Parse Prometheus JSON responses | Standard JSON processing in shell scripts |

## Architecture Patterns

### ConfigMap Organization

The snmp-collector ConfigMap (`snmp-collector-config`) must contain these keys:

```
snmp-collector-config (ConfigMap)
  appsettings.k8s.json    # Core settings (existing)
  oidmap-obp.json         # OBP OID map (existing, 24 entries)
  oidmap-npb.json         # NPB OID map (existing, 68 entries)
  devices.json            # NEW: Device definitions with MetricPoll groups
```

All keys are mounted as files in `/app/config` via directory mount (NOT subPath).

### Critical: Volume Mount Must Change

**Current snmp-collector deployment** uses subPath mount:
```yaml
volumeMounts:
- name: snmp-collector-config
  mountPath: /app/appsettings.Production.json
  subPath: appsettings.k8s.json
```

This mounts a single file, bypassing the auto-scan logic in `Program.cs`. It must change to directory mount:
```yaml
volumeMounts:
- name: snmp-collector-config
  mountPath: /app/config
  readOnly: true
```

With the `CONFIG_DIRECTORY` env var (already set in production deployment but MISSING from snmp-collector deployment):
```yaml
env:
- name: CONFIG_DIRECTORY
  value: /app/config
```

### Critical: Program.cs Auto-Scan Gap

**Current auto-scan** in `Program.cs` (lines 29-33) only scans `oidmap-*.json`:
```csharp
foreach (var file in Directory.GetFiles(configDir, "oidmap-*.json").OrderBy(f => f))
```

It does NOT scan for `devices.json`. The `Devices` array needs to be loaded either by:
1. Adding `devices.json` scan to Program.cs (recommended -- keeps config files separate)
2. Putting the `Devices` array inside `appsettings.k8s.json` (works but mixes concerns)

**Recommendation:** Add explicit `devices.json` loading to Program.cs after the oidmap scan:
```csharp
var devicesConfig = Path.Combine(configDir, "devices.json");
if (File.Exists(devicesConfig))
{
    builder.Configuration.AddJsonFile(devicesConfig, optional: true, reloadOnChange: true);
}
```

### Pattern: One MetricPoll Per Device (All OIDs Together)

Per the CONTEXT.md decision: one MetricPoll entry per device containing ALL OIDs. The MetricPollOptions model has:
```csharp
public sealed class MetricPollOptions
{
    public List<string> Oids { get; set; } = [];
    public int IntervalSeconds { get; set; }
}
```

Each device gets a single MetricPoll with all its OIDs and 10-second interval.

### Pattern: K8s Service DNS for Device Addresses

Simulators already have ClusterIP Services:
- `obp-simulator.simetra.svc.cluster.local` (port 161/UDP)
- `npb-simulator.simetra.svc.cluster.local` (port 161/UDP)

**Important:** DeviceRegistry parses IP addresses (`IPAddress.Parse(d.IpAddress)`), so the `IpAddress` field must be an actual IP, not a DNS name. The devices.json must use the Service ClusterIP or Pod IP.

**Wait -- this is a critical finding.** Looking at `DeviceRegistry.cs` line 35:
```csharp
var ip = IPAddress.Parse(d.IpAddress).MapToIPv4();
```

And `MetricPollJob.cs` line 82:
```csharp
var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), device.Port);
```

Both use `IPAddress.Parse()` which does NOT resolve DNS names. The existing `configmap-devices.yaml` uses `PLACEHOLDER_NPB_POD_IP` / `PLACEHOLDER_OBP_POD_IP` placeholders for this reason.

**However**, the CONTEXT.md decision says "K8s Service DNS names for simulator addresses (e.g., `obp-simulator.simetra.svc.cluster.local`) -- no placeholder IP replacement needed." This conflicts with the current code which only accepts IP addresses.

**Resolution options:**
1. Change `DeviceOptions.IpAddress` parsing to support DNS resolution (code change)
2. Use Pod IPs discovered at deployment time (operational complexity)
3. Use `Dns.GetHostAddresses()` in DeviceRegistry at startup

**Recommendation:** Option 1 or 3 -- resolve DNS to IP at startup in DeviceRegistry. This is the simplest path to honor the CONTEXT.md decision.

### Metric Flow: How Metrics Appear in Prometheus

The pipeline emits OpenTelemetry gauge instruments (`snmp_gauge`, `snmp_info`) which flow:

1. snmp-collector -> OTLP gRPC -> OTel Collector
2. OTel Collector transform processor adds `host_name` from `service.instance.id`
3. OTel Collector -> Prometheus remote write -> Prometheus

In Prometheus, the metric name is `snmp_gauge` with labels:
- `host_name` - K8s node name
- `pod_name` - pod hostname
- `metric_name` - resolved OID name (e.g., `obp_r1_power_L1`, `npb_cpu_util`)
- `oid` - raw OID string
- `device_name` - device name (e.g., `OBP-01`, `NPB-01`)
- `ip` - device IP address
- `source` - `poll` or `trap`
- `snmp_type` - SNMP type (e.g., `integer32`, `gauge32`, `counter32`)

Plus `resource_to_telemetry_conversion: enabled` adds resource attributes as labels:
- `service_name` - `snmp-collector`
- `service_instance_id` - pod identity

### Trap Verification: What to Look For

OBP traps: StateChange notification with varbind OID = `1.3.6.1.4.1.47477.10.21.{N}.3.4.0` (channel OID, which IS in the OID map as `obp_channel_L{N}`). So trap metrics appear as `snmp_gauge{metric_name="obp_channel_L1", source="trap"}`.

NPB traps: portLinkChange notification with varbind OID = `1.3.6.1.4.1.47477.100.2.{port}.1.0` (port_status OID, which IS in the OID map as `npb_port_status_P{N}`). So trap metrics appear as `snmp_gauge{metric_name="npb_port_status_P1", source="trap"}`.

### Headless Service for Trap Delivery

Traps are sent to `simetra-pods.simetra.svc.cluster.local` (headless service, already deployed). This resolves to all pod IPs, and simulators send traps to each resolved IP. The headless service exists at `deploy/k8s/simulators/simetra-headless.yaml` but targets `app: simetra` NOT `app: snmp-collector`.

**This needs to be checked.** The snmp-collector pods have label `app: snmp-collector`, but the headless service selects `app: simetra`. Either:
1. The headless service selector needs updating to `app: snmp-collector`
2. Or a new headless service is needed for snmp-collector pods

The simulator deployments reference `TRAP_TARGET: simetra-pods.simetra.svc.cluster.local`. This must resolve to snmp-collector pod IPs for traps to arrive.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Prometheus querying | Custom HTTP client | `curl` + Prometheus HTTP API `/api/v1/query` | Standard, well-documented REST API |
| JSON parsing in shell | sed/awk/grep on JSON | `jq` | Proper JSON parsing, handles edge cases |
| DNS resolution for devices | IP placeholder replacement scripts | DNS resolution at app startup (Dns.GetHostAddresses) | Cleaner than operational scripts |
| Port forwarding | NodePort services | `kubectl port-forward` | No cluster config changes, works on any K8s |

## Common Pitfalls

### Pitfall 1: subPath Mount Prevents Auto-Scan
**What goes wrong:** snmp-collector deployment uses `subPath: appsettings.k8s.json` which mounts a single file at a specific path. The auto-scan in Program.cs looks for files in a directory (`CONFIG_DIRECTORY`).
**Why it happens:** The snmp-collector deployment was created before the auto-scan pattern was established.
**How to avoid:** Change to directory mount at `/app/config` and add `CONFIG_DIRECTORY=/app/config` env var.
**Warning signs:** snmp-collector starts but has no devices, no OID map, no polls.

### Pitfall 2: IPAddress.Parse Does Not Resolve DNS
**What goes wrong:** DeviceRegistry and MetricPollJob use `IPAddress.Parse()` which throws on hostnames like `obp-simulator.simetra.svc.cluster.local`.
**Why it happens:** The code was written for IP-based configuration, not DNS.
**How to avoid:** Add DNS resolution logic before parsing, or resolve at DeviceRegistry construction time.
**Warning signs:** `FormatException: An invalid IP address was specified` at startup.

### Pitfall 3: Headless Service Selector Mismatch
**What goes wrong:** The `simetra-pods` headless service selects `app: simetra` but snmp-collector pods have `app: snmp-collector`. Traps from simulators resolve no endpoints.
**How to avoid:** Create a headless service with `app: snmp-collector` selector, or update simulator `TRAP_TARGET` to point to the correct service.
**Warning signs:** Simulator logs show "Trap send failed" or traps silently disappear.

### Pitfall 4: Prometheus Remote Write Metric Name Convention
**What goes wrong:** Prometheus remote write may add suffixes to metric names. OTel gauge instruments may appear with `_gauge` suffix or similar transformations.
**Why it happens:** OTel-to-Prometheus naming conventions apply transformations.
**How to avoid:** Test actual metric names in Prometheus before writing verification queries. The actual Prometheus metric name for an OTel gauge named "snmp_gauge" might be just `snmp_gauge` (no suffix since it's already a gauge).
**Warning signs:** PromQL queries return empty results despite metrics being emitted.

### Pitfall 5: Counter Metrics via Gauge Instrument
**What goes wrong:** Counter32/Counter64 SNMP values are recorded as `snmp_gauge` (not a Prometheus counter), so `rate()` works but `increase()` may show unexpected values on resets.
**Why it happens:** Architectural decision -- all SNMP numeric values go through `snmp_gauge` gauge instrument.
**How to avoid:** Verification script should check for existence via `snmp_gauge{metric_name="npb_port_rx_octets_P1"}`, not a counter-style metric.

### Pitfall 6: Trap Timing in E2E Verification
**What goes wrong:** Traps fire at random intervals (60-300 seconds). E2E script may time out waiting.
**Why it happens:** Realistic simulation uses random intervals.
**How to avoid:** 5-minute timeout as decided in CONTEXT.md. Script polls Prometheus every 10-15 seconds.
**Warning signs:** Script passes for polls but times out for traps.

### Pitfall 7: OID Map Required for Trap Metric Names
**What goes wrong:** Trap varbind OIDs that aren't in the OID map get `metric_name="unknown"` and are effectively lost.
**Why it happens:** OidResolutionBehavior maps OIDs to names; unmapped OIDs default to "unknown".
**How to avoid:** Verify all trap varbind OIDs are in oidmap-obp.json / oidmap-npb.json. They are: channel OIDs (`obp_channel_L{N}`) and port_status OIDs (`npb_port_status_P{N}`) are already mapped.

## Code Examples

### devices.json Structure

```json
{
  "Devices": [
    {
      "Name": "OBP-01",
      "IpAddress": "obp-simulator.simetra.svc.cluster.local",
      "Port": 161,
      "MetricPolls": [
        {
          "Oids": [
            "1.3.6.1.4.1.47477.10.21.1.3.1.0",
            "1.3.6.1.4.1.47477.10.21.1.3.4.0",
            "1.3.6.1.4.1.47477.10.21.1.3.10.0",
            ... all 24 OBP OIDs ...
          ],
          "IntervalSeconds": 10
        }
      ]
    },
    {
      "Name": "NPB-01",
      "IpAddress": "npb-simulator.simetra.svc.cluster.local",
      "Port": 161,
      "MetricPolls": [
        {
          "Oids": [
            "1.3.6.1.4.1.47477.100.1.1.0",
            "1.3.6.1.4.1.47477.100.1.2.0",
            ... all 68 NPB OIDs ...
          ],
          "IntervalSeconds": 10
        }
      ]
    }
  ]
}
```

Note: The CONTEXT.md mentions "Explicit per-OID type" and "CommunityString field" but the current `MetricPollOptions` model only has `Oids` (list of strings) and `IntervalSeconds`. It does NOT have per-OID type or CommunityString. The existing template `configmap-devices.yaml` shows a different model with `MetricName`, `MetricType`, `Oids[].Oid/.PropertyName/.Role` structure -- but that's the production template pattern, not what the SnmpCollector code actually binds.

**Critical model check:** The actual C# model is:
```csharp
public sealed class MetricPollOptions
{
    public List<string> Oids { get; set; } = [];  // Just OID strings
    public int IntervalSeconds { get; set; }
}

public sealed class DeviceOptions
{
    public string Name { get; set; }
    public string IpAddress { get; set; }
    public int Port { get; set; } = 161;
    public List<MetricPollOptions> MetricPolls { get; set; } = [];
    // NO CommunityString field
    // NO DeviceType field
}
```

Community string is derived from device name at runtime (`Simetra.{DeviceName}`). There is no `CommunityString` field in `DeviceOptions`. The CONTEXT.md decision for "Explicit CommunityString field" would require adding this to the model.

Similarly, `DeviceType` is not a field on `DeviceOptions`. The production template has it but the C# model doesn't bind it.

### Prometheus API Query for Metric Existence

```bash
# Check if snmp_gauge metrics exist for a specific device
curl -s "http://localhost:9090/api/v1/query?query=snmp_gauge{device_name=\"OBP-01\"}" | jq '.data.result | length'

# Check for specific metric_name label
curl -s "http://localhost:9090/api/v1/query?query=snmp_gauge{metric_name=\"obp_r1_power_L1\"}" | jq '.data.result | length'

# Check for trap-sourced metrics
curl -s "http://localhost:9090/api/v1/query?query=snmp_gauge{source=\"trap\",device_name=\"OBP-01\"}" | jq '.data.result | length'
```

### verify-e2e.sh Script Pattern

```bash
#!/usr/bin/env bash
set -euo pipefail

PROMETHEUS_URL="http://localhost:9090"
POLL_INTERVAL=15
TRAP_TIMEOUT=300

check_metric() {
    local query="$1"
    local description="$2"
    local result
    result=$(curl -s "${PROMETHEUS_URL}/api/v1/query?query=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$query'))")" \
        | jq -r '.data.result | length')
    if [[ "$result" -gt 0 ]]; then
        echo "PASS: $description ($result series)"
        return 0
    else
        echo "FAIL: $description (0 series)"
        return 1
    fi
}

wait_for_metric() {
    local query="$1"
    local description="$2"
    local timeout="$3"
    local elapsed=0
    while [[ $elapsed -lt $timeout ]]; do
        if check_metric "$query" "$description" 2>/dev/null; then
            return 0
        fi
        sleep "$POLL_INTERVAL"
        elapsed=$((elapsed + POLL_INTERVAL))
    done
    echo "TIMEOUT: $description (waited ${timeout}s)"
    return 1
}
```

### DNS Resolution in DeviceRegistry (If Needed)

```csharp
// In DeviceRegistry constructor, before IPAddress.Parse:
IPAddress ip;
if (IPAddress.TryParse(d.IpAddress, out var parsed))
{
    ip = parsed.MapToIPv4();
}
else
{
    // Resolve DNS name to IP at startup
    var addresses = Dns.GetHostAddresses(d.IpAddress);
    ip = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
}
```

## State of the Art

| Aspect | Current State | What Needs to Change |
|--------|--------------|---------------------|
| snmp-collector volume mount | subPath (single file) | Directory mount at /app/config |
| CONFIG_DIRECTORY env var | Missing from snmp-collector deploy | Add `CONFIG_DIRECTORY=/app/config` |
| Program.cs auto-scan | Only loads `oidmap-*.json` | Add `devices.json` loading |
| DeviceOptions model | No CommunityString, no DeviceType | Add fields per CONTEXT.md decisions |
| DeviceRegistry IP parsing | IPAddress.Parse only (no DNS) | Add DNS resolution support |
| Headless service selector | Targets `app: simetra` | Needs `app: snmp-collector` for trap delivery |
| SNMP port on snmp-collector deploy | Not in current deployment | Add UDP port 10162 containerPort |

## Open Questions

1. **DeviceOptions Model Changes vs CONTEXT.md**
   - What we know: CONTEXT.md says "Explicit CommunityString field" and "Explicit per-OID type", but the current C# model has neither. The production template YAML has these fields but they're not in the binding model.
   - What's unclear: Does the planner need to add these fields to the C# model, or are they documentation-only in the JSON?
   - Recommendation: Add `CommunityString` to `DeviceOptions` (optional, falls back to `Simetra.{Name}` convention). Skip per-OID type since the pipeline doesn't use it (all values go to `snmp_gauge`).

2. **DNS vs IP for Device Addresses**
   - What we know: CONTEXT.md says use DNS names. Code requires IPs.
   - What's unclear: How much code change is acceptable for this phase.
   - Recommendation: Add DNS resolution in DeviceRegistry -- minimal change, honors the decision.

3. **Headless Service for Trap Delivery**
   - What we know: `simetra-pods` headless service exists but selects wrong label (`app: simetra` not `app: snmp-collector`).
   - What's unclear: Should we update the existing service or create a new one?
   - Recommendation: Either update the selector or create `snmp-collector-pods` headless service. Update simulator TRAP_TARGET accordingly.

4. **snmp-collector SNMP UDP Port**
   - What we know: The snmp-collector deployment at `deploy/k8s/snmp-collector/deployment.yaml` does NOT expose port 10162/UDP as a containerPort.
   - What's unclear: Whether containerPort declaration is needed for receiving traps (K8s allows traffic to any port on a pod, containerPort is informational).
   - Recommendation: Add it for clarity, though it's not strictly required.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: `src/SnmpCollector/Program.cs` - auto-scan logic (lines 13-34)
- Codebase analysis: `src/SnmpCollector/Configuration/DeviceOptions.cs` - model fields
- Codebase analysis: `src/SnmpCollector/Configuration/MetricPollOptions.cs` - poll model
- Codebase analysis: `src/SnmpCollector/Pipeline/DeviceRegistry.cs` - IP parsing (line 35)
- Codebase analysis: `src/SnmpCollector/Jobs/MetricPollJob.cs` - IP parsing (line 82)
- Codebase analysis: `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` - metric labels
- Codebase analysis: `deploy/k8s/snmp-collector/deployment.yaml` - current volume mount
- Codebase analysis: `deploy/k8s/simulators/obp-deployment.yaml` - trap target config
- Codebase analysis: `deploy/k8s/simulators/simetra-headless.yaml` - service selector
- Codebase analysis: `deploy/k8s/monitoring/otel-collector-configmap.yaml` - metric pipeline
- Codebase analysis: `simulators/obp/obp_simulator.py` - trap OID + varbind mapping
- Codebase analysis: `simulators/npb/npb_simulator.py` - trap OID + varbind mapping

### Secondary (MEDIUM confidence)
- Prometheus HTTP API v1 query endpoint pattern (well-known, stable API)

## Metadata

**Confidence breakdown:**
- ConfigMap structure: HIGH - directly verified against codebase models and existing configs
- Volume mount issue: HIGH - directly compared deployment YAML with Program.cs auto-scan logic
- DNS resolution gap: HIGH - verified IPAddress.Parse usage in DeviceRegistry and MetricPollJob
- Headless service mismatch: HIGH - verified label selectors in YAML
- Prometheus query patterns: MEDIUM - standard API, not verified against this specific Prometheus version
- Trap varbind OID mapping: HIGH - traced from simulator source to OID map entries

**Research date:** 2026-03-07
**Valid until:** 2026-04-07 (stable codebase, no external dependency changes expected)
