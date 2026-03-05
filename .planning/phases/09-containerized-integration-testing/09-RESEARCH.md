# Phase 9: Containerized Integration Testing - Research

**Researched:** 2026-03-05
**Domain:** Kubernetes deployment of .NET 9 application — manifests, ConfigMap wiring, OTel Collector config, health probe behavior, leader election configuration
**Confidence:** HIGH (findings are based on direct code inspection of the actual codebase)

## Summary

Phase 9 deploys SnmpCollector (3 replicas) into the existing `simetra` K8s namespace alongside the already-deployed observability stack (OTel Collector, Prometheus, Grafana, Elasticsearch). No code changes to SnmpCollector are required; this phase is entirely about K8s manifests and configuration correctness.

Research revealed three blocking issues that must be resolved before the deployment will work correctly: (1) the existing `deploy/k8s/monitoring/` OTel Collector ConfigMap uses the `prometheus` scrape exporter instead of the `prometheusremotewrite` exporter that the project mandates — this must be fixed as part of Phase 9; (2) `ReadinessHealthCheck` requires `DeviceChannelManager.DeviceNames.Count > 0` which will always fail when no devices are configured — a dummy device must be added to the ConfigMap to unblock readiness; (3) `LeaseOptions.Namespace` defaults to `"default"` but the RBAC grants lease access to the `simetra` namespace only — the ConfigMap must explicitly set `Lease.Namespace: simetra`.

The Dockerfile at `src/SnmpCollector/Dockerfile` is complete and correct for K8s deployment. The existing `deploy/k8s/` Simetra manifests (namespace, RBAC, ServiceAccount, service structure) are reusable directly as templates for SnmpCollector manifests placed in a new `deploy/k8s/snmp-collector/` subdirectory.

**Primary recommendation:** Fix the OTel Collector ConfigMap to use `prometheusremotewrite` + `--web.enable-remote-write-receiver` on Prometheus before deploying SnmpCollector — without this, no metrics reach Prometheus from any source.

## Standard Stack

### Core (already in use — no new installs needed)

| Tool | Version | Purpose | Notes |
|------|---------|---------|-------|
| Docker Desktop K8s | built-in | Single-node cluster | `imagePullPolicy: Never` works because K8s shares Docker daemon with host |
| kubectl | cluster version | Apply manifests, inspect pods | Standard CLI |
| `otel/opentelemetry-collector-contrib` | 0.120.0 | OTLP gRPC receiver + prometheusremotewrite exporter | Already in `deploy/k8s/monitoring/otel-collector-deployment.yaml` |
| `prom/prometheus` | v3.2.1 | Metrics storage | Already in `deploy/k8s/monitoring/prometheus-deployment.yaml` |
| `mcr.microsoft.com/dotnet/aspnet` | 9.0-bookworm-slim | SnmpCollector runtime image | Already in `src/SnmpCollector/Dockerfile` |

### Supporting

| Tool | Version | Purpose | Notes |
|------|---------|---------|-------|
| `grafana/grafana` | 11.6.11 | Dashboard UI | In `deploy/k8s/production/grafana.yaml` — deferred for this phase |
| `docker.elastic.co/elasticsearch/elasticsearch` | 8.17.3 | Log storage | In `deploy/k8s/monitoring/elasticsearch-deployment.yaml` |

**No new packages or installs required.** Phase 9 is manifest creation and configuration wiring only.

## Architecture Patterns

### Recommended Directory Structure

```
deploy/k8s/
├── namespace.yaml              # EXISTS — simetra namespace
├── rbac.yaml                   # EXISTS — lease role/binding for simetra-sa
├── serviceaccount.yaml         # EXISTS — simetra-sa
├── configmap.yaml              # EXISTS — Simetra config (keep, don't modify)
├── deployment.yaml             # EXISTS — Simetra deployment (keep, don't modify)
├── service.yaml                # EXISTS — Simetra service (keep, don't modify)
├── monitoring/
│   ├── otel-collector-configmap.yaml   # MUST FIX — change to prometheusremotewrite
│   ├── otel-collector-deployment.yaml  # EXISTS — no changes needed
│   ├── prometheus-configmap.yaml       # MUST FIX — add --web.enable-remote-write-receiver
│   ├── prometheus-deployment.yaml      # MUST FIX — add args for remote-write-receiver flag
│   └── elasticsearch-deployment.yaml   # EXISTS — no changes needed
├── simulators/                 # EXISTS — keep in repo, do NOT apply
└── snmp-collector/             # NEW — Phase 9 creates this directory
    ├── configmap.yaml          # NEW — SnmpCollector K8s appsettings
    ├── deployment.yaml         # NEW — 3 replicas, imagePullPolicy: Never
    └── service.yaml            # NEW — ClusterIP, port 8080 health only
```

### Pattern 1: ConfigMap-Mounted appsettings.Production.json

Mount the ConfigMap key as `/app/appsettings.Production.json`. .NET configuration merges this over `appsettings.json` at runtime. The Simetra deployment does exactly this — reuse the pattern.

```yaml
# In Deployment spec.containers[].volumeMounts:
- name: config
  mountPath: /app/appsettings.Production.json
  subPath: appsettings.k8s.json
  readOnly: true

# In Deployment spec.volumes:
- name: config
  configMap:
    name: snmp-collector-config
```

The ConfigMap data key is `appsettings.k8s.json` (the `subPath` determines the mounted filename).

### Pattern 2: Pod Identity via Downward API

Inject the pod name into `Site__PodIdentity` so `K8sLeaseElection` uses the pod name as the lease holder identity (not `Environment.MachineName`). This is already in the Simetra deployment and must be replicated exactly:

```yaml
env:
- name: ASPNETCORE_ENVIRONMENT
  value: Production
- name: Site__PodIdentity
  valueFrom:
    fieldRef:
      fieldPath: metadata.name
```

`K8sLeaseElection` reads `_siteOptions.PodIdentity` (which `PostConfigure` sets from `HOSTNAME` env var, but `Site__PodIdentity` overrides it explicitly via configuration — see `ServiceCollectionExtensions.cs` PostConfigure).

Note: The code reads `PodIdentity` from `SiteOptions` which comes from the `Site:PodIdentity` config key. The env var `Site__PodIdentity` (double underscore = nested key separator in .NET) maps to `Site:PodIdentity` in the configuration system. The PostConfigure in `AddSnmpConfiguration` only sets it if null (`??=`), so the env var override takes precedence.

### Pattern 3: Docker Desktop imagePullPolicy: Never

Docker Desktop K8s shares the Docker daemon with the host machine. Build the image with `docker build` locally, then reference it with `imagePullPolicy: Never`. Kubernetes will find the image in the local Docker cache without any registry push.

```bash
# Build command (run from repo root):
docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local .

# The Dockerfile COPY paths are relative to repo root:
# COPY src/SnmpCollector/SnmpCollector.csproj src/SnmpCollector/
# COPY src/SnmpCollector/. src/SnmpCollector/
# Therefore: build context MUST be repo root, not src/SnmpCollector/
```

The existing Simetra Dockerfile uses the same pattern (`simetra:local`, built from repo root context). The SnmpCollector Dockerfile at `src/SnmpCollector/Dockerfile` follows the same pattern.

### Pattern 4: OTel Collector Config — prometheusremotewrite (REQUIRED FIX)

The existing `deploy/k8s/monitoring/otel-collector-configmap.yaml` uses the `prometheus` scrape exporter (port 8889). This contradicts the project decision mandating `prometheusremotewrite` (PUSH-03). The K8s OTel Collector config must match the Docker Compose config at `deploy/otel-collector-config.yaml`.

The correct K8s OTel Collector config:
```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

exporters:
  prometheusremotewrite:
    endpoint: http://prometheus:9090/api/v1/write
    resource_to_telemetry_conversion:
      enabled: true

  elasticsearch:
    endpoints:
      - http://elasticsearch:9200
    logs_index: simetra-logs
    sending_queue:
      enabled: true
    flush:
      bytes: 1024
      interval: 1s

service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [prometheusremotewrite]
    logs:
      receivers: [otlp]
      exporters: [elasticsearch]
```

### Pattern 5: Prometheus Must Have --web.enable-remote-write-receiver (REQUIRED FIX)

The existing `deploy/k8s/monitoring/prometheus-deployment.yaml` does NOT include the `--web.enable-remote-write-receiver` flag. Without this flag, Prometheus returns HTTP 405 on all remote_write POSTs, silently rejecting all metrics. This must be added to the Prometheus deployment's `args`:

```yaml
args:
  - "--config.file=/etc/prometheus/prometheus.yml"
  - "--web.enable-remote-write-receiver"
```

The prometheus ConfigMap (`prometheus-configmap.yaml`) also needs updating: remove the `scrape_configs` section (OTel Collector no longer exposes port 8889 as a scrape target). Replace with no scrape_configs — pure remote_write target.

### Pattern 6: Lease Namespace Must Be simetra (CRITICAL CONFIG)

`LeaseOptions.Namespace` defaults to `"default"` in `appsettings.json`. The RBAC in `deploy/k8s/rbac.yaml` grants lease permissions to `simetra-sa` in the `simetra` namespace only. If the app tries to create/update a Lease in `"default"`, the Kubernetes API will return 403 Forbidden and leader election will fail permanently.

The ConfigMap MUST override this:
```json
"Lease": {
  "Name": "snmp-collector-leader",
  "Namespace": "simetra",
  "DurationSeconds": 15,
  "RenewIntervalSeconds": 10
}
```

### Pattern 7: Dummy Device Required for Readiness Probe

`ReadinessHealthCheck` (`HealthChecks/ReadinessHealthCheck.cs`) checks `_channels.DeviceNames.Count > 0`. `DeviceChannelManager` creates channels from `IDeviceRegistry.AllDevices`. With an empty `Devices: []` ConfigMap, zero channels are created and readiness fails permanently.

To unblock readiness in Phase 9 (no real devices), add a single dummy device to the ConfigMap:
```json
"Devices": [
  {
    "Name": "dummy-device-01",
    "IpAddress": "127.0.0.1",
    "MetricPolls": []
  }
]
```

A device with an empty `MetricPolls` array registers a channel (satisfying readiness) but schedules zero poll jobs. The `DeviceUnreachabilityTracker` and `CardinalityAuditService` handle this gracefully. The trap listener won't accept traps from `127.0.0.1` (not a real source), and no poll jobs run. This is the minimum config to satisfy the existing health check logic.

**Alternative considered:** Do NOT modify `ReadinessHealthCheck` to skip the device count check — this changes production behavior and scope creeps Phase 9. The dummy device approach is correct for integration testing.

### Anti-Patterns to Avoid

- **Building Docker image with wrong context:** `docker build -f src/SnmpCollector/Dockerfile .` from `src/SnmpCollector/` fails because COPY paths in the Dockerfile are relative to repo root. Always build from repo root.
- **Leaving `imagePullPolicy: IfNotPresent` in snmp-collector manifests:** Use `Never` for local images. `IfNotPresent` would pull from Docker Hub and fail if image isn't pushed there.
- **Using `prometheus` scrape exporter in OTel Collector config:** It creates a scrape endpoint at port 8889 which Prometheus must then scrape. This contradicts the push-only design and breaks if `--web.enable-remote-write-receiver` is set but scrape config is removed. Use `prometheusremotewrite` always.
- **Keeping `Lease.Namespace: default` in ConfigMap:** RBAC only grants access to the `simetra` namespace. Wrong namespace = 403 Forbidden = leader election never starts = K8sLeaseElection keeps retrying indefinitely.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Leader election debugging | Custom log parser | `kubectl get lease -n simetra` | Shows current lease holder, acquire time, renew time directly |
| Health probe status | curl scripts | `kubectl describe pod <name>` | Shows probe results, last probe time, failure counts in Events section |
| Metric verification | Test code | `kubectl port-forward svc/prometheus 9090:9090` then browser | Direct Prometheus UI query, no code needed |
| Namespace cleanup | Manual delete | `kubectl delete deployment simetra -n simetra` | Single command removes Simetra pods without touching other resources |
| Image rebuild | CI pipeline | `docker build ... && kubectl rollout restart deployment/snmp-collector` | Direct local workflow for Docker Desktop |

**Key insight:** Phase 9 is entirely operational. Use kubectl and browser-based Prometheus UI for all verification. Do not write code to validate the deployment.

## Common Pitfalls

### Pitfall 1: OTel Collector K8s Config Diverges from Docker Compose Config

**What goes wrong:** The K8s `otel-collector-configmap.yaml` uses `prometheus` exporter (scrape, port 8889) while the Docker Compose uses `prometheusremotewrite`. Deploying without fixing this means metrics never reach Prometheus — but Prometheus itself will appear healthy (it starts fine). The symptom is "no data" in Prometheus UI with no visible errors.

**Why it happens:** The K8s manifests were written for the Simetra deployment (which uses scrape), not SnmpCollector (which uses push).

**How to avoid:** Update `otel-collector-configmap.yaml` to use `prometheusremotewrite` exporter with endpoint `http://prometheus:9090/api/v1/write`. Update `prometheus-configmap.yaml` to remove `scrape_configs`. Add `--web.enable-remote-write-receiver` to `prometheus-deployment.yaml` args.

**Warning signs:** Prometheus UI shows no metrics after pods are up for 60+ seconds. OTel Collector logs may show HTTP 405 errors to Prometheus.

### Pitfall 2: Lease Namespace Mismatch Causes Silent Election Failure

**What goes wrong:** With `Lease.Namespace: default` in the app config (the default), `K8sLeaseElection` tries to create/update a Lease in the `default` namespace. The RBAC Role+RoleBinding in `deploy/k8s/rbac.yaml` scopes permissions to the `simetra` namespace. The Kubernetes API returns 403 Forbidden. `K8sLeaseElection.ExecuteAsync` throws continuously, `_isLeader` stays false on all pods, `MetricRoleGatedExporter` gates all business metrics on all pods.

**Why it happens:** `appsettings.json` has `Lease.Namespace: default` as a sensible default, but the K8s environment uses the `simetra` namespace.

**How to avoid:** ConfigMap must include `"Lease": { "Namespace": "simetra", ... }`.

**Warning signs:** Pod logs show repeated Kubernetes API errors from `K8sLeaseElection`. `kubectl get lease -n simetra` shows no lease object. No pod ever logs "Acquired leadership".

### Pitfall 3: Readiness Probe Permanently Fails with Empty Device List

**What goes wrong:** `ReadinessHealthCheck` checks `_channels.DeviceNames.Count > 0`. With `"Devices": []`, `DeviceChannelManager` creates zero channels. The `/healthz/ready` endpoint returns 503 forever. K8s marks all pods NotReady. Pods never receive service traffic.

**Why it happens:** The readiness check was designed to confirm devices are registered (Phase 2 requirement), not to handle the zero-device case.

**How to avoid:** Add one dummy device with empty `MetricPolls` to the ConfigMap (`127.0.0.1`, no poll groups).

**Warning signs:** `kubectl get pods -n simetra` shows all snmp-collector pods as `Running` but not `Ready`. `kubectl describe pod <name>` shows readiness probe failures.

### Pitfall 4: Docker Build Context Wrong

**What goes wrong:** Running `docker build -f src/SnmpCollector/Dockerfile .` from `src/SnmpCollector/` directory fails with "COPY failed: file not found." The Dockerfile uses paths like `COPY src/SnmpCollector/SnmpCollector.csproj src/SnmpCollector/` which require the repo root as build context.

**Why it happens:** The Dockerfile was written to be invoked from the repo root (consistent with the Simetra Dockerfile pattern).

**How to avoid:** Always build from repo root: `docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local .`

**Warning signs:** Docker build fails immediately with path/COPY errors.

### Pitfall 5: Prometheus Deployment Needs args Override

**What goes wrong:** Simply adding `--web.enable-remote-write-receiver` to the args list is correct, but you MUST also include `--config.file=/etc/prometheus/prometheus.yml`. The `args` field in a K8s container spec overrides the Docker image's CMD entirely — if you list only the remote-write flag, Prometheus starts without its config file and fails.

**Why it happens:** Docker's ENTRYPOINT/CMD distinction: `args` in K8s spec overrides CMD, not ENTRYPOINT. The Prometheus image uses CMD for its flags, so all flags must be explicit in `args`.

**How to avoid:** See the `deploy/k8s/production/prometheus.yaml` which correctly includes both flags. The dev `prometheus-deployment.yaml` must be updated to match.

**Warning signs:** Prometheus pod crashes immediately. Logs show "cannot parse flag" or "configuration file missing."

### Pitfall 6: SNMP Port Not Needed in SnmpCollector Service

**What goes wrong:** Copying the Simetra service.yaml creates a service with both port 8080 (health) and port 162/10162 (SNMP). Phase 9 has no real SNMP devices sending traps, and exposing SNMP port adds unnecessary complexity (UDP NodePort is complex on Docker Desktop).

**How to avoid:** Create the SnmpCollector service with only port 8080 (ClusterIP for health). The SNMP UDP port is not needed when there are no trap senders.

### Pitfall 7: Leader Election Identifies by PodIdentity, Not Service

**What goes wrong:** If `Site__PodIdentity` is not injected via Downward API, all 3 pods may use the same identity (e.g., `Environment.MachineName` from inside the container, which is the pod name but only if HOSTNAME is set — in K8s, HOSTNAME env var IS the pod name by default). However, the explicit Downward API injection is more reliable and matches the Simetra pattern.

**How to avoid:** Include the Downward API fieldRef for `metadata.name` injected into `Site__PodIdentity` exactly as in the Simetra deployment. If all pods report the same identity, only one can hold the lease — the others will fail to acquire and log "conflict" errors.

## Code Examples

### SnmpCollector ConfigMap (deploy/k8s/snmp-collector/configmap.yaml)

```yaml
# Source: Adapted from deploy/k8s/configmap.yaml and deploy/k8s/production/configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: snmp-collector-config
  namespace: simetra
data:
  appsettings.k8s.json: |
    {
      "Site": {
        "Name": "site-lab-k8s"
      },
      "Otlp": {
        "Endpoint": "http://otel-collector:4317",
        "ServiceName": "snmp-collector"
      },
      "SnmpListener": {
        "BindAddress": "0.0.0.0",
        "Port": 10162,
        "CommunityString": "public"
      },
      "Lease": {
        "Name": "snmp-collector-leader",
        "Namespace": "simetra",
        "DurationSeconds": 15,
        "RenewIntervalSeconds": 10
      },
      "CorrelationJob": {
        "IntervalSeconds": 30
      },
      "Liveness": {
        "GraceMultiplier": 2.0
      },
      "Channels": {
        "BoundedCapacity": 100
      },
      "Devices": [
        {
          "Name": "dummy-device-01",
          "IpAddress": "127.0.0.1",
          "MetricPolls": []
        }
      ],
      "Logging": {
        "EnableConsole": true
      }
    }
```

### SnmpCollector Deployment (deploy/k8s/snmp-collector/deployment.yaml)

```yaml
# Source: Adapted from deploy/k8s/deployment.yaml
# Changes: image name, replicas=3, remove SNMP port (no devices in Phase 9)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: snmp-collector
  namespace: simetra
  labels:
    app: snmp-collector
spec:
  replicas: 3
  selector:
    matchLabels:
      app: snmp-collector
  template:
    metadata:
      labels:
        app: snmp-collector
    spec:
      serviceAccountName: simetra-sa
      terminationGracePeriodSeconds: 30
      containers:
      - name: snmp-collector
        image: snmp-collector:local
        imagePullPolicy: Never
        ports:
        - containerPort: 8080
          name: health
          protocol: TCP
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: Site__PodIdentity
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        resources:
          requests:
            cpu: 100m
            memory: 128Mi
          limits:
            cpu: 500m
            memory: 256Mi
        volumeMounts:
        - name: config
          mountPath: /app/appsettings.Production.json
          subPath: appsettings.k8s.json
          readOnly: true
        startupProbe:
          httpGet:
            path: /healthz/startup
            port: health
          initialDelaySeconds: 5
          periodSeconds: 3
          failureThreshold: 10
        readinessProbe:
          httpGet:
            path: /healthz/ready
            port: health
          periodSeconds: 10
          failureThreshold: 3
        livenessProbe:
          httpGet:
            path: /healthz/live
            port: health
          periodSeconds: 15
          failureThreshold: 3
          timeoutSeconds: 5
      volumes:
      - name: config
        configMap:
          name: snmp-collector-config
```

### SnmpCollector Service (deploy/k8s/snmp-collector/service.yaml)

```yaml
# Source: Adapted from deploy/k8s/service.yaml
# Changes: Only health port (no SNMP UDP needed — no device traffic in Phase 9)
apiVersion: v1
kind: Service
metadata:
  name: snmp-collector
  namespace: simetra
  labels:
    app: snmp-collector
spec:
  type: ClusterIP
  selector:
    app: snmp-collector
  ports:
  - name: health
    port: 8080
    protocol: TCP
    targetPort: health
```

### Fixed OTel Collector ConfigMap (deploy/k8s/monitoring/otel-collector-configmap.yaml)

```yaml
# Source: deploy/otel-collector-config.yaml (Docker Compose reference — must match)
# CHANGE: prometheus exporter -> prometheusremotewrite exporter
apiVersion: v1
kind: ConfigMap
metadata:
  name: otel-collector-config
  namespace: simetra
data:
  config.yaml: |
    receivers:
      otlp:
        protocols:
          grpc:
            endpoint: 0.0.0.0:4317

    exporters:
      prometheusremotewrite:
        endpoint: http://prometheus:9090/api/v1/write
        resource_to_telemetry_conversion:
          enabled: true

      elasticsearch:
        endpoints:
          - http://elasticsearch:9200
        logs_index: simetra-logs
        sending_queue:
          enabled: true
        flush:
          bytes: 1024
          interval: 1s

    service:
      pipelines:
        metrics:
          receivers: [otlp]
          exporters: [prometheusremotewrite]
        logs:
          receivers: [otlp]
          exporters: [elasticsearch]
```

### Fixed Prometheus ConfigMap + Deployment Args

```yaml
# deploy/k8s/monitoring/prometheus-configmap.yaml
# CHANGE: Remove scrape_configs (no scrape targets with push-only pipeline)
apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-config
  namespace: simetra
data:
  prometheus.yml: |
    global:
      scrape_interval: 5s
      evaluation_interval: 5s
    # No scrape_configs — pure remote_write receiver
```

```yaml
# In prometheus-deployment.yaml container spec, add args:
args:
  - "--config.file=/etc/prometheus/prometheus.yml"
  - "--web.enable-remote-write-receiver"
```

### Docker Build Command

```bash
# Run from repo root (C:\Users\UserL\source\repos\Simetra117\):
docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local .
```

### Deployment Commands

```bash
# 1. Remove Simetra deployment (keep namespace/RBAC/SA/monitoring):
kubectl delete deployment simetra -n simetra 2>/dev/null || true

# 2. Apply updated monitoring stack (OTel Collector + Prometheus fixes):
kubectl apply -f deploy/k8s/monitoring/otel-collector-configmap.yaml
kubectl apply -f deploy/k8s/monitoring/prometheus-configmap.yaml
kubectl apply -f deploy/k8s/monitoring/prometheus-deployment.yaml

# 3. Restart OTel Collector to pick up new config:
kubectl rollout restart deployment/otel-collector -n simetra

# 4. Build SnmpCollector image:
docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local .

# 5. Apply SnmpCollector manifests:
kubectl apply -f deploy/k8s/snmp-collector/configmap.yaml
kubectl apply -f deploy/k8s/snmp-collector/deployment.yaml
kubectl apply -f deploy/k8s/snmp-collector/service.yaml

# 6. Watch pods come up:
kubectl get pods -n simetra -w

# 7. Verify lease (wait ~30s after pods ready):
kubectl get lease -n simetra

# 8. Access Prometheus:
kubectl port-forward svc/prometheus 9090:9090 -n simetra
# Then: http://localhost:9090
```

### Prometheus Queries for Validation

```promql
# Runtime metrics — all 3 pods should export:
process_runtime_dotnet_gc_collections_count_total

# Pipeline metrics — all 3 pods should export:
snmp_event_published_total

# Leader election: only 1 pod should be leader at any time
# Check via logs: kubectl logs -l app=snmp-collector -n simetra | grep "Acquired leadership"
```

### Leader Failover Test

```bash
# 1. Find current leader:
kubectl get lease snmp-collector-leader -n simetra -o jsonpath='{.spec.holderIdentity}'

# 2. Delete the leader pod:
kubectl delete pod <leader-pod-name> -n simetra

# 3. Watch for new leader:
kubectl get lease snmp-collector-leader -n simetra -w
# Should update holderIdentity within ~15s (DurationSeconds TTL)

# 4. Check pod logs for "Acquired leadership" on the new leader
kubectl logs -l app=snmp-collector -n simetra --since=60s | grep -i "leader"
```

## State of the Art

| Old Approach (existing K8s manifests) | Required Approach | Impact |
|---------------------------------------|-------------------|--------|
| `prometheus` exporter (scrape) on OTel Collector — port 8889 | `prometheusremotewrite` exporter — pushes to `http://prometheus:9090/api/v1/write` | Prometheus receives metrics via push, no scrape config needed |
| No `--web.enable-remote-write-receiver` on Prometheus | Add flag to Prometheus deployment args | Required for remote_write pushes to succeed |
| `Lease.Namespace: default` (app default) | `Lease.Namespace: simetra` in ConfigMap | RBAC scoped to `simetra`; default would 403 |
| `Devices: []` (empty) | Dummy device with `MetricPolls: []` | Satisfies `ReadinessHealthCheck` |

**What already works correctly (no changes needed):**
- `deploy/k8s/namespace.yaml` — `simetra` namespace, correct
- `deploy/k8s/rbac.yaml` — lease RBAC for `simetra-sa` in `simetra` namespace, correct
- `deploy/k8s/serviceaccount.yaml` — `simetra-sa`, correct (SnmpCollector reuses it)
- `src/SnmpCollector/Dockerfile` — multi-stage build, aspnet:9.0 runtime, non-root user, correct
- `deploy/k8s/monitoring/otel-collector-deployment.yaml` — image `otel/opentelemetry-collector-contrib:0.120.0`, correct
- `deploy/k8s/monitoring/elasticsearch-deployment.yaml` — no changes needed

## Open Questions

1. **Prometheus NodePort for external access**
   - What we know: Production manifests have `service-nodeports.yaml` with Prometheus NodePort at 30090. Dev manifests (`deploy/k8s/monitoring/`) do not have NodePort services.
   - What's unclear: Whether to use `kubectl port-forward` or create NodePort services for Prometheus access during Phase 9 testing.
   - Recommendation: Use `kubectl port-forward svc/prometheus 9090:9090 -n simetra` for simplicity. NodePort creation is optional.

2. **OTel Collector restart required after ConfigMap update**
   - What we know: K8s does not automatically restart pods when a ConfigMap changes. The OTel Collector reads its config at startup.
   - What's unclear: Whether `kubectl rollout restart` is sufficient or if pods need manual delete.
   - Recommendation: `kubectl rollout restart deployment/otel-collector -n simetra` is the standard approach and is sufficient.

3. **Startup probe behavior with 0 Quartz jobs (dummy device, no MetricPolls)**
   - What we know: `StartupHealthCheck` checks `_intervals.TryGetInterval("correlation", out _)` — the CorrelationJob is always registered regardless of device count. Startup should succeed.
   - What's unclear: Whether `CardinalityAuditService` has any issue with 1 device + 0 OIDs.
   - Recommendation: 1 device with 0 MetricPolls produces 0 OID cardinality. `CardinalityAuditService` warning threshold is 10,000 — this will pass silently. No issue expected.

## Sources

### Primary (HIGH confidence)

- Direct code inspection: `src/SnmpCollector/HealthChecks/ReadinessHealthCheck.cs` — verified `DeviceNames.Count > 0` requirement
- Direct code inspection: `src/SnmpCollector/Pipeline/DeviceChannelManager.cs` — verified channels created from `IDeviceRegistry.AllDevices`
- Direct code inspection: `src/SnmpCollector/Configuration/LeaseOptions.cs` — verified `Namespace` defaults to `"default"`
- Direct code inspection: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — verified K8s detection (`IsInCluster()`), LeaseOptions binding, PostConfigure for PodIdentity
- Direct code inspection: `deploy/k8s/monitoring/otel-collector-configmap.yaml` — verified uses `prometheus` scrape exporter (needs fix)
- Direct code inspection: `deploy/k8s/monitoring/prometheus-deployment.yaml` — verified no `--web.enable-remote-write-receiver` flag (needs fix)
- Direct code inspection: `deploy/otel-collector-config.yaml` (Docker Compose) — verified uses `prometheusremotewrite` (the correct pattern)
- Direct code inspection: `deploy/k8s/deployment.yaml` (Simetra) — verified full deployment pattern to replicate for SnmpCollector
- Direct code inspection: `src/SnmpCollector/Dockerfile` — verified correct for K8s, aspnet:9.0, non-root, COPY paths require repo-root build context
- `.planning/STATE.md` decisions log — verified all prior decisions about prometheusremotewrite, resource_to_telemetry_conversion, remote-write-receiver flag

### Secondary (MEDIUM confidence)

- WebSearch: Docker Desktop K8s `imagePullPolicy: Never` — multiple sources confirm Docker Desktop shares Docker daemon with host, local images work with `Never`
- WebSearch: K8s lease namespace best practice — multiple 2025 sources confirm lease namespace should match pod namespace

### Tertiary (LOW confidence)

- WebSearch: OTel Collector prometheusremotewrite vs prometheus exporter — community sources; verified against project's own Docker Compose config which uses prometheusremotewrite

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all tools already in use, versions already pinned in existing manifests
- Architecture: HIGH — patterns derived from direct inspection of existing working manifests + code
- Pitfalls: HIGH — pitfalls 1-5 derived from direct code inspection; pitfall 6-7 from direct manifest inspection
- ConfigMap content: HIGH — all required config keys verified against `LeaseOptions.cs`, `SiteOptions.cs`, `OtlpOptions.cs`, etc.

**Research date:** 2026-03-05
**Valid until:** 2026-04-05 (stable — OTel SDK and K8s API unlikely to change materially in 30 days)
