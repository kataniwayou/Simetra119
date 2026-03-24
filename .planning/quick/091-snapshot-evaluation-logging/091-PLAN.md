---
phase: quick
plan: 091
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Jobs/SnapshotJob.cs
autonomous: true

must_haves:
  truths:
    - "Each tenant evaluation cycle produces one Information-level summary log with tenant ID, priority, state, and stale metrics"
    - "Advance gate blocks are logged with the blocking tenant identity and state"
    - "Skipped priority groups are logged with group priority and tenant count"
    - "The silent AreAllEvaluateViolated path now has a Debug-level log"
    - "State transitions are tracked and logged only when state actually changes"
    - "All existing behavior, metrics, and log levels are unchanged"
  artifacts:
    - path: "src/SnmpCollector/Jobs/SnapshotJob.cs"
      provides: "Evaluation pipeline logging for Elastic traceability"
      contains: "s_previousStates"
  key_links:
    - from: "SnapshotJob.EvaluateTenant"
      to: "ILogger.LogInformation"
      via: "structured logging after state determination"
      pattern: 'LogInformation.*"Tenant \{TenantId\} priority=\{Priority\} state=\{State\}'
---

<objective>
Add comprehensive structured logging to SnapshotJob's evaluation pipeline so every tenant evaluation cycle is traceable in Elastic via the existing correlationId.

Purpose: Currently the evaluation pipeline is mostly silent at Information level. Operators cannot trace why a tenant entered a particular state or why the advance gate blocked without enabling Debug logging. These new log lines make the pipeline observable in production.

Output: Modified SnapshotJob.cs with 5 new logging points and a static state-change tracker.
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
  <name>Task 1: Add evaluation logging and state transition tracking</name>
  <files>src/SnmpCollector/Jobs/SnapshotJob.cs</files>
  <action>
Add a using directive for `System.Collections.Concurrent` at the top of the file.

Add a static field to the class:
```csharp
private static readonly ConcurrentDictionary<string, TenantState> s_previousStates = new();
```

Then add the following log statements at precise locations. Use structured logging parameters (curly-brace placeholders), NOT string interpolation. Do NOT change any existing log statements, metrics calls, or control flow.

**1. Final state summary per tenant (after DECIDE section, before DISPATCH section, around line 196):**
Compute stalePercent early (move the computation or duplicate it — simplest: compute it inline since staleTotal and staleCount are already available):
```csharp
var stalePercent = staleTotal == 0 ? 0.0 : staleCount * 100.0 / staleTotal;
```
Note: stalePercent is already computed later at line 245. To avoid duplication, move the stalePercent computation from line 245 up to just after the DECIDE block (after state is determined, before DISPATCH). Then reference it in both the log and the COMPUTE PERCENTAGES section. Remove the duplicate at line 245.

Then log:
```csharp
_logger.LogInformation(
    "Tenant {TenantId} priority={Priority} state={State} stale={StaleCount}/{StaleTotal} ({StalePercent:F1}%)",
    tenant.Id, tenant.Priority, state, staleCount, staleTotal, stalePercent);
```

**2. State transition tracking (immediately after the summary log above):**
```csharp
var previousState = s_previousStates.GetValueOrDefault(tenant.Id);
if (s_previousStates.TryGetValue(tenant.Id, out var oldState))
{
    if (oldState != state)
    {
        _logger.LogInformation(
            "Tenant {TenantId} state changed {OldState} -> {NewState}",
            tenant.Id, oldState, state);
    }
}
s_previousStates[tenant.Id] = state;
```
Simplify — remove the unused `previousState` variable. Just use the TryGetValue pattern directly.

**3. Silent tier-3 path (AreAllEvaluateViolated, lines 185-188):**
Add a log inside the `else if (AreAllEvaluateViolated(...))` block, before `state = TenantState.Unresolved;`:
```csharp
_logger.LogDebug(
    "Tenant {TenantId} priority={Priority} tier=3 -- all evaluate violated -- dispatching commands",
    tenant.Id, tenant.Priority);
```

**4. Advance gate block (in Execute(), after shouldAdvance is set to false, around line 92-94):**
After the advance gate loop (lines 88-94) but before the `if (!shouldAdvance) break;` at line 97, when shouldAdvance is false, find the blocking tenant. Restructure slightly:

After the existing advance gate loop (lines 87-94), add logging before the break:
```csharp
if (!shouldAdvance)
{
    // Find first blocking tenant for the log
    for (var i = 0; i < results.Length; i++)
    {
        if (results[i] == TenantState.Unresolved || results[i] == TenantState.NotReady)
        {
            _logger.LogInformation(
                "Advance gate blocked at priority={Priority} by tenant {TenantId} state={State}",
                group.Priority, group.Tenants[i].Id, results[i]);
            break;
        }
    }
    break;
}
```
Replace the existing `if (!shouldAdvance) break;` (line 97-98) with this expanded block.

**5. Skipped priority groups (after the advance-gate break exits the foreach):**
This is the trickiest one. The current foreach at line 64 iterates `_registry.Groups`. When we break out, remaining groups are skipped. To log them, we need to restructure the loop slightly.

Convert the foreach to iterate with an index so we can log remaining groups after breaking:
```csharp
var groups = _registry.Groups;
var advanceBlockedAtIndex = -1;

for (var g = 0; g < groups.Count; g++)
{
    var group = groups[g];
    // ... existing evaluation and advance gate logic ...

    if (!shouldAdvance)
    {
        // ... advance gate block log from item 4 above ...
        advanceBlockedAtIndex = g;
        break;
    }
}

// Log skipped priority groups
if (advanceBlockedAtIndex >= 0)
{
    for (var g = advanceBlockedAtIndex + 1; g < groups.Count; g++)
    {
        _logger.LogInformation(
            "Priority group {Priority} skipped -- advance gate blocked ({TenantCount} tenants)",
            groups[g].Priority, groups[g].Tenants.Count);
    }
}
```

Important: `_registry.Groups` must expose an indexable collection (IReadOnlyList). Check the type — if it is IEnumerable, convert to a list first with `.ToList()`. If it is already IReadOnlyList or List, use directly.

**Constraints to obey:**
- Do NOT remove or change ANY existing `_logger.LogDebug(...)` calls
- Do NOT change ANY existing `_logger.LogInformation(...)` calls (the tier-4 command enqueued log)
- Do NOT change ANY existing `_logger.LogWarning(...)` or `_logger.LogError(...)` calls
- Do NOT change any metric recording calls
- Do NOT change any control flow logic (evaluation, dispatch, suppression)
- Use structured logging parameters `{ParamName}` not `$"..."` interpolation
  </action>
  <verify>
Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` and confirm zero errors, zero warnings related to the changes. Then run `dotnet test` to confirm no test regressions.
  </verify>
  <done>
- SnapshotJob.cs builds cleanly with all 5 new logging points
- Existing tests pass without modification
- No existing log statements, metrics, or control flow changed
- Static ConcurrentDictionary tracks state transitions across cycles
- All new logs use structured parameters (no string interpolation)
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- zero errors
2. `dotnet test` -- all tests pass
3. Grep for all new log statements to confirm structured parameters:
   - `grep "Tenant {TenantId} priority={Priority} state={State} stale=" src/SnmpCollector/Jobs/SnapshotJob.cs`
   - `grep "Advance gate blocked" src/SnmpCollector/Jobs/SnapshotJob.cs`
   - `grep "Priority group.*skipped" src/SnmpCollector/Jobs/SnapshotJob.cs`
   - `grep "all evaluate violated" src/SnmpCollector/Jobs/SnapshotJob.cs`
   - `grep "state changed" src/SnmpCollector/Jobs/SnapshotJob.cs`
4. Confirm no `$"` interpolation in any new log calls
5. Confirm existing LogDebug calls at lines 104, 138, 156, 180, 191, 211 are unchanged
</verification>

<success_criteria>
- All 5 logging gaps filled with structured log statements
- State transition tracking via static ConcurrentDictionary
- Zero changes to existing behavior, metrics, or log levels
- Clean build, all tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/091-snapshot-evaluation-logging/091-SUMMARY.md`
</output>
