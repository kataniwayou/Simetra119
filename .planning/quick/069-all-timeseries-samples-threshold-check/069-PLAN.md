---
phase: quick
plan: 069
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Jobs/SnapshotJob.cs
  - tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs
autonomous: true

must_haves:
  truths:
    - "Evaluate metric is only considered violated when ALL time series samples violate the threshold"
    - "A single in-range sample in the time series prevents Evaluate violation"
    - "Resolved metrics still check only the latest sample (unchanged)"
  artifacts:
    - path: "src/SnmpCollector/Jobs/SnapshotJob.cs"
      provides: "AreAllEvaluateViolated using ReadSeries() instead of ReadSlot()"
      contains: "ReadSeries"
    - path: "tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs"
      provides: "Tests for all-samples threshold check behavior"
  key_links:
    - from: "SnapshotJob.AreAllEvaluateViolated"
      to: "MetricSlotHolder.ReadSeries()"
      via: "iterating full ImmutableArray"
      pattern: "ReadSeries\\(\\)"
---

<objective>
Change AreAllEvaluateViolated to require ALL time series samples (not just the latest) to violate the threshold before considering an Evaluate metric violated. This prevents false positives from transient single-sample spikes.

Purpose: Reduce false command dispatches by requiring sustained threshold violations across the full time series window.
Output: Updated SnapshotJob.cs and new/updated tests confirming all-samples behavior.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Jobs/SnapshotJob.cs
@src/SnmpCollector/Pipeline/MetricSlotHolder.cs
@tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Update AreAllEvaluateViolated to check all time series samples</name>
  <files>src/SnmpCollector/Jobs/SnapshotJob.cs</files>
  <action>
In `AreAllEvaluateViolated` (line ~263), replace the single-sample check with a full-series check:

1. Replace `holder.ReadSlot()` with `holder.ReadSeries()` to get the full `ImmutableArray<MetricSlot>`.
2. If the series is empty (Length == 0), skip the holder (same as current null-slot behavior -- does not participate).
3. For each sample in the series, call `IsViolated(holder, sample)`. If ANY sample is NOT violated, that holder is not violated -- return false immediately.
4. If all samples in the series are violated, increment `checkedCount` and continue to the next holder.
5. Keep the existing return logic: `return checkedCount > 0`.

Update the XML doc comment to reflect that all time series samples must violate the threshold, not just the latest.

Do NOT change `AreAllResolvedViolated` -- Resolved metrics intentionally check only the latest sample.
  </action>
  <verify>
`dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles without errors.
  </verify>
  <done>AreAllEvaluateViolated iterates all samples from ReadSeries() and requires every sample to be violated. AreAllResolvedViolated is unchanged.</done>
</task>

<task type="auto">
  <name>Task 2: Add tests for all-samples threshold check</name>
  <files>tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs</files>
  <action>
Add a `MakeHolder` overload or modify existing helper to accept `timeSeriesSize` parameter (current helper hardcodes `timeSeriesSize: 1`). Add a parameter with default value 1 so existing tests are unaffected:

```csharp
private static MetricSlotHolder MakeHolder(
    int intervalSeconds = 3600,
    double graceMultiplier = 2.0,
    string role = "Resolved",
    ThresholdOptions? threshold = null,
    string metricName = "test-metric",
    int timeSeriesSize = 1)
```

Add these test cases:

1. **Execute_EvaluateAllSeriesSamplesViolated_ProceedsToTier4**: Create Evaluate holder with `timeSeriesSize: 3`. Write 3 values that all violate threshold (e.g., Min=10, write 5.0 three times). Verify command is enqueued (Tier 4 reached).

2. **Execute_EvaluateOneSeriesSampleInRange_Healthy**: Create Evaluate holder with `timeSeriesSize: 3`. Write 2 violated values and 1 in-range value (e.g., Min=10, write 5.0, 5.0, 50.0). Verify result is Healthy and no command enqueued.

3. **Execute_EvaluatePartialSeriesFill_AllViolated_ProceedsToTier4**: Create Evaluate holder with `timeSeriesSize: 5` but only write 2 violated values. Verify command is enqueued (partial fill, all present samples violated).

Each test needs a Resolved holder that is NOT all-violated (in-range) so Tier 3 evaluation is reached, following the pattern of existing tests like `Execute_AllEvaluateViolated_ProceedsToTier4`.
  </action>
  <verify>
`dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests pass including new and existing ones. Existing Evaluate tests still pass (they use timeSeriesSize=1 which means the single sample IS the full series).
  </verify>
  <done>Three new tests confirm: all-violated series triggers Tier 4, one in-range sample prevents Tier 4, partial series fill with all violated triggers Tier 4. All existing tests still pass.</done>
</task>

</tasks>

<verification>
- `dotnet build src/SnmpCollector/SnmpCollector.csproj` succeeds
- `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` all pass
- Grep `ReadSeries` in AreAllEvaluateViolated confirms new approach
- Grep `ReadSlot` in AreAllResolvedViolated confirms Resolved is unchanged
</verification>

<success_criteria>
- AreAllEvaluateViolated checks all time series samples via ReadSeries()
- AreAllResolvedViolated remains unchanged (latest sample only)
- All existing tests pass without modification (timeSeriesSize=1 default preserves behavior)
- Three new tests cover: full series violated, partial series with in-range sample, partial fill
</success_criteria>

<output>
After completion, create `.planning/quick/069-all-timeseries-samples-threshold-check/069-SUMMARY.md`
</output>
