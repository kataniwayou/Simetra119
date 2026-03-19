---
phase: quick
plan: 079
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Jobs/SnapshotJob.cs
autonomous: true

must_haves:
  truths:
    - "Each cycle logs a unique cycle ID and timestamp at the very start of Execute()"
    - "Each G1 tenant in a priority group logs its TierResult after evaluation"
    - "The advance gate logs whether it blocks or passes for each group"
  artifacts:
    - path: "src/SnmpCollector/Jobs/SnapshotJob.cs"
      provides: "Debug logging for advance gate diagnosis"
      contains: "cycleId"
  key_links: []
---

<objective>
Add debug-level logging to SnapshotJob.Execute() to diagnose why the advance gate
fails for multi-tenant priority groups.

Purpose: The advance gate blocks correctly for single-tenant groups but fails for
multi-tenant groups (2+ tenants). We need per-tenant TierResult visibility,
gate pass/block decisions per group, and concurrency proof via cycle IDs.

Output: Modified SnapshotJob.cs with diagnostic logging (zero logic changes).
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Jobs/SnapshotJob.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add cycle-start and advance-gate debug logging</name>
  <files>src/SnmpCollector/Jobs/SnapshotJob.cs</files>
  <action>
Add three blocks of LogDebug calls to Execute(). Do NOT change any logic, control flow,
or variable assignments. Only add _logger.LogDebug(...) calls.

1. **Cycle-start log** — immediately after `var sw = Stopwatch.StartNew();` (line 59),
   generate a short unique cycle ID and log it:
   ```csharp
   var cycleId = Guid.NewGuid().ToString("N")[..8];
   _logger.LogDebug(
       "[SnapCycle:{CycleId}] Execute started at {UtcNow:O}",
       cycleId, DateTimeOffset.UtcNow);
   ```

2. **Per-tenant TierResult log** — inside the `foreach (var group in _registry.Groups)` loop,
   immediately AFTER the results array is fully populated (after the if/else block that handles
   single vs multi-tenant, around line 79), log each tenant's result:
   ```csharp
   for (var i = 0; i < results.Length; i++)
   {
       _logger.LogDebug(
           "[SnapCycle:{CycleId}] Group={GroupPriority} Tenant={TenantId} TierResult={TierResult}",
           cycleId, group.Priority, group.Tenants[i].Id, results[i]);
   }
   ```

3. **Gate decision log** — immediately after the `shouldAdvance` variable is finalized
   (after the for-loop that checks for Unresolved, around line 97), log the gate decision:
   ```csharp
   _logger.LogDebug(
       "[SnapCycle:{CycleId}] Group={GroupPriority} tenants={TenantCount} gate={GateDecision}",
       cycleId, group.Priority, group.Tenants.Count,
       shouldAdvance ? "PASS" : "BLOCK");
   ```

4. **Update the existing cycle-complete log** (line 106-108) to include the cycleId
   so it can be correlated:
   ```csharp
   _logger.LogDebug(
       "[SnapCycle:{CycleId}] Snapshot cycle complete: {TenantsEvaluated} evaluated, {Unresolved} unresolved, {DurationMs:F1}ms",
       cycleId, totalEvaluated, totalUnresolved, sw.Elapsed.TotalMilliseconds);
   ```

Important:
- Use `[SnapCycle:{CycleId}]` prefix on ALL new/modified log lines for grep filtering.
- The `cycleId` variable must be declared in Execute() scope so all logs can reference it.
- The `group.Priority` property exists on the priority group object — verify the actual
  property name by checking the `_registry.Groups` type. If it is named differently
  (e.g., `PriorityLevel` or `Name`), use the correct property. Fall back to the loop
  index if no priority property exists.
- Do NOT add `using System;` — Guid and DateTimeOffset are already available.
  </action>
  <verify>
Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` — must compile with zero errors.
Grep the file for `[SnapCycle:` and confirm exactly 4 LogDebug calls contain this prefix.
  </verify>
  <done>
SnapshotJob.cs compiles cleanly with 4 new/modified LogDebug calls:
(1) cycle-start with unique ID, (2) per-tenant TierResult, (3) gate PASS/BLOCK,
(4) updated cycle-complete with cycleId correlation.
  </done>
</task>

</tasks>

<verification>
- `dotnet build src/SnmpCollector/SnmpCollector.csproj` succeeds with no errors or warnings
- `grep -c "SnapCycle" src/SnmpCollector/Jobs/SnapshotJob.cs` returns 4
- No logic changes: diff shows only additions (new lines) and one modified log line
</verification>

<success_criteria>
- Build passes
- Four [SnapCycle:*] log lines present
- Zero logic changes (no control flow, no variable reassignment, no new branches)
</success_criteria>

<output>
After completion, create `.planning/quick/079-snapshot-gate-debug-logging/079-SUMMARY.md`
</output>
