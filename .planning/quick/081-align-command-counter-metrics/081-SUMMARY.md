---
phase: quick-081
plan: 01
subsystem: telemetry
tags: [metrics, counter, rename, snmp-set, grafana, e2e]
dependency-graph:
  requires: []
  provides: [snmp.command.dispatched counter on TryWrite, consistent dispatch counting across replicas]
  affects: [grafana dashboards, e2e test suite]
tech-stack:
  added: []
  patterns: [dispatch-counting-at-enqueue-site]
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs
    - src/SnmpCollector/Jobs/SnapshotJob.cs
    - src/SnmpCollector/Services/CommandWorkerService.cs
    - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
    - tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs
    - deploy/grafana/dashboards/simetra-operations.json
    - tests/e2e/scenarios/*.sh (21 files)
decisions: []
metrics:
  duration: ~5 min
  completed: 2026-03-20
---

# Quick Task 081: Align Command Counter Metrics Summary

Rename snmp.command.sent to snmp.command.dispatched and move increment from CommandWorkerService (leader-only, post-SET) to SnapshotJob TryWrite (all instances, on channel enqueue).

## Tasks Completed

| Task | Name | Commit | Key Changes |
|------|------|--------|-------------|
| 1 | Rename metric and relocate increment in C# source | 9f788d6 | Renamed IncrementCommandSent to IncrementCommandDispatched, moved counter from CommandWorkerService to SnapshotJob TryWrite success path, updated all unit tests |
| 2 | Update Grafana dashboard and all E2E test scripts | 6407c3b | Dashboard panel title/expr/description updated, 108 occurrences replaced across 21 E2E scripts |

## What Changed

**PipelineMetricService.cs:** Field `_commandSent` renamed to `_commandDispatched`, instrument name `snmp.command.sent` to `snmp.command.dispatched`, method `IncrementCommandSent` to `IncrementCommandDispatched`.

**SnapshotJob.cs:** Added `_pipelineMetrics.IncrementCommandDispatched(tenant.Id)` inside the `TryWrite(request)` success branch. This means all replicas (not just the leader) now report dispatch volume.

**CommandWorkerService.cs:** Removed lines 193-194 (success counter comment and `IncrementCommandSent` call). The command_failed counter remains for SET errors and timeouts.

**Grafana dashboard:** Panel title "Command Sent" to "Command Dispatched", PromQL expr and description updated.

**E2E scripts:** All 21 scenario scripts updated from `snmp_command_sent_total` to `snmp_command_dispatched_total`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CommandWorkerServiceTests.IncrementsCommandSentOnSuccess test failure**

- **Found during:** Task 1 verification (dotnet test)
- **Issue:** The plan did not list `CommandWorkerServiceTests.cs` as a file to modify, but it contained a test `IncrementsCommandSentOnSuccess` that asserted `snmp.command.sent` was incremented by CommandWorkerService. After removing that call, the test failed.
- **Fix:** Renamed test to `NoCommandDispatchedCounter_OnSuccess` and changed assertion to verify 0 occurrences of `snmp.command.dispatched` (since dispatch is now counted in SnapshotJob, not CommandWorkerService).
- **Files modified:** tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs
- **Commit:** 9f788d6

## Verification

- dotnet build: 0 errors, 0 warnings
- dotnet test: 462 passed, 0 failed
- grep for old names (IncrementCommandSent, snmp.command.sent, snmp_command_sent): 0 matches in src/ and tests/
- grep for snmp_command_dispatched_total in tests/e2e/scenarios/: 108 matches across 21 files
- IncrementCommandDispatched in SnapshotJob.cs: 1 match (TryWrite success path)
- IncrementCommandDispatched in CommandWorkerService.cs: 0 matches
