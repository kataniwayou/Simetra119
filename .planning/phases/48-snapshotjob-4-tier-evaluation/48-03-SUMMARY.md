---
phase: 48-snapshotjob-4-tier-evaluation
plan: 03
subsystem: evaluation
tags: [evaluate-gate, command-dispatch, suppression, tier-evaluation]

dependency_graph:
  requires: [48-02]
  provides: [tier3-evaluate-gate, tier4-command-dispatch, are-all-evaluate-violated]
  affects: [48-04]

tech_stack:
  added: []
  patterns: [suppression-key-with-tenant-id, vacuous-false-evaluate, command-enqueue-with-trywrite]

file_tracking:
  key_files:
    created: []
    modified:
      - src/SnmpCollector/Jobs/SnapshotJob.cs
      - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs

decisions:
  - id: VACUOUS-FALSE-EVALUATE
    decision: "AreAllEvaluateViolated returns false when no Evaluate holders have data (vacuous false)"
    rationale: "No data = no command; opposite of Resolved gate which uses vacuous true (defensive)"
  - id: SUPPRESSION-KEY-FORMAT
    decision: "Suppression key: {TenantId}:{Ip}:{Port}:{CommandName}"
    rationale: "Isolate tenants per RESEARCH.md pitfall 2 — two tenants with same target get independent suppression"
  - id: TIER4-ALWAYS-COMMANDED
    decision: "Tier 4 always returns TierResult.Commanded regardless of suppression/failure outcomes"
    rationale: "Intent was to command; plan 48-04 will refine if needed"

metrics:
  duration: ~3 min
  completed: 2026-03-16
---

# Phase 48 Plan 03: Tier 3 Evaluate Gate and Tier 4 Command Dispatch Summary

**AreAllEvaluateViolated with vacuous-false semantics; Tier 4 command dispatch with tenant-scoped suppression key, channel-full handling, and Information/Warning/Debug logging; 10 new tests (27 total SnapshotJob tests, 403 total)**

## What Was Done

### Task 1: Implement Tier 3 Evaluate gate and Tier 4 command dispatch
Modified `src/SnmpCollector/Jobs/SnapshotJob.cs`:
- Added `AreAllEvaluateViolated`: filters `Role=="Evaluate"`, skips null `ReadSlot`, returns false when no Evaluate holders have data (vacuous false — no data = no command)
- Updated `EvaluateTenant` to wire Tier 3 after Tier 2: if not all Evaluate violated returns `Healthy`; if all violated proceeds to Tier 4
- Added Tier 4 command loop: iterates `tenant.Commands`, builds suppression key as `{tenant.Id}:{cmd.Ip}:{cmd.Port}:{cmd.CommandName}`, calls `TrySuppress` before `TryWrite`
- Suppressed commands: `IncrementCommandSuppressed(tenant.Id)` + Debug log
- Channel-full: `IncrementCommandFailed(tenant.Id)` + Warning log (no exception)
- Successful enqueue: Information-level log with enqueue count
- `CommandRequest.DeviceName` set to `tenant.Id` per locked decision

### Task 2: Unit tests for Tier 3 and Tier 4
Extended `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` with 10 new tests:

**Tier 3 (5 tests):**
1. All Evaluate violated → proceeds to Tier 4 (command enqueued)
2. One Evaluate in range → Healthy (no commands)
3. Evaluate null ReadSlot → excluded from check (does not prevent commanding)
4. Evaluate null threshold → treated as violated (proceeds to Tier 4)
5. Evaluate at exact boundary → NOT violated (Healthy)

**Tier 4 (5 tests):**
6. Command not suppressed → TryWrite with correct fields (Ip, Port, CommandName, Value, ValueType, DeviceName=tenant.Id)
7. Command suppressed → no TryWrite, Commanded result
8. Channel full → IncrementCommandFailed, no exception
9. Multiple commands → each checked independently (per-key suppression)
10. Suppression key includes tenant ID → two tenants with same target get independent suppression

Enhanced `StubSuppressionCache` with per-key `SuppressResults` dictionary for targeted suppression control.

## Verification

- `dotnet build src/SnmpCollector/` compiles with 0 errors
- `dotnet test tests/SnmpCollector.Tests/ --filter "SnapshotJobTests"` — 27 tests pass (17 from 48-02 + 10 new)
- `dotnet test tests/SnmpCollector.Tests/` — all 403 tests pass (393 existing + 10 new)
- Tier 3 tests set up Resolved holders as NOT all violated (prerequisite for reaching Tier 3)
- Suppression key includes tenant ID for isolation
- CommandRequest.DeviceName = tenant.Id in all enqueue paths

## Deviations from Plan

None — plan executed exactly as written.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | 1b6ff7c | feat(48-03): implement Tier 3 Evaluate gate and Tier 4 command dispatch in SnapshotJob |
| 2 | 3494d13 | test(48-03): add 10 unit tests for Tier 3 Evaluate gate and Tier 4 command dispatch |

## Next Phase Readiness

Plan 48-04 will add the priority-group advance gate and cycle summary logging using the now-complete TierResult enum values from all 4 tiers. The full evaluation flow is:
- Stale (T1) → ConfirmedBad if all Resolved violated (T2) → Healthy if not all Evaluate violated (T3) → Commanded via Tier 4 commands

No blockers.
