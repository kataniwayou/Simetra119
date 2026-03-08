# Phase 17: Dashboard Provisioning - Research

**Researched:** 2026-03-08
**Domain:** Grafana file-based provisioning on Kubernetes
**Confidence:** HIGH

## Summary

This phase sets up Grafana's file-based provisioning so the Prometheus datasource and a dashboard provider configuration load automatically on Grafana startup. The existing `deploy/grafana/` directory contains stale reference project artifacts that must be deleted and replaced. The current `deploy/k8s/production/grafana.yaml` Deployment has no volume mounts -- it relies entirely on manual UI setup.

The standard approach for non-Helm Grafana deployments on Kubernetes is: (1) create K8s ConfigMaps containing the provisioning YAML files (datasource config, dashboard provider config), (2) create a ConfigMap for dashboard JSON files (empty for now -- Phases 18-19 populate it), (3) mount these ConfigMaps into Grafana's `/etc/grafana/provisioning/` and `/var/lib/grafana/dashboards/` directories, and (4) update the Grafana Deployment with the volume mounts.

**Primary recommendation:** Use three ConfigMaps (datasource provider, dashboard provider, dashboard JSONs) mounted into standard Grafana container paths, with the Grafana Deployment updated to include volumeMounts. Delete all stale files under `deploy/grafana/`.

## Standard Stack

### Core
| Component | Version | Purpose | Why Standard |
|-----------|---------|---------|--------------|
| grafana/grafana | 11.6.11 | Dashboard visualization | Already deployed at this version |
| prom/prometheus | v3.2.1 | Metrics backend | Already deployed, datasource target |
| Kubernetes ConfigMap | v1 | Store provisioning YAML and dashboard JSON | Native K8s, no Helm dependency |

### Supporting
| Component | Purpose | When to Use |
|-----------|---------|-------------|
| kubectl | Manual apply of ConfigMaps and Deployment | Deployment workflow (locked decision: manual kubectl apply) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Raw ConfigMaps | Helm chart (kube-prometheus-stack) | Helm adds sidecar watchers and auto-discovery, but project uses raw manifests -- stay consistent |
| ConfigMap for dashboards | PersistentVolume with JSON files | PV adds storage complexity; ConfigMap is simpler for small dashboard count |
| ConfigMap for dashboards | Grafana API provisioning | API approach requires init containers or jobs; file-based is simpler and declarative |

## Architecture Patterns

### Recommended File Structure
```
deploy/
  k8s/
    production/
      grafana.yaml                    # Deployment (updated with volumeMounts)
      grafana-provisioning.yaml       # NEW: ConfigMaps for datasource + dashboard providers + dashboard JSONs
```

### Pattern 1: Provisioning ConfigMaps in a Single File
**What:** Bundle all Grafana provisioning ConfigMaps into one YAML file separated by `---`, similar to how `prometheus.yaml` bundles its ConfigMap + Deployment + Service.
**When to use:** Always for this project -- keeps provisioning concerns together and follows existing project conventions.
**Example:**
```yaml
# Source: https://grafana.com/docs/grafana/latest/administration/provisioning/
# ConfigMap 1: Datasource provider
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-datasource-provisioning
  namespace: simetra
data:
  simetra-prometheus.yaml: |
    apiVersion: 1
    datasources:
      - name: Simetra Prometheus
        type: prometheus
        access: proxy
        url: http://prometheus:9090
        isDefault: true
        editable: false
        jsonData:
          httpMethod: POST
          timeInterval: 5s
---
# ConfigMap 2: Dashboard provider
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-dashboard-provisioning
  namespace: simetra
data:
  simetra-dashboards.yaml: |
    apiVersion: 1
    providers:
      - name: Simetra
        orgId: 1
        folder: Simetra
        type: file
        disableDeletion: false
        updateIntervalSeconds: 30
        allowUiUpdates: false
        options:
          path: /var/lib/grafana/dashboards
---
# ConfigMap 3: Dashboard JSON files (empty for now -- Phases 18-19 add JSON entries)
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-dashboards
  namespace: simetra
data: {}
```

### Pattern 2: Grafana Deployment Volume Mounts
**What:** Mount the three ConfigMaps into Grafana's standard provisioning paths.
**When to use:** Always -- this is how file-based provisioning works.
**Example:**
```yaml
# Source: https://grafana.com/docs/grafana/latest/administration/provisioning/
# Added to the Grafana Deployment spec.template.spec
containers:
  - name: grafana
    image: grafana/grafana:11.6.11
    volumeMounts:
      - name: datasource-provisioning
        mountPath: /etc/grafana/provisioning/datasources
        readOnly: true
      - name: dashboard-provisioning
        mountPath: /etc/grafana/provisioning/dashboards
        readOnly: true
      - name: dashboards
        mountPath: /var/lib/grafana/dashboards
        readOnly: true
volumes:
  - name: datasource-provisioning
    configMap:
      name: grafana-datasource-provisioning
  - name: dashboard-provisioning
    configMap:
      name: grafana-dashboard-provisioning
  - name: dashboards
    configMap:
      name: grafana-dashboards
```

### Pattern 3: Datasource Configuration
**What:** Prometheus datasource with editable=false (provisioned dashboards are read-only per user decision).
**When to use:** Always for provisioned datasources.
**Key fields:**
- `editable: false` -- prevents UI modifications to provisioned datasource
- `url: http://prometheus:9090` -- K8s internal DNS (both in `simetra` namespace)
- `httpMethod: POST` -- better for large queries
- `timeInterval: 5s` -- matches Prometheus scrape_interval

### Anti-Patterns to Avoid
- **Mounting individual files with subPath:** Do NOT use `subPath` for provisioning files. Grafana reads the entire directory; subPath mounts don't receive ConfigMap updates on rolling restarts. Mount the whole ConfigMap as a directory.
- **Using editable: true on provisioned datasources:** Defeats the purpose of file-based provisioning. UI changes would be lost on pod restart.
- **Putting dashboard JSON inline in the provider YAML:** Dashboard JSON goes in a separate ConfigMap mounted at the `path` specified in the provider. The provider YAML only points to the directory.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Datasource setup | Init container calling Grafana API | Grafana provisioning YAML | Built-in, declarative, restart-safe |
| Dashboard loading | Sidecar container watching ConfigMaps | Grafana file-based provider | Sidecar is Helm-chart territory; file-based is sufficient for manual kubectl apply |
| Dashboard JSON generation | Hand-writing dashboard JSON from scratch | Grafana UI export or programmatic generation | Dashboard JSON is complex; generate in Phases 18-19 |

**Key insight:** Grafana's built-in provisioning system handles everything this phase needs. No custom scripts, sidecars, or API calls required.

## Common Pitfalls

### Pitfall 1: subPath Prevents ConfigMap Updates
**What goes wrong:** Using `subPath` in volumeMount causes the mounted file to be a snapshot -- it does NOT update when the ConfigMap changes.
**Why it happens:** Kubernetes limitation -- subPath mounts are not updated on ConfigMap changes.
**How to avoid:** Mount the entire ConfigMap as a directory (no subPath). Each key in the ConfigMap becomes a file in the directory.
**Warning signs:** Dashboard JSON changes via `kubectl apply` don't appear in Grafana after pod restart.

### Pitfall 2: Missing Provisioning Directories
**What goes wrong:** Grafana fails to start or ignores provisioning if the directory mount replaces a required parent directory.
**Why it happens:** Mounting a volume at `/etc/grafana/provisioning/datasources` replaces the entire directory. If Grafana expects other files in parent directories, they may be lost.
**How to avoid:** Mount each provisioning subdirectory separately (datasources, dashboards). Do NOT mount at `/etc/grafana/provisioning/` as a single volume -- mount at the leaf directories.
**Warning signs:** Grafana logs showing "no provisioning files found" or startup errors.

### Pitfall 3: Dashboard Provider Path Mismatch
**What goes wrong:** Dashboard provider YAML points to a path (e.g., `/var/lib/grafana/dashboards`) but the ConfigMap containing dashboard JSON is mounted at a different path.
**Why it happens:** Copy-paste errors or miscommunication between provisioning config and volume mounts.
**How to avoid:** Ensure the `options.path` in the dashboard provider YAML exactly matches the `mountPath` of the dashboards volume mount.
**Warning signs:** Grafana starts but shows no dashboards, logs show "no dashboard files found in /path".

### Pitfall 4: ConfigMap Size Limit
**What goes wrong:** ConfigMaps have a 1 MiB data limit. Large dashboard JSON files can exceed this.
**Why it happens:** Complex dashboards with many panels can produce large JSON.
**How to avoid:** For this project (2-3 dashboards), unlikely to hit the limit. If dashboards grow large, split into multiple ConfigMaps with multiple volume mounts.
**Warning signs:** `kubectl apply` error: "ConfigMap too large".

### Pitfall 5: Forgetting namespace: simetra
**What goes wrong:** ConfigMaps created in default namespace, Grafana pod in simetra namespace can't see them.
**Why it happens:** Omitting namespace in metadata.
**How to avoid:** Every ConfigMap must have `namespace: simetra` in metadata.
**Warning signs:** Grafana pod fails to start with volume mount errors.

## Code Examples

### Complete Datasource Provisioning YAML
```yaml
# Source: https://grafana.com/docs/grafana/latest/administration/provisioning/
# File: deploy/k8s/production/grafana-provisioning.yaml (part 1)
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-datasource-provisioning
  namespace: simetra
  labels:
    app: grafana
data:
  simetra-prometheus.yaml: |
    apiVersion: 1
    datasources:
      - name: Simetra Prometheus
        type: prometheus
        access: proxy
        url: http://prometheus:9090
        isDefault: true
        editable: false
        jsonData:
          httpMethod: POST
          timeInterval: 5s
```

### Complete Dashboard Provider YAML
```yaml
# Source: https://grafana.com/docs/grafana/latest/administration/provisioning/
# File: deploy/k8s/production/grafana-provisioning.yaml (part 2)
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-dashboard-provisioning
  namespace: simetra
  labels:
    app: grafana
data:
  simetra-dashboards.yaml: |
    apiVersion: 1
    providers:
      - name: Simetra
        orgId: 1
        folder: Simetra
        type: file
        disableDeletion: false
        updateIntervalSeconds: 30
        allowUiUpdates: false
        options:
          path: /var/lib/grafana/dashboards
```

### Updated Grafana Deployment (volumes added)
```yaml
# Source: existing deploy/k8s/production/grafana.yaml + provisioning mounts
apiVersion: apps/v1
kind: Deployment
metadata:
  name: grafana
  namespace: simetra
  labels:
    app: grafana
spec:
  replicas: 1
  selector:
    matchLabels:
      app: grafana
  template:
    metadata:
      labels:
        app: grafana
    spec:
      securityContext:
        fsGroup: 472
        supplementalGroups:
        - 0
      containers:
      - name: grafana
        image: grafana/grafana:11.6.11
        imagePullPolicy: IfNotPresent
        ports:
        - containerPort: 3000
          name: http
          protocol: TCP
        readinessProbe:
          httpGet:
            path: /robots.txt
            port: 3000
          initialDelaySeconds: 10
          periodSeconds: 30
          failureThreshold: 3
          timeoutSeconds: 2
        livenessProbe:
          tcpSocket:
            port: 3000
          initialDelaySeconds: 30
          periodSeconds: 10
          failureThreshold: 3
          timeoutSeconds: 1
        resources:
          requests:
            cpu: 50m
            memory: 64Mi
          limits:
            cpu: 200m
            memory: 256Mi
        volumeMounts:
        - name: datasource-provisioning
          mountPath: /etc/grafana/provisioning/datasources
          readOnly: true
        - name: dashboard-provisioning
          mountPath: /etc/grafana/provisioning/dashboards
          readOnly: true
        - name: dashboards
          mountPath: /var/lib/grafana/dashboards
          readOnly: true
      volumes:
      - name: datasource-provisioning
        configMap:
          name: grafana-datasource-provisioning
      - name: dashboard-provisioning
        configMap:
          name: grafana-dashboard-provisioning
      - name: dashboards
        configMap:
          name: grafana-dashboards
```

### Verification Commands
```bash
# Apply provisioning ConfigMaps
kubectl apply -f deploy/k8s/production/grafana-provisioning.yaml

# Apply updated Grafana Deployment (triggers pod restart)
kubectl apply -f deploy/k8s/production/grafana.yaml

# Verify pod restarts successfully
kubectl get pods -n simetra -l app=grafana -w

# Check Grafana logs for provisioning
kubectl logs -n simetra -l app=grafana | grep -i "provisioning"

# Verify datasource is loaded
kubectl exec -n simetra deploy/grafana -- curl -s http://localhost:3000/api/datasources | python -m json.tool
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual UI datasource setup | File-based provisioning YAML | Grafana 5.0+ (stable since) | Fully declarative, restart-safe |
| Grafana API + init containers | File-based provisioning | Grafana 5.0+ | Simpler, no API auth needed |
| Sidecar ConfigMap watchers | Direct ConfigMap volume mounts | N/A (sidecar is Helm-specific) | Simpler for raw manifest deployments |

**Deprecated/outdated:**
- The `deploy/grafana/` directory in this project: Contains reference project artifacts (npb-device.json, obp-device.json, simetra-operations.json, simetra-prometheus.yaml). All must be deleted as per user decision.

## Open Questions

1. **Grafana folder name for provisioned dashboards**
   - What we know: The dashboard provider `folder` field controls which Grafana folder dashboards appear in. Using "Simetra" groups all project dashboards.
   - What's unclear: Whether the user wants a specific folder name or the default "General" folder.
   - Recommendation: Use `folder: Simetra` to keep dashboards organized. Easy to change later.

2. **Empty dashboards ConfigMap initially**
   - What we know: Phase 17 sets up infrastructure; Phases 18-19 add dashboard JSON.
   - What's unclear: Whether an empty ConfigMap (`data: {}`) causes any Grafana warnings.
   - Recommendation: Use `data: {}` -- Grafana handles empty dashboard directories gracefully (no errors, just no dashboards). Verified by standard provisioning behavior.

## Sources

### Primary (HIGH confidence)
- [Grafana provisioning documentation](https://grafana.com/docs/grafana/latest/administration/provisioning/) - datasource YAML format, dashboard provider format, default paths, environment variable support
- Existing project files: `deploy/k8s/production/grafana.yaml` (current Deployment), `deploy/k8s/production/prometheus.yaml` (ConfigMap+Deployment pattern), `deploy/grafana/provisioning/datasources/simetra-prometheus.yaml` (stale but informative for field values)

### Secondary (MEDIUM confidence)
- [Prometheus: adding a Grafana dashboard using a ConfigMap](https://fabianlee.org/2022/07/06/prometheus-adding-a-grafana-dashboard-using-a-configmap/) - K8s ConfigMap mounting patterns for Grafana
- [Grafana dashboards as ConfigMaps](https://faun.pub/grafana-dashboards-as-configmaps-fbd7d493a2bc) - Community pattern validation

### Tertiary (LOW confidence)
- None -- all findings verified against official docs or existing project code.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Grafana provisioning is well-documented, stable since Grafana 5.0, verified against official docs
- Architecture: HIGH - ConfigMap volume mount pattern is standard K8s, verified against project's existing prometheus.yaml pattern
- Pitfalls: HIGH - subPath limitation is well-documented K8s behavior; other pitfalls verified from official docs and community sources

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable domain, Grafana provisioning API unchanged for years)
