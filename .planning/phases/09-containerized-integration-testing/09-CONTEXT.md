# Phase 9: Containerized Integration Testing - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Verify the full SnmpCollector pipeline runs correctly in a containerized K8s environment using Docker Desktop's built-in Kubernetes. Deploy SnmpCollector (3 replicas) + observability stack in the simetra namespace. Remove Simetra pods and simulators. Validate runtime metrics flow end-to-end and leader election works. No SNMP device traffic in this phase — runtime metrics only.

</domain>

<decisions>
## Implementation Decisions

### Namespace Cleanup
- Keep observability stack in simetra namespace: OTel Collector, Prometheus, Grafana, Elasticsearch, Kibana
- Remove Simetra pods (deployment.yaml references simetra:local image)
- Keep simulator manifests in repo (deploy/k8s/simulators/) but do NOT apply them — reference only
- SnmpCollector deploys into the simetra namespace alongside the observability stack

### K8s Manifests
- Existing K8s manifests live at deploy/k8s/ — already have namespace, RBAC, serviceaccount, monitoring stack
- Adapt the existing Simetra deployment.yaml as template for SnmpCollector deployment (change image, ports, config, keep probe structure and resource limits)
- Create new subdirectory deploy/k8s/snmp-collector/ for SnmpCollector-specific manifests (deployment, configmap, service)
- Keep existing deploy/k8s/ Simetra manifests intact for reference

### Test Scenarios — Runtime Metrics Only
- Primary goal: Full pipeline E2E for runtime/pipeline metrics (not SNMP business metrics)
- Verify: SnmpCollector starts, health probes pass, pipeline metrics (snmp.event.*, snmp.poll.*, snmp.trap.*) and System.Runtime metrics flow through OTel Collector to Prometheus
- 3 replicas with K8sLeaseElection — verify leader election works, only leader exports business metrics, all 3 export pipeline/runtime metrics
- Include leader failover test: kubectl delete pod <leader>, verify another pod acquires lease and resumes metric export
- Validation: Manual — query actual metrics in Prometheus directly, no scripted checks

### Deployment Topology
- Docker Desktop built-in Kubernetes (single-node cluster)
- Local docker build with imagePullPolicy: Never — same pattern as existing Simetra deployment
- Dockerfile from Phase 8 (08-04) — Claude reviews and adjusts if needed for K8s environment

### Claude's Discretion
- Dockerfile adjustments (if any needed for K8s)
- ConfigMap structure for SnmpCollector appsettings in K8s
- Exact resource requests/limits for SnmpCollector pods
- Service type and port configuration
- OTel Collector config adjustments (if needed to receive from SnmpCollector)

</decisions>

<specifics>
## Specific Ideas

- Existing Simetra deployment.yaml has health probes on port 8080 (startup/ready/live) — SnmpCollector uses same probe paths (/healthz/startup, /healthz/ready, /healthz/live)
- Simetra uses `appsettings.Production.json` mounted from ConfigMap — same pattern for SnmpCollector
- Pod identity injected via `metadata.name` fieldRef into `Site__PodIdentity` env var — keep this pattern
- Monitoring stack manifests already exist at deploy/k8s/monitoring/ (OTel Collector, Prometheus, Elasticsearch)

</specifics>

<deferred>
## Deferred Ideas

- SNMP device simulation (snmpsim or custom mock) for full E2E with snmp_gauge/snmp_counter/snmp_info validation — future phase
- Scripted/automated test validation (Prometheus API assertions) — future phase
- Grafana dashboard provisioning for SnmpCollector — future phase

</deferred>

---

*Phase: 09-containerized-integration-testing*
*Context gathered: 2026-03-05*
