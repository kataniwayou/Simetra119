---
phase: quick-016
plan: 01
type: execute
wave: 1
depends_on: []
files_modified: []
autonomous: false

must_haves:
  truths:
    - "simetraHeartbeat metric visible in Prometheus with host_name label"
    - "Pipeline metrics (snmp.event.*, snmp.trap.*, snmp.poll.*) visible in Prometheus with host_name label"
    - "Runtime metrics (dotnet_*) visible in Prometheus with host_name label"
    - "Logs appear in Elasticsearch with host_name attribute"
    - "Console logs show heartbeat trap activity"
  artifacts: []
  key_links:
    - from: "HeartbeatJob"
      to: "SNMP trap listener (loopback)"
      via: "UDP trap to 127.0.0.1:10162"
    - from: "OTel Collector transform processor"
      to: "Prometheus remote-write"
      via: "host_name attribute injection from service.instance.id"
---

<objective>
Build the Docker image, deploy to K8s, and verify end-to-end that heartbeat metrics,
pipeline metrics, runtime metrics, and logs are all flowing through the observability stack
with correct host_name labels.

Purpose: Prove the full telemetry pipeline works in a real K8s environment after adding
HeartbeatJob, liveness vector, and host_name labeling in phases 04-10 and quick tasks 013-015.

Output: Verified running deployment with observable heartbeat and metrics in Prometheus + Elasticsearch.
</objective>

<context>
Key deployment files:
@deploy/k8s/configmap.yaml           — simetra-config with HeartbeatJob + OidMap
@deploy/k8s/deployment.yaml          — simetra deployment (uses simetra-config, imagePullPolicy: Never)
@deploy/k8s/production/otel-collector.yaml — OTel Collector with transform processor for host_name
@Dockerfile                          — Multi-stage build from repo root
</context>

<tasks>

<task type="auto">
  <name>Task 1: Build Docker image and redeploy to K8s</name>
  <files></files>
  <action>
1. Build the Docker image from repo root:
   ```
   docker build -t simetra:local .
   ```

2. Apply the configmap (in case it changed since last deploy):
   ```
   kubectl apply -f deploy/k8s/configmap.yaml
   ```

3. Restart the simetra deployment to pick up the new image and config:
   ```
   kubectl rollout restart deployment/simetra -n simetra
   ```

4. Wait for rollout to complete:
   ```
   kubectl rollout status deployment/simetra -n simetra --timeout=120s
   ```

5. Verify pods are running:
   ```
   kubectl get pods -n simetra -l app=simetra
   ```

6. Tail logs for ~15 seconds to confirm heartbeat activity:
   ```
   kubectl logs -n simetra deployment/simetra --tail=50
   ```
   Look for: HeartbeatJob sending traps, pipeline processing varbinds, OID resolution to "simetraHeartbeat".

7. If pods are in CrashLoopBackOff or error state, capture logs and report the issue.
  </action>
  <verify>
  - `kubectl rollout status deployment/simetra -n simetra` shows "successfully rolled out"
  - At least one pod is Running and Ready
  - Console logs show heartbeat trap messages
  </verify>
  <done>Simetra pods running with latest image, console logs confirm heartbeat activity</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 2: Verify metrics in Prometheus and logs in Elasticsearch</name>
  <what-built>
  Full observability stack: Simetra -> OTel Collector -> Prometheus (metrics) + Elasticsearch (logs).
  HeartbeatJob sends loopback SNMP traps every 15s, which flow through the pipeline and emit metrics.
  The OTel Collector transform processor injects host_name from service.instance.id into all metric datapoints.
  </what-built>
  <how-to-verify>
  **Setup port-forwards (run each in a separate terminal):**
  ```
  kubectl port-forward -n simetra svc/prometheus 9090:9090
  kubectl port-forward -n simetra svc/elasticsearch 9200:9200
  ```

  **1. Heartbeat metric in Prometheus:**
  Open http://localhost:9090 and run these queries:

  Query: `snmp_info{metric_name="simetraHeartbeat"}`
  Expected: At least one result with `host_name` label set to the K8s node name.

  **2. Pipeline metrics in Prometheus:**
  Query: `snmp_event_published_total`
  Expected: Counter > 0, has `host_name` label.

  Query: `snmp_trap_received_total`
  Expected: Counter > 0, has `host_name` label.

  Query: `snmp_poll_executed_total`
  Expected: May be 0 if no real devices, but metric should exist with `host_name` label.

  **3. Runtime metrics in Prometheus:**
  Query: `dotnet_gc_collections_total`
  Expected: Results with `host_name` label matching K8s node name.

  **4. Logs in Elasticsearch:**
  ```
  curl -s "http://localhost:9200/simetra-logs*/_search?size=5&sort=@timestamp:desc" | python -m json.tool
  ```
  Expected: Recent log entries. Check that `host_name` attribute is present in the log records.

  **5. Liveness vector (kubectl logs):**
  ```
  kubectl logs -n simetra deployment/simetra --tail=100 | grep -i "heartbeat\|liveness\|stamp"
  ```
  Expected: Evidence that HeartbeatJob is running and liveness vector is being stamped.

  **6. Console heartbeat activity (kubectl logs):**
  ```
  kubectl logs -n simetra deployment/simetra --tail=100 | grep -i "trap\|heartbeat\|varbind"
  ```
  Expected: Lines showing heartbeat trap sent, varbinds processed, OID resolved to simetraHeartbeat.
  </how-to-verify>
  <resume-signal>Type "approved" if all 6 checks pass, or describe which checks failed and what you see</resume-signal>
</task>

</tasks>

<verification>
All 6 verification checks in Task 2 pass:
1. simetraHeartbeat metric exists in Prometheus with host_name
2. Pipeline counters are incrementing
3. dotnet_* runtime metrics have host_name
4. Elasticsearch has recent logs
5. Liveness vector is being stamped
6. Console logs confirm heartbeat flow
</verification>

<success_criteria>
- Docker image builds successfully
- Pods are Running and Ready in K8s
- All metrics visible in Prometheus with host_name label
- Logs flowing to Elasticsearch
- HeartbeatJob proven active via console output and metric presence
</success_criteria>

<output>
After completion, create `.planning/quick/016-e2e-deploy-test-heartbeat-metrics/016-SUMMARY.md`
</output>
