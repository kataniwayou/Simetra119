---
phase: quick
plan: "034"
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true

must_haves:
  truths:
    - "Pod Identity table shows columns in order: Host, Pod, Role"
    - "Host column displays service_instance_id values"
    - "Pod column displays k8s_pod_name values (not 'Pod Name')"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Operations dashboard with updated Pod Identity panel"
      contains: "Host"
  key_links:
    - from: "Pod Identity panel overrides"
      to: "service_instance_id"
      via: "displayName override"
      pattern: "\"displayName\".*\"Host\""
---

<objective>
Update the operations dashboard Pod Identity table panel to add a Host column (service_instance_id) at the first position, rename "Pod Name" to "Pod", and enforce column order Host, Pod, Role.

Purpose: Align operations dashboard with the business dashboard's established column naming and ordering pattern.
Output: Updated simetra-operations.json with correct column config.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@deploy/grafana/dashboards/simetra-operations.json
@deploy/grafana/dashboards/simetra-business.json (reference for Host/Pod pattern)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add Host column, rename Pod Name, enforce column order</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
In the Pod Identity table panel, make these three changes:

1. **Add service_instance_id override** - Add a new fieldConfig override entry:
   ```json
   {
     "matcher": { "id": "byName", "options": "service_instance_id" },
     "properties": [{ "id": "displayName", "value": "Host" }]
   }
   ```

2. **Rename k8s_pod_name** - Change the existing k8s_pod_name override's displayName from "Pod Name" to "Pod".

3. **Add organize transformation** - After the existing "merge" transformation, add an "organize" transformation to enforce column order:
   ```json
   {
     "id": "organize",
     "options": {
       "indexByName": {
         "service_instance_id": 0,
         "k8s_pod_name": 1,
         "Value #B": 2
       }
     }
   }
   ```
   This puts Host first (index 0), Pod second (index 1), Role third (index 2). Time and Value #A are already hidden via overrides so they don't need indexing.

Use Python to parse and modify the JSON to avoid manual edit errors. Preserve all other panel config unchanged.
  </action>
  <verify>
Validate with Python:
```bash
python -c "
import json
with open('deploy/grafana/dashboards/simetra-operations.json') as f:
    d = json.load(f)
for p in d['panels']:
    if p.get('title') == 'Pod Identity':
        overrides = p['fieldConfig']['overrides']
        # Check Host override exists
        host = [o for o in overrides if o['matcher']['options'] == 'service_instance_id']
        assert len(host) == 1, 'Missing service_instance_id override'
        assert host[0]['properties'][0]['value'] == 'Host', 'Not renamed to Host'
        # Check Pod rename
        pod = [o for o in overrides if o['matcher']['options'] == 'k8s_pod_name']
        assert pod[0]['properties'][0]['value'] == 'Pod', f'Expected Pod, got {pod[0][\"properties\"][0][\"value\"]}'
        # Check organize transform
        org = [t for t in p['transformations'] if t['id'] == 'organize']
        assert len(org) == 1, 'Missing organize transformation'
        idx = org[0]['options']['indexByName']
        assert idx['service_instance_id'] == 0, 'Host not at index 0'
        assert idx['k8s_pod_name'] == 1, 'Pod not at index 1'
        print('ALL CHECKS PASSED')
        break
"
```
  </verify>
  <done>Pod Identity panel has Host (service_instance_id) at column 0, Pod (k8s_pod_name) at column 1, Role at column 2. JSON is valid and all assertions pass.</done>
</task>

</tasks>

<verification>
- JSON parses without errors
- Pod Identity panel has 5 fieldConfig overrides (Time hidden, Value #A hidden, service_instance_id->Host, k8s_pod_name->Pod, Value #B->Role)
- Organize transformation orders columns: Host(0), Pod(1), Role(2)
- No other panels affected
</verification>

<success_criteria>
Operations dashboard Pod Identity table shows columns in order: Host, Pod, Role matching the business dashboard pattern.
</success_criteria>

<output>
After completion, create `.planning/quick/034-ops-dashboard-host-column-pod-rename/034-SUMMARY.md`
</output>
