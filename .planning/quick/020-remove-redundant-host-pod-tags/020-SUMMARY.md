---
phase: quick
plan: 020
subsystem: telemetry
tags: [otel, metrics, cardinality, tags]
dependency-graph:
  requires: []
  provides: [reduced-metric-cardinality]
  affects: [grafana-dashboards]
tech-stack:
  added: []
  patterns: [resource-level-identity]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs
    - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
    - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
    - tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs
decisions:
  - id: Q020-D1
    description: "host_name and pod_name removed from metric tags; OTel resource attributes provide identity"
metrics:
  duration: ~3 min
  completed: 2026-03-08
---

# Quick Task 020: Remove Redundant host_name/pod_name Tags Summary

Removed redundant host_name and pod_name per-metric tags from PipelineMetricService (11 counters) and SnmpMetricFactory (2 gauges), cutting cardinality by eliminating duplicate identity labels already provided by OTel resource attributes service_instance_id and k8s_pod_name.

## What Was Done

### Task 1: Remove host_name/pod_name from PipelineMetricService and its tests
- Removed `_hostName` and `_podName` fields and their `Environment.GetEnvironmentVariable` calls
- Simplified all 11 `Increment*` method TagLists from 3 tags to 1 (`device_name` only)
- Fixed tests that called trap methods with zero arguments (source now requires `string deviceName`)
- Updated all test assertions to verify absence of host_name/pod_name
- Commit: `6043212`

### Task 2: Remove host_name/pod_name from SnmpMetricFactory and its tests
- Removed `_hostName` and `_podName` fields and their `Environment.GetEnvironmentVariable` calls
- RecordGauge TagList reduced from 8 to 6 tags
- RecordInfo TagList reduced from 9 to 7 tags
- Renamed test methods to reflect correct tag counts (SixLabels/SevenLabels)
- Updated doc comment to remove host_name reference
- Commit: `976b36e`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed test method signatures for trap counters**
- **Found during:** Task 1
- **Issue:** Tests called `IncrementTrapAuthFailed()`, `IncrementTrapUnknownDevice()`, and `IncrementTrapReceived()` with zero arguments, but source signatures require `string deviceName`
- **Fix:** Added `"test-device"` argument to all three calls; replaced `Assert.DoesNotContain("device_name", ...)` with `Assert.Equal("test-device", tags["device_name"])`
- **Files modified:** PipelineMetricServiceTests.cs
- **Commit:** 6043212

## Verification

- Full test suite: 138/138 passed
- Zero occurrences of host_name/pod_name in PipelineMetricService.cs and SnmpMetricFactory.cs
- SnmpLogEnrichmentProcessor.cs still contains host_name (logs untouched, as intended)

## Notes

- OTel resource-level attributes `service_instance_id` (from NODE_NAME) and `k8s_pod_name` (from HOSTNAME) continue to provide pod/host identity in Prometheus as `instance` and `k8s_pod_name` labels
- Grafana dashboards referencing `host_name` or `pod_name` metric labels will need updating to use the resource-level equivalents
