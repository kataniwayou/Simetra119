---
phase: quick
plan: 056
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true

must_haves:
  truths:
    - "Aggregated Computed panel appears in Pipeline Counters section at x=0, y=31"
    - "Panel shows rate of snmp_aggregated_computed_total per pod"
    - "Dashboard JSON remains valid and importable"
  artifacts:
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Aggregated Computed timeseries panel"
      contains: "snmp_aggregated_computed_total"
  key_links:
    - from: "Grafana panel query"
      to: "Prometheus metric"
      via: "PromQL rate query"
      pattern: "rate\\(snmp_aggregated_computed_total"
---

<objective>
Add an "Aggregated Computed" timeseries panel to the Simetra Operations Grafana dashboard.

Purpose: Make the snmp.aggregated.computed counter (the 12th pipeline counter, added in Phase 40) visible on the operations dashboard alongside the existing 11 pipeline counter panels.
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
  <name>Task 1: Insert Aggregated Computed panel into dashboard JSON</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
    Insert a new timeseries panel into the `panels` array, between the "Tenant Vector Routed" panel (id=22, ends at approximately line 1322) and the ".NET Runtime" row (id=15, starts at approximately line 1323).

    The new panel JSON object (id=23) must match the exact structure of the "Tenant Vector Routed" panel (id=22) with these differences:

    - `gridPos`: `{ "h": 8, "w": 6, "x": 0, "y": 31 }` (new row below the y=23 row; first slot)
    - `id`: 23 (next unused id; existing ids use 1-22)
    - `title`: "Aggregated Computed"
    - `description`: "Rate of aggregated (combined) metrics computed by MetricPollJob aggregate dispatch. Source: snmp_aggregated_computed_total."
    - `targets[0].expr`: `sum by (k8s_pod_name) (rate(snmp_aggregated_computed_total{k8s_pod_name=~\"$pod\"}[$__rate_interval]))`

    Note: The OTel metric is named `snmp.aggregated.computed` in code (PipelineMetricService.cs), but Prometheus converts dots to underscores and appends `_total` for counters, so the PromQL metric name is `snmp_aggregated_computed_total`.

    Keep identical from the Tenant Vector Routed panel template:
    - `datasource`: `{"type": "prometheus", "uid": "${DS_PROMETHEUS}"}`
    - `fieldConfig` with `unit: "ops"`, lineWidth: 2, fillOpacity: 10, color palette-classic, thresholds green/red at 80
    - `options` with legend bottom list, tooltip multi sort desc
    - `targets[0].legendFormat`: `{{k8s_pod_name}}`
    - `targets[0].refId`: "A"
    - `targets[0].range`: true
    - `targets[0].instant`: false
    - `type`: "timeseries"

    Also update the ".NET Runtime" row's `gridPos.y` from 39 to 40 to account for the new row (31 + 8 = 39, but the .NET Runtime row was already at y=39 which implies Grafana auto-adjusts; keep it at 39 if that's how Grafana handles it — Grafana auto-layouts rows below panels, so no adjustment to subsequent panels is needed).

    After editing, validate the JSON is syntactically correct by parsing it (e.g., `python -c "import json; json.load(open('deploy/grafana/dashboards/simetra-operations.json'))"`).
  </action>
  <verify>
    1. `python -c "import json; d=json.load(open('deploy/grafana/dashboards/simetra-operations.json')); panels=[p for p in d['panels'] if p.get('title')=='Aggregated Computed']; assert len(panels)==1; p=panels[0]; assert p['id']==23; assert p['gridPos']=={'h':8,'w':6,'x':0,'y':31}; assert 'snmp_aggregated_computed_total' in p['targets'][0]['expr']; print('PASS')"` prints PASS
    2. Total panel count increased by exactly 1 compared to current (currently 22 panels -> should be 23)
    3. `python -c "import json; json.load(open('deploy/grafana/dashboards/simetra-operations.json')); print('JSON valid')"` prints "JSON valid"
  </verify>
  <done>
    Dashboard JSON contains an "Aggregated Computed" timeseries panel at id=23, position x=0 y=31 in the Pipeline Counters section, querying rate of snmp_aggregated_computed_total per pod, styled identically to sibling pipeline counter panels.
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
- simetra-operations.json contains 23 panels (was 22)
- "Aggregated Computed" panel is positioned at x=0, y=31, w=6, h=8
- Query uses `rate(snmp_aggregated_computed_total{k8s_pod_name=~"$pod"}[$__rate_interval])`
- JSON is valid and importable
</success_criteria>

<output>
After completion, create `.planning/quick/056-add-aggregated-computed-to-ops-dashboard/056-SUMMARY.md`
</output>
