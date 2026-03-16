---
phase: 46-infrastructure-components
plan: 03
subsystem: telemetry
tags: [otel, counters, command-pipeline, metrics]
dependency-graph:
  requires: []
  provides:
    - "PipelineMetricService command counters (PMET-13/14/15)"
    - "IncrementCommandSent/Failed/Suppressed methods"
  affects:
    - "47-command-worker-service"
    - "48-snapshot-job-command-enqueue"
    - "49-dashboard-panels"
tech-stack:
  added: []
  patterns:
    - "Counter<long> with device_name TagList (established pattern)"
key-files:
  created: []
  modified:
    - src/SnmpCollector/Telemetry/PipelineMetricService.cs
    - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
decisions: []
metrics:
  duration: "~1 min"
  completed: "2026-03-16"
---

# Phase 46 Plan 03: Command Pipeline Counters Summary

Three OTel command counters (snmp.command.sent/failed/suppressed) added to PipelineMetricService with device_name tags and full test coverage.

## What Was Done

### Task 1: Add three command counters to PipelineMetricService + tests

| Step | Detail |
|------|--------|
| Counter fields | Added `_commandSent`, `_commandFailed`, `_commandSuppressed` (PMET-13/14/15) |
| Constructor init | Three `_meter.CreateCounter<long>()` calls for snmp.command.sent/failed/suppressed |
| Increment methods | `IncrementCommandSent`, `IncrementCommandFailed`, `IncrementCommandSuppressed` with device_name tag |
| Class doc | Updated "all 12 pipeline counter instruments" to "all 15 pipeline counter instruments" |
| Tests | Three new tests following MeterListener pattern confirming instrument name, value, and tag correctness |

**Commit:** `7fee54c` feat(46-03): add command pipeline counters to PipelineMetricService

## Verification

- `dotnet build src/SnmpCollector/` -- 0 warnings, 0 errors
- `dotnet test tests/SnmpCollector.Tests/ --filter "PipelineMetricService"` -- 6/6 pass (3 existing + 3 new)
- `dotnet test tests/SnmpCollector.Tests/` -- 347/347 pass
- `Counter<long>` count in PipelineMetricService.cs: 30 (15 fields + 15 CreateCounter = correct)

## Deviations from Plan

None -- plan executed exactly as written.

## Next Phase Readiness

CommandWorkerService (Phase 47) and SnapshotJob (Phase 48) can now call:
- `IncrementCommandSent(deviceName)` when dispatching SET commands
- `IncrementCommandFailed(deviceName)` on SET failures
- `IncrementCommandSuppressed(deviceName)` when SuppressionCache blocks a command

No blockers.
