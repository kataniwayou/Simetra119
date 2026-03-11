---
phase: quick
plan: 046
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true

must_haves:
  truths:
    - "Tenant Vector Routed panel appears in Pipeline Counters section at x=18, y=23"
    - "Panel shows rate of snmp_tenantvector_routed_total per pod"
    - "Dashboard JSON remains valid and importable"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Tenant Vector Routed timeseries panel"
      contains: "snmp_tenantvector_routed_total"
  key_links:
    - from: "Grafana panel query"
      to: "Prometheus metric"
      via: "PromQL rate query"
      pattern: "rate\\(snmp_tenantvector_routed_total"
---

<objective>
Add a "Tenant Vector Routed" timeseries panel to the Simetra Operations Grafana dashboard.

Purpose: Make the snmp_tenantvector_routed_total counter visible on the operations dashboard alongside other pipeline metrics.
Output: Updated simetra-operations.json with the new panel.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@deploy/grafana/dashboards/simetra-operations.json
</context>

<tasks>

<task type="auto">
  <name>Task 1: Insert Tenant Vector Routed panel into dashboard JSON</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
    Insert a new timeseries panel into the `panels` array, between the "Poll Recovered" panel (id=14, ends at approximately line 1226) and the ".NET Runtime" row (id=15, starts at approximately line 1227).

    The new panel JSON object (id=22) must match the exact structure of the "Poll Recovered" panel (id=14) with these differences:

    - `gridPos`: `{ "h": 8, "w": 6, "x": 18, "y": 23 }` (same row, next slot after Poll Recovered at x=12)
    - `id`: 22 (next unused id; existing ids use 1-21)
    - `title`: "Tenant Vector Routed"
    - `description`: "Rate of SNMP samples routed to tenant vector metric slots by TenantVectorFanOutBehavior. Source: snmp_tenantvector_routed_total."
    - `targets[0].expr`: `sum by (k8s_pod_name) (rate(snmp_tenantvector_routed_total{k8s_pod_name=~\"$pod\"}[$__rate_interval]))`

    Keep identical from the Poll Recovered panel template:
    - `datasource`: `{"type": "prometheus", "uid": "${DS_PROMETHEUS}"}`
    - `fieldConfig` with `unit: "ops"`, lineWidth: 2, fillOpacity: 10, color palette-classic, thresholds green/red at 80
    - `options` with legend bottom list, tooltip multi sort desc
    - `targets[0].legendFormat`: `{{k8s_pod_name}}`
    - `targets[0].refId`: "A"
    - `targets[0].range`: true
    - `targets[0].instant`: false
    - `type`: "timeseries"

    After editing, validate the JSON is syntactically correct by parsing it (e.g., `python -c "import json; json.load(open('deploy/grafana/dashboards/simetra-operations.json'))"`).
  </action>
  <verify>
    1. `python -c "import json; d=json.load(open('deploy/grafana/dashboards/simetra-operations.json')); panels=[p for p in d['panels'] if p.get('title')=='Tenant Vector Routed']; assert len(panels)==1; p=panels[0]; assert p['id']==22; assert p['gridPos']=={'h':8,'w':6,'x':18,'y':23}; assert 'snmp_tenantvector_routed_total' in p['targets'][0]['expr']; print('PASS')"` prints PASS
    2. Total panel count increased by exactly 1 compared to current (currently 21 panels -> should be 22)
  </verify>
  <done>
    Dashboard JSON contains a "Tenant Vector Routed" timeseries panel at id=22, position x=18 y=23 in the Pipeline Counters section, querying rate of snmp_tenantvector_routed_total per pod, styled identically to sibling pipeline counter panels.
  </done>
</task>

</tasks>

<verification>
- Dashboard JSON parses without errors
- New panel exists with correct id, position, query, title, and description
- No existing panels were modified
- Panel style (fieldConfig, options) matches other Pipeline Counter panels
</verification>

<success_criteria>
- simetra-operations.json contains 22 panels (was 21)
- "Tenant Vector Routed" panel is positioned at x=18, y=23, w=6, h=8
- Query uses `rate(snmp_tenantvector_routed_total{k8s_pod_name=~"$pod"}[$__rate_interval])`
- JSON is valid and importable
</success_criteria>

<output>
After completion, create `.planning/quick/046-add-tenantvector-metric-to-dashboard/046-SUMMARY.md`
</output>
