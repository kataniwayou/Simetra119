---
phase: 47-commandworkerservice
plan: 01
subsystem: pipeline
tags: [channel, bounded-channel, command, set, di]
depends_on: []
provides:
  - CommandRequest record (6-field sealed record for SET command data)
  - ICommandChannel interface (Writer/Reader only)
  - CommandChannel implementation (BoundedChannel, capacity 16, DropWrite)
  - DI singleton registration for ICommandChannel
affects:
  - 47-02 (CommandWorkerService consumes ICommandChannel)
  - 48 (SnapshotJob enqueues via ICommandChannel.Writer)
tech_stack:
  added: []
  patterns:
    - BoundedChannel with DropWrite (caller-handles-failure pattern)
    - No-drain shutdown (immediate stop, idempotent re-evaluation)
key_files:
  created:
    - src/SnmpCollector/Pipeline/CommandRequest.cs
    - src/SnmpCollector/Pipeline/ICommandChannel.cs
    - src/SnmpCollector/Pipeline/CommandChannel.cs
  modified:
    - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
decisions:
  - id: CMD-CHAN-01
    decision: "DropWrite mode instead of DropOldest or Wait"
    rationale: "Caller (SnapshotJob) handles TryWrite=false via counter increment; no itemDropped callback needed"
  - id: CMD-CHAN-02
    decision: "No Complete/WaitForDrainAsync on ICommandChannel"
    rationale: "SET commands are idempotent; immediate stop on cancellation, re-evaluated next cycle"
  - id: CMD-CHAN-03
    decision: "Capacity 16 (fixed, not configurable)"
    rationale: "Command throughput is low (tens per cycle); 16 provides buffer without over-allocation"
metrics:
  duration: ~1 min
  completed: 2026-03-16
---

# Phase 47 Plan 01: Command Channel Infrastructure Summary

**One-liner:** BoundedChannel(16) with DropWrite for SET command dispatch — CommandRequest record, ICommandChannel interface, CommandChannel impl, DI singleton

## What Was Done

### Task 1: CommandRequest record + ICommandChannel + CommandChannel
Created three files in `src/SnmpCollector/Pipeline/`:
- **CommandRequest.cs**: Sealed record with 6 positional parameters (Ip, Port, CommandName, Value, ValueType, DeviceName). CommunityString intentionally excluded — resolved at execution time from IDeviceRegistry.
- **ICommandChannel.cs**: Interface exposing only Writer and Reader properties. No Complete() or WaitForDrainAsync() — differs from ITrapChannel by design (immediate stop, no drain).
- **CommandChannel.cs**: Sealed class creating BoundedChannel with capacity 16, DropWrite mode, SingleWriter=false, SingleReader=true, AllowSynchronousContinuations=false.

### Task 2: Register ICommandChannel in DI
Added `services.AddSingleton<ICommandChannel, CommandChannel>()` in `AddSnmpPipeline()` after the trap channel block, with Phase 47 comment header.

## Verification

- `dotnet build` succeeds with 0 warnings, 0 errors
- `dotnet test` passes all 367 existing tests (no regressions)
- CommandRequest has 6 fields, no CommunityString
- ICommandChannel has only Writer/Reader (no Complete/WaitForDrainAsync)
- CommandChannel uses BoundedChannelFullMode.DropWrite with capacity 16

## Deviations from Plan

None — plan executed exactly as written.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 0dd8319 | feat(47-01): add CommandRequest record, ICommandChannel, and CommandChannel |
| 2 | d0fced0 | feat(47-01): register ICommandChannel singleton in DI |

## Next Phase Readiness

Plan 47-02 (CommandWorkerService) can now inject ICommandChannel and start draining commands. No blockers.
