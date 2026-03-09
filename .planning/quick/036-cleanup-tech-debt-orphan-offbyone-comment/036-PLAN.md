---
phase: quick-036
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
  - src/SnmpCollector/Pipeline/DeviceRegistry.cs
  - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
  - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
  - src/SnmpCollector/Services/PollSchedulerStartupService.cs
  - deploy/k8s/production/grafana.yaml
autonomous: true

must_haves:
  truths:
    - "TryGetDevice(IPAddress) does not exist in interface or implementation"
    - "_byIp dictionary and all IP-lookup plumbing removed from DeviceRegistry"
    - "PollSchedulerStartupService thread pool log accounts for HeartbeatJob"
    - "grafana.yaml comments reference current dashboard files only"
  artifacts:
    - path: "src/SnmpCollector/Pipeline/IDeviceRegistry.cs"
      provides: "Interface without TryGetDevice(IPAddress)"
      contains: "TryGetDeviceByName"
    - path: "src/SnmpCollector/Pipeline/DeviceRegistry.cs"
      provides: "Implementation without _byIp dictionary"
    - path: "src/SnmpCollector/Services/PollSchedulerStartupService.cs"
      provides: "Corrected thread pool calculation"
      contains: "+2"
    - path: "deploy/k8s/production/grafana.yaml"
      provides: "Accurate dashboard file references"
      contains: "simetra-business.json"
  key_links: []
---

<objective>
Clean up three known tech debt items in a single pass: remove orphaned IP-based device lookup, fix thread pool log off-by-one, and update stale Grafana comments.

Purpose: Eliminate dead code and inaccurate comments that mislead future readers.
Output: Three atomic commits, each addressing one debt item.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Pipeline/IDeviceRegistry.cs
@src/SnmpCollector/Pipeline/DeviceRegistry.cs
@tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
@tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
@src/SnmpCollector/Services/PollSchedulerStartupService.cs
@deploy/k8s/production/grafana.yaml
</context>

<tasks>

<task type="auto">
  <name>Task 1: Remove orphaned TryGetDevice(IPAddress) and _byIp plumbing</name>
  <files>
    src/SnmpCollector/Pipeline/IDeviceRegistry.cs
    src/SnmpCollector/Pipeline/DeviceRegistry.cs
    tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
    tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
  </files>
  <action>
    Nothing calls TryGetDevice(IPAddress) in production code. The community-string convention
    (Simetra.{DeviceName}) replaced IP-based device lookup. Remove all traces:

    **IDeviceRegistry.cs:**
    - Remove the TryGetDevice(IPAddress, out DeviceInfo?) method (lines 13-21)
    - Remove `using System.Net;` (no longer needed -- verify no other member uses IPAddress)
    - Remove `using System.Diagnostics.CodeAnalysis;` if NotNullWhen is only used by TryGetDevice
      (check: TryGetDeviceByName also uses [NotNullWhen] so keep it)
    - Keep: TryGetDeviceByName, AllDevices, ReloadAsync

    **DeviceRegistry.cs:**
    - Remove the `_byIp` field (line 19)
    - Remove all `byIpBuilder` code in the constructor (lines 35, 61, 65) -- the Dictionary creation,
      the `byIpBuilder[ip] = info;` assignment, and the `_byIp = byIpBuilder.ToFrozenDictionary();`
    - Remove the TryGetDevice method (lines 70-73)
    - In ReloadAsync: remove `byIpBuilder` dictionary creation (line 89), `byIpBuilder[ip] = info;`
      (line 116), `newByIp` creation (line 119), and `_byIp = newByIp;` (line 123)
    - KEEP the IP resolution logic (lines 40-50 in constructor, 95-104 in ReloadAsync) -- DeviceInfo
      still stores the resolved IpAddress string. The `ip` local variable is still needed to set
      `ip.ToString()` in the DeviceInfo constructor call.
    - Remove `using System.Collections.Frozen;` only if FrozenDictionary is no longer used
      (it IS still used for `_byName`, so KEEP it)
    - Update the class XML doc (lines 11-15) to remove mention of "normalized IPv4 addresses" --
      it should say "device names" only

    **DeviceRegistryTests.cs:**
    - Remove three tests: TryGetDevice_KnownIp_ReturnsDevice (lines 50-60),
      TryGetDevice_Ipv6Mapped_ReturnsDevice (lines 62-73),
      TryGetDevice_UnknownIp_ReturnsFalse (lines 75-84)
    - Keep all TryGetDeviceByName tests, AllDevices, Constructor, ReloadAsync, and JobKey tests

    **MetricPollJobTests.cs (StubDeviceRegistry class around line 370):**
    - Remove the TryGetDevice(IPAddress, out DeviceInfo?) method from StubDeviceRegistry (lines 378-383)
    - Remove `using System.Net;` from the file ONLY if no other code in the file uses IPAddress/System.Net
      (check first -- the stub itself uses IPAddress.Parse, so after removing TryGetDevice the using
      may become unused. Verify by checking remaining code.)

    Commit this task atomically:
    `fix: remove orphaned TryGetDevice(IPAddress) and _byIp lookup plumbing`
  </action>
  <verify>
    Run `dotnet build` from `src/SnmpCollector/` -- must compile with zero errors and zero warnings.
    Run `dotnet test` from `tests/SnmpCollector.Tests/` -- all remaining tests pass.
    Grep for `TryGetDevice(IPAddress` across entire repo -- zero matches.
    Grep for `_byIp` across entire repo -- zero matches.
  </verify>
  <done>
    TryGetDevice(IPAddress) removed from interface, implementation, all test stubs, and all test
    invocations. _byIp dictionary and all IP-keyed FrozenDictionary plumbing removed. Build and
    tests green.
  </done>
</task>

<task type="auto">
  <name>Task 2: Fix PollSchedulerStartupService thread pool log off-by-one</name>
  <files>
    src/SnmpCollector/Services/PollSchedulerStartupService.cs
  </files>
  <action>
    Line 32 currently reads:
    ```csharp
    var threadPoolSize = pollJobCount + 1; // +1 for CorrelationJob
    ```

    HeartbeatJob also runs on the Quartz scheduler but is not counted. Change to:
    ```csharp
    var threadPoolSize = pollJobCount + 2; // +1 CorrelationJob, +1 HeartbeatJob
    ```

    This is cosmetic -- Quartz auto-sizes its thread pool regardless -- but the log should be accurate.

    Commit atomically:
    `fix: account for HeartbeatJob in thread pool size log`
  </action>
  <verify>
    Run `dotnet build` from `src/SnmpCollector/` -- compiles cleanly.
    Read the file and confirm the comment and value are correct.
  </verify>
  <done>
    Thread pool size calculation includes +2 (CorrelationJob + HeartbeatJob) with accurate comment.
  </done>
</task>

<task type="auto">
  <name>Task 3: Update stale grafana.yaml dashboard comments</name>
  <files>
    deploy/k8s/production/grafana.yaml
  </files>
  <action>
    Lines 12-14 currently read:
    ```yaml
    #      - simetra-operations.json
    #      - npb-device.json
    #      - obp-device.json
    ```

    npb-device.json and obp-device.json no longer exist. Replace with the current dashboard files:
    ```yaml
    #      - simetra-operations.json
    #      - simetra-business.json
    ```

    Commit atomically:
    `fix: update grafana.yaml comments to reference current dashboards`
  </action>
  <verify>
    Read the file and confirm only simetra-operations.json and simetra-business.json are listed.
    Verify no other stale references exist in the file.
  </verify>
  <done>
    grafana.yaml comment block lists exactly the two current dashboard JSON files.
  </done>
</task>

</tasks>

<verification>
- `dotnet build` in `src/SnmpCollector/` succeeds with zero errors/warnings
- `dotnet test` in `tests/SnmpCollector.Tests/` passes all tests
- `grep -r "TryGetDevice(IPAddress" .` returns zero matches
- `grep -r "_byIp" .` returns zero matches
- PollSchedulerStartupService shows `pollJobCount + 2`
- grafana.yaml references simetra-operations.json and simetra-business.json only
</verification>

<success_criteria>
All three tech debt items resolved. Build green, tests green, three atomic commits.
</success_criteria>

<output>
No summary file needed for quick plans.
</output>
