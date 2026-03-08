---
phase: quick
plan: 020
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Telemetry/PipelineMetricService.cs
  - src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
  - tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
  - tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs
autonomous: true

must_haves:
  truths:
    - "No metric emitted by PipelineMetricService or SnmpMetricFactory contains host_name or pod_name tags"
    - "All metrics still carry device_name (and business metrics still carry metric_name, oid, ip, source, snmp_type)"
    - "OTel resource attributes service_instance_id and k8s_pod_name still provide host/pod identity at the resource level"
    - "All tests pass after tag removal"
  artifacts:
    - path: "src/SnmpCollector/Telemetry/PipelineMetricService.cs"
      provides: "Pipeline counters without host_name/pod_name tags"
      contains: "TagList"
    - path: "src/SnmpCollector/Telemetry/SnmpMetricFactory.cs"
      provides: "Business metric gauges without host_name/pod_name tags"
      contains: "TagList"
  key_links:
    - from: "src/SnmpCollector/Telemetry/PipelineMetricService.cs"
      to: "OTel resource attributes"
      via: "Resource-level service_instance_id replaces per-metric host_name"
---

<objective>
Remove redundant `host_name` and `pod_name` tags from all metric TagLists in PipelineMetricService and SnmpMetricFactory.

Purpose: These tags duplicate OTel resource attributes `service_instance_id` and `k8s_pod_name` (set in ServiceCollectionExtensions.cs), which Prometheus already surfaces as `instance` and `k8s_pod_name`. Removing them cuts metric cardinality and eliminates the redundant Environment.GetEnvironmentVariable calls.

Output: Both metric classes emit tags without host_name/pod_name; tests updated to match.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Telemetry/PipelineMetricService.cs
@src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
@tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
@tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Remove host_name/pod_name from PipelineMetricService and its tests</name>
  <files>
    src/SnmpCollector/Telemetry/PipelineMetricService.cs
    tests/SnmpCollector.Tests/Telemetry/PipelineMetricServiceTests.cs
  </files>
  <action>
In PipelineMetricService.cs:
1. Remove the `_hostName` field (line 14) and `_podName` field (line 15).
2. Remove the two Environment.GetEnvironmentVariable lines in the constructor (lines 53-54).
3. In ALL 11 Increment* methods, change the TagList from:
   `new TagList { { "host_name", _hostName }, { "pod_name", _podName }, { "device_name", deviceName } }`
   to:
   `new TagList { { "device_name", deviceName } }`

In PipelineMetricServiceTests.cs:
1. Remove all `Assert.True(tags.ContainsKey("host_name"))` assertions (lines 67, 84, 101, 118).
2. Remove `Assert.DoesNotContain("device_name", tags.Keys)` lines where present — these tested that certain trap methods had no device_name tag which may still be valid depending on signatures.
   NOTE: Several test methods call `IncrementTrapAuthFailed()`, `IncrementTrapUnknownDevice()`, and `IncrementTrapReceived()` with zero arguments, but the source requires `string deviceName`. If these tests don't compile, fix the test calls to pass a device name string (e.g., `"test-device"`) and update assertions accordingly — replace `Assert.DoesNotContain("device_name", ...)` with `Assert.Equal("test-device", tags["device_name"])`.
  </action>
  <verify>
Run: `dotnet build src/SnmpCollector/SnmpCollector.csproj` — no errors.
Run: `dotnet test tests/SnmpCollector.Tests/ --filter "FullyQualifiedName~PipelineMetricServiceTests"` — all pass.
Grep PipelineMetricService.cs for `host_name` and `pod_name` — zero matches.
  </verify>
  <done>PipelineMetricService emits only device_name on all 11 counters. No _hostName or _podName fields remain. Tests updated and passing.</done>
</task>

<task type="auto">
  <name>Task 2: Remove host_name/pod_name from SnmpMetricFactory and its tests</name>
  <files>
    src/SnmpCollector/Telemetry/SnmpMetricFactory.cs
    tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs
  </files>
  <action>
In SnmpMetricFactory.cs:
1. Remove the `_hostName` field (line 17) and `_podName` field (line 18).
2. Remove the two Environment.GetEnvironmentVariable lines in the constructor (lines 35-36).
3. In RecordGauge, change the TagList to remove `{ "host_name", _hostName }` and `{ "pod_name", _podName }`. Keep the remaining 6 tags: metric_name, oid, device_name, ip, source, snmp_type.
4. In RecordInfo, change the TagList to remove `{ "host_name", _hostName }` and `{ "pod_name", _podName }`. Keep the remaining 7 tags: metric_name, oid, device_name, ip, source, snmp_type, value.

In SnmpMetricFactoryTests.cs:
1. Update the class-level doc comment to remove the "(e.g., host_name)" reference.
2. In RecordGauge_IncludesAllEightLabels: Remove the two `Assert.True(tags.ContainsKey("host_name"))` and `Assert.True(tags.ContainsKey("pod_name"))` lines. Change `Assert.Equal(8, tags.Count)` to `Assert.Equal(6, tags.Count)`. Rename the test method to `RecordGauge_IncludesAllSixLabels`.
3. In RecordInfo_IncludesAllNineLabels: Remove the two host_name/pod_name assertions. Change `Assert.Equal(9, tags.Count)` to `Assert.Equal(7, tags.Count)`. Rename the test method to `RecordInfo_IncludesAllSevenLabels`.
  </action>
  <verify>
Run: `dotnet build src/SnmpCollector/SnmpCollector.csproj` — no errors.
Run: `dotnet test tests/SnmpCollector.Tests/ --filter "FullyQualifiedName~SnmpMetricFactoryTests"` — all pass.
Grep SnmpMetricFactory.cs for `host_name` and `pod_name` — zero matches.
  </verify>
  <done>SnmpMetricFactory emits 6 tags on snmp_gauge and 7 tags on snmp_info (no host_name/pod_name). Tests updated with correct counts and passing.</done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles without errors.
2. `dotnet test tests/SnmpCollector.Tests/` — all tests pass (not just the modified ones).
3. `grep -r "host_name\|pod_name" src/SnmpCollector/Telemetry/PipelineMetricService.cs src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` returns no matches.
4. `grep -r "host_name\|pod_name" src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs` still returns matches (logs untouched, as intended).
</verification>

<success_criteria>
- Zero occurrences of host_name or pod_name in PipelineMetricService.cs and SnmpMetricFactory.cs
- No _hostName or _podName fields or Environment.GetEnvironmentVariable calls in either class
- All unit tests pass with updated tag count assertions
- SnmpLogEnrichmentProcessor.cs unchanged (logs still have host_name)
- Full test suite green
</success_criteria>

<output>
After completion, create `.planning/quick/020-remove-redundant-host-pod-tags/020-SUMMARY.md`
</output>
