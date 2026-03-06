---
phase: quick-012
plan: 01
type: execute
wave: 1
depends_on: []
files_modified: []
autonomous: false

must_haves:
  truths:
    - "Runtime metrics (dotnet_*) in Prometheus carry host_name label with physical hostname value"
    - "Business metrics (snmp_*) in Prometheus carry host_name label with physical hostname value"
    - "Logs in Elasticsearch carry host_name attribute with physical hostname value"
    - "host_name value matches the K8s node hostname (from spec.nodeName / hostnamectl)"
  artifacts: []
  key_links:
    - from: "PHYSICAL_HOSTNAME env var (K8s Downward API)"
      to: "service.instance.id resource attribute"
      via: "OTel SDK resource detection"
      pattern: "host_name"
    - from: "OTel Collector transform processor"
      to: "Prometheus host_name label"
      via: "set(attributes[\"host_name\"], resource.attributes[\"service.instance.id\"])"
      pattern: "host_name"
---

<objective>
Verify that the PHYSICAL_HOSTNAME (K8s spec.nodeName) flows correctly through the entire observability stack:
OTel SDK -> OTel Collector transform processor -> Prometheus labels and Elasticsearch log attributes.

Purpose: Confirm the rename from NODE_NAME to PHYSICAL_HOSTNAME and the OTel Collector transform processor
correctly propagate the physical hostname to all three metric types (runtime, business, logs).

Output: Verified queries showing host_name label/attribute present with correct value.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@deploy/k8s/production/deployment.yaml
@deploy/k8s/production/otel-collector.yaml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Rebuild and redeploy to K8s</name>
  <files></files>
  <action>
1. Build the Docker image from repo root:
   ```
   docker build -t simetra:local .
   ```
2. Apply the OTel Collector config (which includes the transform processor):
   ```
   kubectl apply -f deploy/k8s/production/otel-collector.yaml -n simetra
   ```
3. Restart the OTel Collector to pick up config changes:
   ```
   kubectl rollout restart deployment/otel-collector -n simetra
   ```
4. Apply the SnmpCollector deployment (which has PHYSICAL_HOSTNAME env var):
   ```
   kubectl apply -f deploy/k8s/production/deployment.yaml -n simetra
   ```
5. Restart SnmpCollector to pick up new image:
   ```
   kubectl rollout restart deployment/simetra -n simetra
   ```
6. Wait for rollouts to complete:
   ```
   kubectl rollout status deployment/otel-collector -n simetra --timeout=120s
   kubectl rollout status deployment/simetra -n simetra --timeout=120s
   ```
7. Tail logs briefly to confirm startup is clean:
   ```
   kubectl logs -n simetra deployment/simetra --tail=20
   ```
  </action>
  <verify>
Both deployments are Running:
```
kubectl get pods -n simetra -l app=simetra
kubectl get pods -n simetra -l app=otel-collector
```
All pods show STATUS=Running, READY=1/1.
  </verify>
  <done>SnmpCollector and OTel Collector are running with latest code and config.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 2: Verify host_name on metrics and logs</name>
  <what-built>
Rebuilt and redeployed SnmpCollector with PHYSICAL_HOSTNAME env var and OTel Collector
with transform processor that copies service.instance.id to host_name datapoint attribute.
  </what-built>
  <how-to-verify>
Wait ~60 seconds for metrics to flow, then run these queries:

**A. Get the expected hostname value:**
```
kubectl get nodes -o jsonpath='{.items[0].metadata.name}'
```
This is the value that PHYSICAL_HOSTNAME resolves to via spec.nodeName.

**B. Runtime metrics in Prometheus (dotnet_* should have host_name from transform processor):**
Port-forward Prometheus if not already:
```
kubectl port-forward -n simetra svc/prometheus 9090:9090
```
Then query:
```
curl -s 'http://localhost:9090/api/v1/query?query=dotnet_gc_collections_total' | jq '.data.result[0].metric'
```
Look for `"host_name"` key in the metric labels. Value should match the node name from step A.

**C. Business/pipeline metrics (snmp_* should have host_name from .NET TagList):**
```
curl -s 'http://localhost:9090/api/v1/query?query=snmp_event_published_total' | jq '.data.result[0].metric'
```
Look for `"host_name"` key. Value should match.

If no snmp_* metrics exist yet (no devices configured), check pipeline counters:
```
curl -s 'http://localhost:9090/api/v1/query?query={host_name!=""}' | jq '[.data.result[].metric.__name__] | unique'
```
This shows all metric names that have a host_name label.

**D. Logs in Elasticsearch:**
Port-forward Elasticsearch if not already:
```
kubectl port-forward -n simetra svc/elasticsearch 9200:9200
```
Then query for host_name attribute in recent logs:
```
curl -s 'http://localhost:9200/simetra-logs-*/_search?size=1&sort=@timestamp:desc' | jq '.hits.hits[0]._source'
```
Look for `host_name` field in the log body/attributes. Value should match the node name.

**E. Cross-check: Confirm PHYSICAL_HOSTNAME env var inside the pod:**
```
kubectl exec -n simetra deployment/simetra -- printenv PHYSICAL_HOSTNAME
```
This should match the node name from step A.

**Pass criteria:**
- Runtime metrics (dotnet_*): host_name label present with correct value
- Business metrics (snmp_*) or pipeline counters: host_name label present with correct value
- Logs: host_name attribute present with correct value
- All three host_name values match the physical node hostname
  </how-to-verify>
  <resume-signal>Type "verified" with any notes, or describe issues found.</resume-signal>
</task>

</tasks>

<verification>
- PHYSICAL_HOSTNAME env var resolves to spec.nodeName inside the pod
- OTel Collector transform processor copies service.instance.id to host_name on all datapoints
- Prometheus shows host_name label on runtime metrics (dotnet_*)
- Prometheus shows host_name label on business/pipeline metrics (snmp_*)
- Elasticsearch shows host_name attribute on log entries
- All host_name values match the K8s node hostname
</verification>

<success_criteria>
All three observability targets (runtime metrics, business metrics, logs) carry host_name
with the correct physical hostname value. The PHYSICAL_HOSTNAME rename and OTel Collector
transform processor are confirmed working end-to-end.
</success_criteria>

<output>
After completion, create `.planning/quick/012-test-hostname-runtime-metrics-logs/012-SUMMARY.md`
</output>
