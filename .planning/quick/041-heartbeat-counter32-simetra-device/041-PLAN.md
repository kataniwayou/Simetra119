---
phase: quick
plan: 041
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
  - src/SnmpCollector/Jobs/HeartbeatJob.cs
  - src/SnmpCollector/Pipeline/OidMapService.cs
  - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
  - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
autonomous: true

must_haves:
  truths:
    - "Heartbeat exports as snmp_gauge with device_name=Simetra, metric_name=Heartbeat, snmp_type=counter32"
    - "Heartbeat value increments on each poll cycle instead of sending static 1"
    - "OtelMetricHandler no longer suppresses heartbeat - it flows through normal export path"
    - "All tests pass including updated heartbeat test assertions"
  artifacts:
    - path: "src/SnmpCollector/Configuration/HeartbeatJobOptions.cs"
      provides: "HeartbeatDeviceName = Simetra"
      contains: "\"Simetra\""
    - path: "src/SnmpCollector/Jobs/HeartbeatJob.cs"
      provides: "Counter32 with incrementing value"
      contains: "Counter32"
    - path: "src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs"
      provides: "No heartbeat suppression"
  key_links:
    - from: "HeartbeatJob.cs"
      to: "OtelMetricHandler.cs"
      via: "Counter32 varbind flows through pipeline without suppression"
      pattern: "Counter32"
---

<objective>
Change heartbeat from a suppressed Integer32(1) signal to a fully-exported Counter32 metric with incrementing value and device name "Simetra". This makes heartbeat visible in Prometheus as a standard snmp_gauge metric, enabling monitoring of collector liveness via the same dashboards used for real device metrics.

Purpose: Heartbeat becomes a first-class metric visible in Grafana alongside device data.
Output: Modified source files + updated passing tests.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
@src/SnmpCollector/Jobs/HeartbeatJob.cs
@src/SnmpCollector/Pipeline/OidMapService.cs
@src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
@tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Update heartbeat source code (options, job, seed, handler)</name>
  <files>
    src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    src/SnmpCollector/Jobs/HeartbeatJob.cs
    src/SnmpCollector/Pipeline/OidMapService.cs
    src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
  </files>
  <action>
    1. HeartbeatJobOptions.cs: Change `HeartbeatDeviceName = "heartbeat"` to `HeartbeatDeviceName = "Simetra"`.

    2. HeartbeatJob.cs:
       - Add a static field: `private static long _counter;`
       - Change the varbind from `new Integer32(1)` to `new Counter32((int)Interlocked.Increment(ref _counter))`.
       - Add `using Lextm.SharpSnmpLib;` if Counter32 is not already imported (it should be in scope from the existing using for Integer32).
       - The community string derivation already uses HeartbeatJobOptions.HeartbeatDeviceName, so changing the option to "Simetra" will automatically produce community string "Simetra.Simetra" via CommunityStringHelper.

    3. OidMapService.cs: In MergeWithHeartbeatSeed, change `merged[HeartbeatJobOptions.HeartbeatOid] = "heartbeat";` to `= "Heartbeat";` (capital H).

    4. OtelMetricHandler.cs:
       - Remove the `using SnmpCollector.Configuration;` import (line 4) that was added solely for the suppression check.
       - Remove the heartbeat suppression block (lines 40-44) that checks `if (request.DeviceName == HeartbeatJobOptions.HeartbeatDeviceName)` and returns Unit.Value.
       - The heartbeat will now flow through the existing switch statement where Counter32 maps to snmp_gauge export.
  </action>
  <verify>
    Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` and confirm zero errors.
  </verify>
  <done>
    HeartbeatDeviceName is "Simetra", HeartbeatJob sends Counter32 with incrementing value, OidMapService seeds "Heartbeat", OtelMetricHandler has no suppression block. Project builds cleanly.
  </done>
</task>

<task type="auto">
  <name>Task 2: Update tests and verify all pass</name>
  <files>
    tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
  </files>
  <action>
    1. Find the test `Heartbeat_SuppressedFromMetricExport` (or similarly named). Update it:
       - Change the test name to reflect that heartbeat IS now exported (e.g., `Heartbeat_ExportedAsGauge`).
       - Change the assertion from `Assert.Empty(result.GaugeRecords)` to `Assert.Single(result.GaugeRecords)`.
       - Verify the single gauge record has expected labels: device_name="Simetra", metric_name="Heartbeat", snmp_type="counter32".
       - Update the test's request DeviceName from "heartbeat" to "Simetra" if it references the old name directly (or keep using HeartbeatJobOptions.HeartbeatDeviceName which now resolves to "Simetra").
       - Make sure the varbind in the test sends Counter32 instead of Integer32 to match the new behavior.

    2. Find the test `HeartbeatDeviceName_SuppressedFromMetricExport` (or similarly named). Apply the same changes:
       - Rename to indicate export (e.g., `HeartbeatDeviceName_ExportedAsGauge`).
       - Change Empty assertion to Single assertion with label verification.
       - Update varbind type to Counter32.

    3. Search for any other test files referencing "heartbeat" (lowercase) as a device name or metric name and update to match new casing/behavior:
       ```bash
       grep -rn '"heartbeat"' tests/
       ```

    4. Run the full test suite: `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj`
       All tests must pass. Expect 207+ tests passing, zero failures.
  </action>
  <verify>
    `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` shows all tests passing with zero failures.
  </verify>
  <done>
    Heartbeat tests assert export (not suppression). All tests pass. No test references stale "heartbeat" lowercase device name.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- zero errors
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all pass
3. `grep -rn "SuppressedFromMetricExport" tests/` -- no matches (old test names gone)
4. `grep -rn '"heartbeat"' src/SnmpCollector/` -- no matches (all changed to "Heartbeat" or "Simetra")
</verification>

<success_criteria>
- HeartbeatJob sends Counter32 with incrementing value (not Integer32(1))
- HeartbeatDeviceName is "Simetra" producing community string "Simetra.Simetra"
- OidMapService seeds heartbeat OID as "Heartbeat" (capital H)
- OtelMetricHandler has NO heartbeat suppression -- heartbeat exports as snmp_gauge
- All tests pass including updated heartbeat export assertions
</success_criteria>

<output>
After completion, create `.planning/quick/041-heartbeat-counter32-simetra-device/041-SUMMARY.md`
</output>
