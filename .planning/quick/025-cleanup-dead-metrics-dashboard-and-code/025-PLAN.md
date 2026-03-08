---
phase: quick
plan: 025
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Telemetry/PipelineMetricService.cs
  - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
  - tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs
  - deploy/grafana/dashboards/simetra-operations.json
autonomous: true

must_haves:
  truths:
    - "No dead metrics exist in PipelineMetricService (every defined counter has at least one caller)"
    - "Dashboard panels only query metrics that are actively emitted by code"
    - "Dashboard panel descriptions accurately reference the correct metric name"
    - "All tests compile and pass after metric removal"
  artifacts:
    - path: "src/SnmpCollector/Telemetry/PipelineMetricService.cs"
      provides: "Pipeline metric counters (10 live metrics, PMET-08 removed)"
      contains: "snmp.event.published"
    - path: "deploy/grafana/dashboards/simetra-operations.json"
      provides: "Operations dashboard with no dead panels"
  key_links:
    - from: "PipelineMetricService.cs"
      to: "simetra-operations.json"
      via: "OTel instrument names map to Prometheus metric names"
      pattern: "snmp\\."
---

<objective>
Remove the dead `snmp.trap.unknown_device` metric (PMET-08) from code, tests, and dashboard.
Fix the stale description on the "Events Rejected" dashboard panel.

Purpose: Eliminate dead code and dashboard panels that will never show data, reducing confusion during operations monitoring.
Output: Clean PipelineMetricService with 10 live metrics, dashboard with accurate panels only.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Telemetry/PipelineMetricService.cs
@tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
@tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs
@deploy/grafana/dashboards/simetra-operations.json
</context>

<tasks>

<task type="auto">
  <name>Task 1: Remove dead PMET-08 metric from code and tests</name>
  <files>
    src/SnmpCollector/Telemetry/PipelineMetricService.cs
    tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
    tests/SnmpCollector.Tests/Services/SnmpTrapListenerServiceTests.cs
  </files>
  <action>
In PipelineMetricService.cs:
- Remove the `_trapUnknownDevice` field declaration (line 37-38)
- Remove the `_trapUnknownDevice` counter creation in constructor (line 59)
- Remove the `IncrementTrapUnknownDevice` method and its XML doc comment (lines 98-104)
- Update the class doc comment from "11 pipeline counter instruments" to "10 pipeline counter instruments"

In PipelineMetricServiceTests.cs:
- Remove the test method `IncrementTrapUnknownDevice_RecordsWithDeviceNameTag` (around lines 73-81)
- Remove the comment about PMET-08 if present

In SnmpTrapListenerServiceTests.cs:
- On line 221, remove `or "snmp.trap.unknown_device"` from the instrument name filter. The filter should only match `"snmp.trap.auth_failed"`.

Do NOT renumber PMET codes on remaining metrics -- they are stable identifiers.
  </action>
  <verify>
Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- must compile with no errors.
Run `dotnet test tests/SnmpCollector.Tests/` -- all tests must pass.
Grep for `unknown_device` across the entire repo (excluding .planning/) -- should return zero hits.
  </verify>
  <done>
PMET-08 (snmp.trap.unknown_device) is fully removed from source code and tests.
No compilation errors. All remaining tests pass. No stale references anywhere in src/ or tests/.
  </done>
</task>

<task type="auto">
  <name>Task 2: Remove dead dashboard panel and fix stale description</name>
  <files>deploy/grafana/dashboards/simetra-operations.json</files>
  <action>
In simetra-operations.json:

1. Remove the "Trap Unknown Device" panel (id=11). This is the entire panel object from roughly line 913 to line 1012 (the object that contains `"title": "Trap Unknown Device"` and `"expr": "...snmp_trap_unknown_device_total..."`).

2. After removing panel id=11, the row at y=15 has 3 remaining panels (ids 8, 9, 10) that were each w=6.
   Widen these 3 panels to w=8 each (3 x 8 = 24, filling the row):
   - Panel id=8 (Polls Executed): gridPos x=0, w=8
   - Panel id=9 (Traps Received): gridPos x=8, w=8
   - Panel id=10 (Trap Auth Failed): gridPos x=16, w=8

3. Fix the "Events Rejected" panel (id=7) description:
   Change: `"Source: snmp_event_validation_failed_total."`
   To: `"Source: snmp_event_rejected_total."`
   (The query already correctly uses snmp_event_rejected_total -- only the description text is wrong.)

Validate the resulting JSON is syntactically valid.
  </action>
  <verify>
Run `python -m json.tool deploy/grafana/dashboards/simetra-operations.json > /dev/null` (or equivalent JSON validation) -- must parse without errors.
Grep for `unknown_device` in the dashboard file -- should return zero hits.
Grep for `validation_failed` in the dashboard file -- should return zero hits.
Count the total panels: should be one fewer than before (the row panel at y=15 should have 3 panels instead of 4).
  </verify>
  <done>
Dashboard has no panel querying snmp_trap_unknown_device_total.
The y=15 row has 3 evenly-spaced panels (w=8 each).
"Events Rejected" panel description correctly references snmp_event_rejected_total.
Dashboard JSON is valid.
  </done>
</task>

</tasks>

<verification>
- `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles cleanly
- `dotnet test tests/SnmpCollector.Tests/` all tests pass
- No references to `unknown_device` exist in src/, tests/, or deploy/ directories
- No references to `validation_failed` exist in the dashboard JSON
- Dashboard JSON parses without errors
- Dashboard panel count is correct (one fewer than before)
</verification>

<success_criteria>
1. PipelineMetricService defines exactly 10 counter instruments (PMET-08 removed)
2. Every remaining Increment method has at least one caller in src/
3. Every dashboard panel queries a metric that is actively emitted
4. Dashboard panel descriptions match actual metric names
5. All tests compile and pass
</success_criteria>

<output>
After completion, create `.planning/quick/025-cleanup-dead-metrics-dashboard-and-code/025-SUMMARY.md`
</output>
