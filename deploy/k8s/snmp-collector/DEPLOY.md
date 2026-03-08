# SnmpCollector K8s Deployment and Validation Guide

## Prerequisites

- Docker Desktop with Kubernetes enabled and running
- `kubectl` configured for the `docker-desktop` context

Confirm context:

```bash
kubectl config current-context
```

Expected output: `docker-desktop`

---

## Step 1: Remove Simetra deployment (if exists)

```bash
kubectl delete deployment simetra -n simetra 2>/dev/null || true
```

---

## Step 2: Apply/update monitoring stack

```bash
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/serviceaccount.yaml
kubectl apply -f deploy/k8s/rbac.yaml
kubectl apply -f deploy/k8s/monitoring/otel-collector-configmap.yaml
kubectl apply -f deploy/k8s/monitoring/otel-collector-deployment.yaml
kubectl apply -f deploy/k8s/monitoring/prometheus-configmap.yaml
kubectl apply -f deploy/k8s/monitoring/prometheus-deployment.yaml
kubectl apply -f deploy/k8s/monitoring/elasticsearch-deployment.yaml
```

Restart monitoring pods to pick up config changes:

```bash
kubectl rollout restart deployment/otel-collector -n simetra
kubectl rollout restart deployment/prometheus -n simetra
```

Wait for rollout:

```bash
kubectl rollout status deployment/otel-collector -n simetra
kubectl rollout status deployment/prometheus -n simetra
```

---

## Step 3: Build SnmpCollector Docker image

Run from repo root (build context requires `src/SnmpCollector/`):

```bash
docker build -f src/SnmpCollector/Dockerfile -t snmp-collector:local .
```

---

## Step 4: Deploy SnmpCollector

```bash
kubectl apply -f deploy/k8s/snmp-collector/snmp-collector-config.yaml
kubectl apply -f deploy/k8s/snmp-collector/simetra-oidmaps.yaml
kubectl apply -f deploy/k8s/snmp-collector/simetra-devices.yaml
kubectl apply -f deploy/k8s/snmp-collector/deployment.yaml
kubectl apply -f deploy/k8s/snmp-collector/service.yaml
```

---

## Step 5: Watch pods start

```bash
kubectl get pods -n simetra -w
```

Expected: 3 `snmp-collector-*` pods reach `Running` with `READY 1/1`.

Press Ctrl+C once all 3 are ready.

---

## Step 6: Verify health probes

```bash
kubectl get pods -n simetra -l app=snmp-collector
```

All 3 pods must show `READY 1/1` and `STATUS Running`.

---

## Step 7: Check logs

```bash
kubectl logs -l app=snmp-collector -n simetra --tail=50
```

Look for:

- Structured JSON log lines on startup
- `"Acquired leadership"` on exactly one pod
- Correlation ID rotation log entries

---

## Step 8: Verify leader election

```bash
kubectl get lease -n simetra
kubectl get lease snmp-collector-leader -n simetra -o jsonpath='{.spec.holderIdentity}'
```

Exactly one pod name must appear as `holderIdentity`.

---

## Step 9: Port-forward Prometheus and validate metrics

```bash
kubectl port-forward svc/prometheus 9090:9090 -n simetra
```

Open http://localhost:9090 in a browser and run these PromQL queries.

**Runtime metrics — must show 3 instances (one per pod):**

```promql
process_runtime_dotnet_gc_collections_count_total
```

**Pipeline metrics — must show entries from all 3 pods:**

```promql
snmp_event_published_total
```

Both queries must return results with 3 distinct `service_instance_id` label values.

---

## Step 10: Leader failover test

Find the current leader:

```bash
kubectl get lease snmp-collector-leader -n simetra -o jsonpath='{.spec.holderIdentity}'
```

Delete the leader pod (replace `<leader-pod-name>` with the name from the command above):

```bash
kubectl delete pod <leader-pod-name> -n simetra
```

Watch for a new leader (should acquire within ~15 seconds):

```bash
kubectl get lease snmp-collector-leader -n simetra -w
```

Confirm new holder differs from the deleted pod:

```bash
kubectl get lease snmp-collector-leader -n simetra -o jsonpath='{.spec.holderIdentity}'
```

---

## Teardown (optional)

Remove SnmpCollector resources only:

```bash
kubectl delete -f deploy/k8s/snmp-collector/
```
