---
phase: quick
plan: 029
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-business.json
autonomous: true
---

<objective>
Remove the Trend column (delta query, merge transformation, Value #B overrides) from the gauge metrics table.
Reverts quick-028 since Grafana tables cannot achieve the desired flash-on-change coloring lifecycle.
</objective>
