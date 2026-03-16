---
phase: 50-label-rename
plan: 01
subsystem: telemetry
tags: [prometheus, labels, grafana, e2e]
dependency-graph:
  requires: []
  provides: [resolved_name-label]
  affects: []
tech-stack:
  added: []
  patterns: []
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
    - src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs
    - src/SnmpCollector/Pipeline/CardinalityAuditService.cs
    - src/SnmpCollector/Configuration/MetricSlotOptions.cs
    - src/SnmpCollector/Configuration/OidMapOptions.cs
    - src/SnmpCollector/Configuration/TenantOptions.cs
    - src/SnmpCollector/Configuration/Validators/TenantVectorOptionsValidator.cs
    - tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs
    - deploy/grafana/dashboards/simetra-business.json
    - tests/e2e/scenarios/11-gauge-labels-e2e-sim.sh
    - tests/e2e/scenarios/12-gauge-labels-obp.sh
    - tests/e2e/scenarios/13-gauge-labels-npb.sh
    - tests/e2e/scenarios/14-info-labels.sh
    - tests/e2e/scenarios/15-unknown-oid.sh
    - tests/e2e/scenarios/16-trap-originated.sh
    - tests/e2e/scenarios/17-snmp-type-labels.sh
    - tests/e2e/scenarios/18-oid-rename.sh
    - tests/e2e/scenarios/19-oid-remove.sh
    - tests/e2e/scenarios/20-oid-add.sh
decisions: []
metrics:
  duration: ~2 min
  completed: 2026-03-16
---

# Phase 50 Plan 01: Label Rename Summary

Renamed metric_name Prometheus label to resolved_name across all 4 SNMP instruments, Grafana business dashboard, unit tests, and 10 E2E test scripts.

## What Was Done

### Task 1: Rename metric_name to resolved_name in C# source and unit tests
- Changed TagList key from `"metric_name"` to `"resolved_name"` in all 4 instruments (snmp_gauge, snmp_info, snmp_gauge_duration, snmp_info_duration) in SnmpMetricFactory.cs
- Updated 4 unit test assertions in SnmpMetricFactoryTests.cs to assert on `tags["resolved_name"]`
- Updated XML doc comments in ISnmpMetricFactory.cs, CardinalityAuditService.cs, MetricSlotOptions.cs, OidMapOptions.cs, TenantOptions.cs, TenantVectorOptionsValidator.cs
- C# parameter name `metricName` intentionally preserved (internal variable, not a Prometheus label)
- Commit: 33a3b9a

### Task 2: Update Grafana dashboard and E2E test scripts
- Replaced all 10 occurrences of `metric_name` with `resolved_name` in simetra-business.json (PromQL label_join/label_replace, histogram_quantile sum-by, column sort order, column overrides, OID Resolution pie chart queries)
- Updated all 10 E2E test scripts (scenarios 11-20): PromQL queries, jq label extraction, shell variable names (METRIC_NAME -> RESOLVED_NAME), evidence strings, and scenario comments
- E2E lib files (common.sh, prometheus.sh) intentionally left unchanged — their `metric_name` variable references Prometheus `__name__`, not our custom label
- Commit: 60ab887

## Verification Results

1. `dotnet test` -- 414/414 tests pass (including 4 SnmpMetricFactoryTests with resolved_name assertions)
2. `grep "metric_name" src/SnmpCollector/Telemetry/` -- zero results (no string literal tag key references remain)
3. `grep "metric_name" deploy/grafana/dashboards/simetra-business.json` -- zero results
4. `grep "metric_name" tests/e2e/scenarios/` -- zero results
5. `python -m json.tool simetra-business.json` -- valid JSON confirmed
6. `grep "metric_name" tests/e2e/lib/` -- 16 hits remain (correct, those reference Prometheus __name__)

## Deviations from Plan

None -- plan executed exactly as written.

## Decisions Made

None -- straightforward rename with no architectural choices.

## Next Phase Readiness

This is the final plan in the v2.0 milestone. No subsequent phases depend on this work.
