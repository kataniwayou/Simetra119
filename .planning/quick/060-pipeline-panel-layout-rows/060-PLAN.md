---
phase: quick
plan: 060
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true
---

<objective>
Reorganize the Pipeline Counters panel group in the operations dashboard into 4 semantic rows:
1. Events (unchanged)
2. Polls (executed, unreachable, recovered — right to left)
3. Traps (received, auth_failed, dropped — right to left)
4. Routing (tenant vector routed, aggregated computed)
</objective>

<tasks>
<task type="auto">
  <name>Task 1: Update gridPos for pipeline counter panels</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>Update gridPos (x, y, w) for 8 panels to achieve the new 4-row layout.</action>
</task>
</tasks>
