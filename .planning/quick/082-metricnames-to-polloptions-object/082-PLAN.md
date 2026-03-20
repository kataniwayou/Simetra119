---
phase: quick-082
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/PollMetricOptions.cs
  - src/SnmpCollector/Configuration/PollOptions.cs
  - src/SnmpCollector/Services/DeviceWatcherService.cs
  - src/SnmpCollector/config/devices.json
  - src/SnmpCollector/appsettings.Development.json
  - deploy/k8s/snmp-collector/simetra-devices.yaml
  - deploy/k8s/production/configmap.yaml
  - tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs
  - tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs
autonomous: true

must_haves:
  truths:
    - "PollOptions.Metrics is List<PollMetricOptions> instead of List<string> MetricNames"
    - "All JSON configs deserialize correctly with new Metrics object array shape"
    - "All unit tests pass with new property names"
    - "dotnet build succeeds with zero errors"
  artifacts:
    - path: "src/SnmpCollector/Configuration/PollMetricOptions.cs"
      provides: "PollMetricOptions wrapper class"
      contains: "public sealed class PollMetricOptions"
    - path: "src/SnmpCollector/Configuration/PollOptions.cs"
      provides: "Updated PollOptions with List<PollMetricOptions> Metrics"
      contains: "List<PollMetricOptions> Metrics"
  key_links:
    - from: "src/SnmpCollector/Services/DeviceWatcherService.cs"
      to: "PollOptions.Metrics"
      via: "poll.Metrics and m.MetricName"
      pattern: "poll\\.Metrics"
---

<objective>
Refactor PollOptions.MetricNames (List<string>) to PollOptions.Metrics (List<PollMetricOptions>) with a new PollMetricOptions wrapper class.

Purpose: Introduce an object wrapper around metric names to enable future per-metric configuration (thresholds, labels, etc.) without another breaking config change.
Output: Updated C# model, service code, all JSON/YAML configs, and unit tests.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Configuration/PollOptions.cs
@src/SnmpCollector/Services/DeviceWatcherService.cs
@src/SnmpCollector/config/devices.json
@src/SnmpCollector/appsettings.Development.json
@deploy/k8s/snmp-collector/simetra-devices.yaml
@deploy/k8s/production/configmap.yaml
@tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs
@tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create PollMetricOptions and update C# source</name>
  <files>
    src/SnmpCollector/Configuration/PollMetricOptions.cs
    src/SnmpCollector/Configuration/PollOptions.cs
    src/SnmpCollector/Services/DeviceWatcherService.cs
  </files>
  <action>
1. Create `src/SnmpCollector/Configuration/PollMetricOptions.cs`:
   ```csharp
   namespace SnmpCollector.Configuration;

   /// <summary>
   /// Options for a single metric within a poll group.
   /// </summary>
   public sealed class PollMetricOptions
   {
       public string MetricName { get; set; } = string.Empty;
   }
   ```

2. In `PollOptions.cs`, change line 16:
   - FROM: `public List<string> MetricNames { get; set; } = [];`
   - TO:   `public List<PollMetricOptions> Metrics { get; set; } = [];`
   - Update the XML doc comment to say "Metrics to poll in this group" instead of "Metric names to poll".

3. In `DeviceWatcherService.cs`:
   - Line 323: change `foreach (var name in poll.MetricNames)` to `foreach (var m in poll.Metrics)` and update the loop body to use `m.MetricName` instead of `name` for the metric name value.
   - Line 339: change `poll.MetricNames.Count` to `poll.Metrics.Count`.
   - Verify no other references to MetricNames exist in this file.
  </action>
  <verify>Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- must compile with zero errors (tests will fail until configs updated, that is expected).</verify>
  <done>PollMetricOptions.cs exists, PollOptions uses List<PollMetricOptions> Metrics, DeviceWatcherService iterates poll.Metrics with m.MetricName. Build succeeds.</done>
</task>

<task type="auto">
  <name>Task 2: Transform all JSON and YAML config files</name>
  <files>
    src/SnmpCollector/config/devices.json
    src/SnmpCollector/appsettings.Development.json
    deploy/k8s/snmp-collector/simetra-devices.yaml
    deploy/k8s/production/configmap.yaml
  </files>
  <action>
In every file, find all occurrences of the old MetricNames string array pattern and transform to the new Metrics object array pattern.

Transform pattern:
- FROM: `"MetricNames": ["metric_a", "metric_b", ...]`
- TO:   `"Metrics": [{"MetricName": "metric_a"}, {"MetricName": "metric_b"}, ...]`

Files and expected occurrence counts:
1. `src/SnmpCollector/config/devices.json` -- ~8 poll groups with MetricNames
2. `src/SnmpCollector/appsettings.Development.json` -- ~3 poll groups with MetricNames
3. `deploy/k8s/snmp-collector/simetra-devices.yaml` -- ~12 poll groups (embedded JSON inside YAML)
4. `deploy/k8s/production/configmap.yaml` -- ~10 poll groups (embedded JSON inside YAML)

IMPORTANT: For the YAML files, the JSON is embedded as a string value inside the configmap data section. Edit the JSON content within the YAML string, preserving the YAML structure. Be careful with indentation.

After editing, verify zero occurrences of "MetricNames" remain in all four files.
  </action>
  <verify>Run `grep -r "MetricNames" src/SnmpCollector/config/devices.json src/SnmpCollector/appsettings.Development.json deploy/k8s/snmp-collector/simetra-devices.yaml deploy/k8s/production/configmap.yaml` -- must return zero matches. Also spot-check that JSON is valid: `python -m json.tool src/SnmpCollector/config/devices.json > /dev/null`.</verify>
  <done>All four config files use "Metrics": [{"MetricName": "..."}] format with zero remaining "MetricNames" references.</done>
</task>

<task type="auto">
  <name>Task 3: Update unit tests</name>
  <files>
    tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs
    tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs
  </files>
  <action>
1. `AggregatedMetricModelTests.cs` (2 occurrences):
   - Find JSON string literals containing `"MetricNames": [...]`.
   - Transform to `"Metrics": [{"MetricName": "..."}, ...]` format.

2. `DeviceWatcherValidationTests.cs` (~18 occurrences):
   - Find all C# object initializer patterns like:
     `MetricNames = ["name1", "name2"]` or `MetricNames = new List<string> { ... }`
   - Transform to:
     `Metrics = [new PollMetricOptions { MetricName = "name1" }, new PollMetricOptions { MetricName = "name2" }]`
   - For single-item lists: `Metrics = [new PollMetricOptions { MetricName = "name1" }]`
   - For empty lists: `Metrics = []`
   - Add `using SnmpCollector.Configuration;` if not already present (PollMetricOptions needs to be in scope).

After editing, verify zero occurrences of "MetricNames" remain in both test files.
  </action>
  <verify>Run `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj --no-restore` -- all tests must pass. Also verify: `grep -c "MetricNames" tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` returns 0 for both files.</verify>
  <done>Both test files use new Metrics/PollMetricOptions pattern. Full test suite passes green. Zero "MetricNames" references remain anywhere in modified files.</done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- zero errors
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests pass
3. `grep -r "MetricNames" src/SnmpCollector/ deploy/k8s/ tests/SnmpCollector.Tests/Pipeline/AggregatedMetricModelTests.cs tests/SnmpCollector.Tests/Services/DeviceWatcherValidationTests.cs` -- zero matches (excluding bin/obj)
4. New file exists: `src/SnmpCollector/Configuration/PollMetricOptions.cs`
</verification>

<success_criteria>
- PollMetricOptions class exists with MetricName property
- PollOptions.Metrics is List<PollMetricOptions> (old MetricNames removed)
- DeviceWatcherService uses poll.Metrics and m.MetricName
- All 4 config files use new "Metrics": [{"MetricName": ...}] format
- All unit tests pass
- Zero remaining references to "MetricNames" in modified files
</success_criteria>

<output>
After completion, create `.planning/quick/082-metricnames-to-polloptions-object/082-SUMMARY.md`
</output>
