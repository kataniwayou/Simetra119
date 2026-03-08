---
phase: quick
plan: 025
subsystem: telemetry
tags: [metrics, dashboard, cleanup, dead-code]
dependency-graph:
  requires: [quick-024]
  provides: [clean-pipeline-metrics, accurate-dashboard]
  affects: []
tech-stack:
  added: []
  patterns: []
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs
    - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
    - tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs
    - deploy/grafana/dashboards/simetra-operations.json
decisions: []
metrics:
  duration: ~3 minutes
  completed: 2026-03-08
---

# Quick Task 025: Cleanup Dead Metrics, Dashboard, and Code Summary

**One-liner:** Removed dead PMET-08 (snmp.trap.unknown_device) metric from code/tests/dashboard and fixed stale Events Rejected panel description.

## What Was Done

### Task 1: Remove dead PMET-08 metric from code and tests
**Commit:** 40f266d

- Removed `_trapUnknownDevice` field, counter creation, and `IncrementTrapUnknownDevice` method from `PipelineMetricService.cs`
- Updated class doc comment from "11 pipeline counter instruments" to "10 pipeline counter instruments"
- Removed `IncrementTrapUnknownDevice_RecordsWithDeviceNameTag` test from `PipelineMetricServiceTests.cs`
- Removed `"snmp.trap.unknown_device"` from instrument name filter in `SnmpTrapListenerServiceTests.cs`

### Task 2: Remove dead dashboard panel and fix stale description
**Commit:** 3f34fad

- Removed "Trap Unknown Device" panel (id=11) from operations dashboard
- Widened remaining 3 panels in y=15 row from w=6 to w=8 each (fills 24-column grid)
- Fixed Events Rejected panel (id=7) description: changed `snmp_event_validation_failed_total` to `snmp_event_rejected_total`

## Deviations from Plan

None -- plan executed exactly as written.

## Verification

- `dotnet build` compiles cleanly (0 errors, 0 warnings)
- `dotnet test` passes all 137 tests
- Zero references to `unknown_device` in src/, tests/, or deploy/
- Zero references to `validation_failed` in dashboard JSON
- Dashboard JSON validates as syntactically correct
