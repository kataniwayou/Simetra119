# SnmpCollector Offline Deployment Guide

## Overview

This guide deploys the SnmpCollector system on an offline prod machine running minikube.
The develop machine builds Docker images, exports them as tar files, and transfers them
to the prod machine via RDP.

**Components deployed in minikube:**
- OTel Collector (receives metrics/logs from snmp-collector, exports to external infrastructure)
- SnmpCollector (3 replicas with leader election)

**Provided by your organization (not deployed here):**
- Prometheus
- Grafana
- Elasticsearch / Kibana

> The OTel Collector export addresses are configured in `deploy/otel-collector-configmap.yaml`.
> Update the `prometheusremotewrite` and `elasticsearch` endpoints to match your organization's infrastructure.

---

## Part 1: Build Images (Develop Machine)

### 1.1 Build SnmpCollector image

From the `ship/` folder root:

```bash
docker build -t snmp-collector:v2.6 .
```

### 1.2 Save SnmpCollector image to tar

```bash
docker save snmp-collector:v2.6 -o snmp-collector-v2.6.tar
```

> The OTel Collector image is pre-saved in the ship folder as `otel-collector-0.120.0.tar`.

### 1.3 Transfer to prod machine

Copy the entire `ship/` folder to the prod machine via RDP. It contains everything needed:
- `snmp-collector-v2.6.tar` (built in step 1.2)
- `otel-collector-0.120.0.tar` (pre-saved)
- `deploy/` folder (all yaml files)

---

## Part 2: Deploy (Prod Machine - minikube)

### 2.1 Load images into minikube

```bash
minikube image load snmp-collector-v2.6.tar
minikube image load otel-collector-0.120.0.tar
```

Verify:

```bash
minikube image ls | grep -E "snmp-collector|otel"
```

### 2.2 Create namespace and service account

```bash
kubectl create namespace simetra
kubectl -n simetra create serviceaccount simetra-sa
```

### 2.3 Apply RBAC

```bash
kubectl apply -f deploy/rbac.yaml
```

### 2.4 Deploy OTel Collector

```bash
kubectl apply -f deploy/otel-collector-configmap.yaml
kubectl apply -f deploy/otel-collector-deployment.yaml
```

Wait for OTel Collector to be ready:

```bash
kubectl -n simetra rollout status deployment/otel-collector
```

### 2.5 Apply SnmpCollector ConfigMaps

```bash
kubectl apply -f deploy/snmp-collector-config.yaml
kubectl apply -f deploy/simetra-oid-metric-map.yaml
kubectl apply -f deploy/simetra-oid-command-map.yaml
kubectl apply -f deploy/simetra-devices.yaml
kubectl apply -f deploy/simetra-tenants.yaml
```

### 2.6 Update image tag in deployment.yaml

Edit `deploy/deployment.yaml` and change:

```yaml
image: snmp-collector:local
```

to:

```yaml
image: snmp-collector:v2.6
```

`imagePullPolicy: Never` must remain (no registry available).

### 2.7 Deploy SnmpCollector

```bash
kubectl apply -f deploy/service.yaml
kubectl apply -f deploy/deployment.yaml
```

---

## Part 3: Verify

### 3.1 Check pods

```bash
kubectl -n simetra get pods
```

Expected: 1 `otel-collector` pod and 3 `snmp-collector` pods, all `Running` with `READY 1/1`.

### 3.2 Check startup logs

```bash
kubectl -n simetra logs -l app=snmp-collector --tail=5 | grep "Startup sequence"
```

Expected: all pods show devices > 0, e.g.:

```
Startup sequence: OidMapWatcher=145 -> DeviceWatcher=3 -> CommandMapWatcher=13 -> TenantWatcher=4
```

### 3.3 Verify leader election

```bash
kubectl -n simetra get lease snmp-collector-leader -o jsonpath='{.spec.holderIdentity}'
```

Exactly one pod name must appear.

### 3.4 Verify poll jobs are firing

```bash
kubectl -n simetra logs -l app=snmp-collector --tail=50 | grep "metric-poll"
```

Must show poll job execution lines.

---

## Teardown

Remove all SnmpCollector resources:

```bash
kubectl delete deployment snmp-collector -n simetra
kubectl delete service snmp-collector -n simetra
kubectl delete configmap snmp-collector-config simetra-oid-metric-map simetra-oid-command-map simetra-devices simetra-tenants -n simetra
```

Remove OTel Collector:

```bash
kubectl delete deployment otel-collector -n simetra
kubectl delete service otel-collector -n simetra
kubectl delete configmap otel-collector-config -n simetra
```

Remove namespace (removes everything):

```bash
kubectl delete namespace simetra
```
