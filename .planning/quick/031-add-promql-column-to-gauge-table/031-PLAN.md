---
phase: quick
plan: 031
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/grafana/dashboards/simetra-business.json
autonomous: true
---

<objective>
Add a PromQL column to the gauge metrics table showing a copyable PromQL query string
per metric row, so users can create their own time series panels.
</objective>
