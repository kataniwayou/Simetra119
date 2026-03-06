# Quick Task 016: E2E Deploy and Test Heartbeat Metrics

## What Was Done

### Task 1: Build Docker image and redeploy to K8s

**Dockerfile fix:** The Dockerfile was building the Simetra reference project (`src/Simetra/Simetra.csproj`) instead of SnmpCollector. Rewritten to:
- Build `src/SnmpCollector/SnmpCollector.csproj`
- Entrypoint `dotnet SnmpCollector.dll`
- Removed unnecessary `sed` command and solution file copy

**ConfigMap update:** `deploy/k8s/snmp-collector/configmap.yaml` updated:
- Added `HeartbeatJob: { IntervalSeconds: 15 }`
- Added `OidMap: { "1.3.6.1.4.1.9999.1.1.1.0": "simetraHeartbeat" }`
- Removed stale `CommunityString: "public"` from SnmpListener
- Changed Devices from dummy-device-01 to empty array
- Added `LogLevel: { Default: "Debug" }`

**Deployment:** Built `snmp-collector:local` image, applied configmap, rolled out 3 replicas successfully.

### Task 2: E2E Verification (All 6 Checks Passed)

| Check | Query / Method | Result |
|-------|---------------|--------|
| 1. Heartbeat metric | `snmp_gauge{metric_name="simetraHeartbeat"}` | Value = 1, host_name = docker-desktop |
| 2. Pipeline metrics | `snmp_event_published_total`, `snmp_trap_received_total`, `snmp_event_handled_total` | All > 0, host_name present |
| 3. Runtime metrics | `dotnet_gc_collections_total` | host_name = docker-desktop |
| 4. Elasticsearch logs | `curl localhost:9200/simetra-logs*/_search` | Attributes.host_name = docker-desktop |
| 5. Console heartbeat | `kubectl logs` grep heartbeat | HeartbeatJob firing every 15s |
| 6. host_name consistency | All sources | docker-desktop (K8s node name) |

**Key finding:** Heartbeat dispatches to `snmp_gauge` (not `snmp_info`) because `Integer32(1)` maps to `TypeCode.Integer32` in OtelMetricHandler's switch, which routes to gauge.

## Commits

| Hash | Description |
|------|-------------|
| 4e82181 | fix(quick-016): fix Dockerfile for SnmpCollector and update K8s configmap |

## Verification

All 6 E2E checks passed against live Prometheus and Elasticsearch in Docker Desktop K8s cluster.
