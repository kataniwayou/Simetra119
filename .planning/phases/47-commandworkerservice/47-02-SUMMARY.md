---
phase: 47-commandworkerservice
plan: 02
subsystem: pipeline
tags: [background-service, snmp-set, command-worker, mediatr, channel-consumer]
depends_on: [47-01]
provides:
  - CommandWorkerService BackgroundService (drain loop, SET execution, response dispatch)
  - Singleton-then-HostedService DI registration
  - 9 unit tests for command worker behavior
affects:
  - 48 (SnapshotJob enqueues commands; CommandWorkerService executes them)
tech_stack:
  added: []
  patterns:
    - BackgroundService channel drain with per-item error isolation
    - SET timeout via linked CancellationTokenSource (IntervalSeconds * TimeoutMultiplier)
    - Response varbind dispatch with MetricName pre-set from command map
key_files:
  created:
    - src/SnmpCollector/Services/CommandWorkerService.cs
    - tests/SnmpCollector.Tests/Services/CommandWorkerServiceTests.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
decisions:
  - id: CWS-01
    decision: "DeviceName on SnmpOidReceived from CommandRequest.DeviceName, not device.Name"
    rationale: "CONTEXT.md locked decision; consistent with tenant-aware command dispatch"
  - id: CWS-02
    decision: "No ICommandWorkerService interface"
    rationale: "SnapshotJob injects ICommandChannel only; worker is internal implementation detail"
metrics:
  duration: ~2 min
  completed: 2026-03-16
---

# Phase 47 Plan 02: CommandWorkerService Implementation Summary

**One-liner:** BackgroundService draining command channel, executing SNMP SETs with timeout, dispatching response varbinds via ISender.Send with Source=Command and MetricName pre-set

## What Was Done

### Task 1: CommandWorkerService implementation
Created `CommandWorkerService.cs` in `src/SnmpCollector/Services/`:
- **ExecuteAsync**: Mirrors ChannelConsumerService drain loop -- `await foreach` over `_commandChannel.Reader.ReadAllAsync(stoppingToken)`, correlation ID wiring, per-item try/catch with error isolation.
- **ExecuteCommandAsync**: Resolves OID via `ICommandMapService.ResolveCommandOid`, resolves device via `IDeviceRegistry.TryGetByIpPort`, builds Variable using `SharpSnmpClient.ParseSnmpData`, executes `SetAsync` with linked CTS timeout (`IntervalSeconds * TimeoutMultiplier`), dispatches each response varbind as `SnmpOidReceived` with `Source=Command` and `MetricName` pre-set from `ResolveCommandName`.
- **Error paths**: OID not found, device not found, SET timeout, and general exceptions all increment `snmp.command.failed` and continue processing.
- **Anti-patterns avoided**: No `device.Name` for DeviceName, no `Writer.Complete()`, no drain on shutdown, timeout vs shutdown correctly distinguished.

### Task 2: DI registration + unit tests
- **DI**: Added Singleton-then-HostedService registration after `ICommandChannel` in `ServiceCollectionExtensions.AddSnmpPipeline()`.
- **Tests**: 9 `[Fact]` tests in `CommandWorkerServiceTests.cs` with `[Collection(NonParallelCollection.Name)]`:
  1. DispatchesSetAndCallsSenderSend -- happy path, OID matches response
  2. SetsSourceToCommand -- Source == SnmpSource.Command
  3. SetsDeviceNameFromCommandRequest -- uses "request-name" not "registry-name"
  4. PreSetsMetricNameWhenOidFoundInCommandMap -- MetricName == "set-power-threshold"
  5. LeavesMetricNameNullWhenOidNotInCommandMap -- MetricName is null
  6. ExceptionInSetAsync_ContinuesProcessing -- first throws, second succeeds
  7. OidNotInCommandMap_IncrementsFailedAndSkips -- no Send, counter incremented
  8. DeviceNotFound_IncrementsFailedAndSkips -- no Send, counter incremented
  9. IncrementsCommandSentOnSuccess -- snmp.command.sent counter == 1

## Verification

- `dotnet build` succeeds with 0 warnings, 0 errors
- `dotnet test` passes all 376 tests (367 existing + 9 new)
- CommandWorkerService registered as singleton with hosted service forwarding
- No ICommandWorkerService interface created
- DeviceName on SnmpOidReceived comes from req.DeviceName, not device.Name
- SET timeout uses IntervalSeconds * TimeoutMultiplier from SnapshotJobOptions
- Error isolation: one failed SET does not block subsequent commands

## Deviations from Plan

None -- plan executed exactly as written.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 2635684 | feat(47-02): implement CommandWorkerService BackgroundService |
| 2 | 411888b | feat(47-02): register CommandWorkerService DI + 9 unit tests |

## Next Phase Readiness

Phase 48 (SnapshotJob) can now inject `ICommandChannel` and enqueue `CommandRequest` items. `CommandWorkerService` will drain and execute them as SNMP SETs with full pipeline dispatch. No blockers.
