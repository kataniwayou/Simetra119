---
phase: quick
plan: "090"
subsystem: grafana-dashboards
tags: [grafana, dashboard, table, footer, row-count]

dependency-graph:
  requires: []
  provides: [table-footer-row-counts]
  affects: []

tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - deploy/grafana/dashboards/simetra-operations.json
    - deploy/grafana/dashboards/simetra-business.json

decisions: []

metrics:
  duration: "~5 min"
  completed: "2026-03-24"
---

# Quick Task 090: Table Footer Row Counts Summary

**One-liner:** Enabled Grafana table footer with row count display on all 4 table panels across both dashboards.

## What Was Done

Updated `options.footer` on all 4 `type: "table"` panels to enable the row count footer:

- `show: false` -> `show: true`
- `countRows: false` -> `countRows: true`
- `reducer: ["sum"]` -> `reducer: ["count"]`
- `fields: ""` unchanged

## Panels Modified

| Dashboard | Panel | Commit |
|-----------|-------|--------|
| simetra-operations.json | Pod Identity | 32ed4dd |
| simetra-operations.json | Tenant Status | 32ed4dd |
| simetra-business.json | Gauge Metrics | 32ed4dd |
| simetra-business.json | Info Metrics | 32ed4dd |

## Verification

All 4 panels confirmed via Python validation script:
- `footer.show == true`
- `footer.countRows == true`
- `footer.reducer == ["count"]`
- Both JSON files parse as valid JSON

## Deviations from Plan

None - plan executed exactly as written. Note: the plan described `footer` as a top-level panel property, but in practice Grafana stores it inside `options.footer`. The Python script was adapted accordingly — no functional difference.
