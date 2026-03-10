---
phase: quick-037
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Pipeline/IDeviceRegistry.cs
  - src/SnmpCollector/Pipeline/DeviceRegistry.cs
  - src/SnmpCollector/Pipeline/MetricPollInfo.cs
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  - src/SnmpCollector/Services/DynamicPollScheduler.cs
  - src/SnmpCollector/Jobs/MetricPollJob.cs
  - src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
  - tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
  - tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
  - tests/SnmpCollector.Tests/Services/DynamicPollSchedulerTests.cs
autonomous: true

must_haves:
  truths:
    - "Two devices with same IP+Port are rejected at startup with clear error message"
    - "Two devices with same Name but different IP+Port are accepted"
    - "MetricPollJob resolves device by IP+Port from JobDataMap, not by Name"
    - "Job keys use ip_port format so dynamic reload correctly diffs jobs"
    - "TryGetDeviceByName still works for trap listener compatibility"
  artifacts:
    - path: "src/SnmpCollector/Pipeline/IDeviceRegistry.cs"
      provides: "TryGetByIpPort method on interface"
      contains: "TryGetByIpPort"
    - path: "src/SnmpCollector/Pipeline/DeviceRegistry.cs"
      provides: "FrozenDictionary keyed by (ip,port) as primary, _byName as secondary"
      contains: "_byIpPort"
    - path: "src/SnmpCollector/Pipeline/MetricPollInfo.cs"
      provides: "Updated JobKey using ip_port format"
      contains: "metric-poll-"
    - path: "src/SnmpCollector/Jobs/MetricPollJob.cs"
      provides: "Lookup by ipAddress+port instead of deviceName"
      contains: "TryGetByIpPort"
  key_links:
    - from: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      to: "src/SnmpCollector/Jobs/MetricPollJob.cs"
      via: "JobDataMap passes ipAddress and port instead of deviceName"
      pattern: 'UsingJobData\("ipAddress"'
    - from: "src/SnmpCollector/Services/DynamicPollScheduler.cs"
      to: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      via: "Same job key format ip_port used in both startup and runtime reconciliation"
      pattern: "metric-poll-.*_"
---

<objective>
Change DeviceRegistry primary key from device Name to (IP, Port) tuple. Update all Quartz job keys, MetricPollJob lookup, DynamicPollScheduler reconciliation, and config validation to use IP+Port as the canonical device identity. Keep TryGetDeviceByName as secondary lookup for trap listener compatibility.

Purpose: Device Name is a human label, not a unique network identity. Two devices could theoretically share a name (e.g., after rename) but IP+Port is the true SNMP endpoint identity. This prevents silent misconfiguration where two config entries target the same SNMP agent.

Output: Updated DeviceRegistry, job key format, MetricPollJob, DynamicPollScheduler, validator, and all affected unit tests.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Pipeline/IDeviceRegistry.cs
@src/SnmpCollector/Pipeline/DeviceRegistry.cs
@src/SnmpCollector/Pipeline/DeviceInfo.cs
@src/SnmpCollector/Pipeline/MetricPollInfo.cs
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
@src/SnmpCollector/Services/DynamicPollScheduler.cs
@src/SnmpCollector/Jobs/MetricPollJob.cs
@src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
@tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
@tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
@tests/SnmpCollector.Tests/Services/DynamicPollSchedulerTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: DeviceRegistry keyed by IP+Port with duplicate validation</name>
  <files>
    src/SnmpCollector/Pipeline/IDeviceRegistry.cs
    src/SnmpCollector/Pipeline/DeviceRegistry.cs
    src/SnmpCollector/Pipeline/MetricPollInfo.cs
    src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs
    tests/SnmpCollector.Tests/Pipeline/DeviceRegistryTests.cs
  </files>
  <action>
    **IDeviceRegistry.cs:**
    - Add method: `bool TryGetByIpPort(string ipAddress, int port, [NotNullWhen(true)] out DeviceInfo? device);`
    - Keep `TryGetDeviceByName` (trap listener still needs it).
    - Update `ReloadAsync` return type: change from `(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)` where strings are names, to `(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)` where strings are `"{ip}:{port}"` identity keys. Update XML doc to clarify the returned sets contain IP:Port keys, not device names.

    **DeviceRegistry.cs:**
    - Add field: `private volatile FrozenDictionary<string, DeviceInfo> _byIpPort;` keyed by `"{ip}:{port}"` string.
    - Keep `_byName` as secondary lookup (for trap listener).
    - Add private helper: `private static string IpPortKey(string ip, int port) => $"{ip}:{port}";`
    - Constructor: build both `_byIpPort` (primary, OrdinalIgnoreCase) and `_byName` (secondary, OrdinalIgnoreCase). Before building, validate no duplicate IP+Port exists -- if found, throw `InvalidOperationException($"Duplicate IP+Port {ip}:{port} in device configuration (devices: '{name1}', '{name2}')")`. This will surface as a DI failure at startup with clear message.
    - `TryGetByIpPort`: lookup in `_byIpPort`.
    - `ReloadAsync`: same duplicate IP+Port check (throw on duplicate). Build both dictionaries. Diff uses `_byIpPort` keys (ip:port strings) for Added/Removed sets. Atomic swap both `_byIpPort` and `_byName`.

    **MetricPollInfo.cs:**
    - Change `JobKey` method signature to: `public string JobKey(string ipAddress, int port) => $"metric-poll-{ipAddress}_{port}-{PollIndex}";`
    - Use underscore between ip and port (colons are problematic in Quartz job key names on some systems).

    **DevicesOptionsValidator.cs:**
    - In `ValidateNoDuplicates`: replace `seenIps` (checking IP alone) with `seenIpPorts` checking `"{ip}:{port}"` composite key. Error message: `$"Devices[{i}] IP+Port '{ip}:{port}' is a duplicate -- each device must have a unique IP+Port combination"`.
    - Remove the duplicate Name check entirely (duplicate names are now allowed per the user's specification).

    **DeviceRegistryTests.cs:**
    - Update `TryGetDeviceByName_*` tests: these should still pass (secondary lookup preserved).
    - Add test: `TryGetByIpPort_ExactMatch_ReturnsDevice` -- lookup by ip+port returns correct device.
    - Add test: `TryGetByIpPort_Unknown_ReturnsFalse`.
    - Add test: `Constructor_DuplicateIpPort_ThrowsInvalidOperationException` -- two devices with same ip+port but different names throws.
    - Add test: `Constructor_DuplicateName_DifferentIpPort_Accepted` -- two devices with same name but different IP+Port succeeds (no throw).
    - Update `JobKey_ProducesCorrectIdentity` test to use new signature `pollInfo.JobKey("10.0.10.1", 161)` and assert format `"metric-poll-10.0.10.1_161-0"`.
    - Update `ReloadAsync_*` tests: added/removed sets now contain ip:port keys (e.g., "10.0.10.3:161") instead of names. Update assertions accordingly.
  </action>
  <verify>
    Run: `dotnet test tests/SnmpCollector.Tests/ --filter "FullyQualifiedName~DeviceRegistryTests" --no-restore`
    All existing and new tests pass.
  </verify>
  <done>
    DeviceRegistry has _byIpPort as primary and _byName as secondary lookup. Duplicate IP+Port rejected at construction. MetricPollInfo.JobKey uses ip_port format. All DeviceRegistryTests pass.
  </done>
</task>

<task type="auto">
  <name>Task 2: Job keys, MetricPollJob lookup, and DynamicPollScheduler use IP+Port</name>
  <files>
    src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    src/SnmpCollector/Services/DynamicPollScheduler.cs
    src/SnmpCollector/Jobs/MetricPollJob.cs
    tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs
    tests/SnmpCollector.Tests/Services/DynamicPollSchedulerTests.cs
  </files>
  <action>
    **ServiceCollectionExtensions.cs (AddSnmpScheduling method, lines 456-480):**
    - Change job key from `$"metric-poll-{device.Name}-{pi}"` to `$"metric-poll-{device.IpAddress}_{device.Port}-{pi}"`.
    - Change JobDataMap: replace `UsingJobData("deviceName", device.Name)` with `UsingJobData("ipAddress", device.IpAddress)` and add `UsingJobData("port", device.Port)`. Keep `UsingJobData("pollIndex", pi)` and `UsingJobData("intervalSeconds", poll.IntervalSeconds)`.
    - Update trigger identity to match: `$"metric-poll-{device.IpAddress}_{device.Port}-{pi}-trigger"`.
    - Update intervalRegistry.Register key to match new job key format.

    **DynamicPollScheduler.cs:**
    - In `ReconcileAsync`, change desired job name from `$"{JobPrefix}{device.Name}-{pi}"` to `$"{JobPrefix}{device.IpAddress}_{device.Port}-{pi}"` (line 72).
    - In `ScheduleJobAsync`, change JobDataMap from `UsingJobData("deviceName", device.Name)` to `UsingJobData("ipAddress", device.IpAddress)` and add `UsingJobData("port", device.Port)`. Keep pollIndex and intervalSeconds.

    **MetricPollJob.cs:**
    - In `Execute`, replace:
      ```csharp
      var deviceName = map.GetString("deviceName")!;
      ```
      with:
      ```csharp
      var ipAddress = map.GetString("ipAddress")!;
      var port = map.GetInt("port");
      ```
    - Replace `_deviceRegistry.TryGetDeviceByName(deviceName, out var device)` with `_deviceRegistry.TryGetByIpPort(ipAddress, port, out var device)`.
    - Update the warning log message from `"device '{DeviceName}' not found"` to `"device at {IpAddress}:{Port} not found"` with the ip/port structured params.
    - The rest of the method continues to use `device.Name` for logging, metrics, and pipeline dispatch (unchanged -- Name is still on DeviceInfo for labels).

    **MetricPollJobTests.cs:**
    - Update `StubDeviceRegistry`: add `TryGetByIpPort` implementation that matches on IpAddress+Port from the device list. Keep existing `TryGetDeviceByName`.
    - Update `StubJobExecutionContext` constructor: change JobDataMap from `["deviceName"] = deviceName` to `["ipAddress"] = ipAddress, ["port"] = port`. Update the constructor signature to accept `string ipAddress, int port` instead of `string deviceName`. Update the job identity to `$"metric-poll-{ipAddress}_{port}-{pollIndex}"`.
    - Update `MakeContext` helper: change from `string deviceName = DeviceName` to `string ipAddress = DeviceIp, int port = DevicePort`.
    - Update ALL test call sites using `MakeContext`: replace `MakeContext("nonexistent-device")` with `MakeContext("99.99.99.99", 9999)` (for not-found tests) and `MakeContext(deviceName: "custom-device")` with `MakeContext(ipAddress: "10.0.0.99", port: 1161)`.
    - Test 1 (device not found): use `MakeContext("99.99.99.99", 9999)` with an empty registry.
    - Test 3 (custom port): use `MakeContext(ipAddress: "10.0.0.99", port: 1161)`.

    **DynamicPollSchedulerTests.cs:**
    - Update all `SetupExistingJobs` calls: change `"metric-poll-DEV-01-0"` to `"metric-poll-127.0.0.1_161-0"` (matches the MakeDevice helper which uses ip=127.0.0.1, port=161).
    - Update all `_intervalRegistry` stub setups to use the new key format.
    - Update assertions that check job key strings.
    - The `MakeDevice` helper already has `IpAddress = "127.0.0.1"` and `Port = 161`, so the desired key naturally becomes `"metric-poll-127.0.0.1_161-0"`.
  </action>
  <verify>
    Run: `dotnet test tests/SnmpCollector.Tests/ --no-restore`
    ALL tests pass (not just the ones we modified -- full suite).
    Then: `dotnet build src/SnmpCollector/ --no-restore` to confirm no compile errors.
  </verify>
  <done>
    Job keys use `metric-poll-{ip}_{port}-{pollIndex}` format everywhere. MetricPollJob resolves device by IP+Port. DynamicPollScheduler reconciles using IP+Port keys. All unit tests pass. Application compiles cleanly.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/ --no-restore` -- compiles without errors or warnings
2. `dotnet test tests/SnmpCollector.Tests/ --no-restore` -- all tests pass
3. Grep for old patterns to confirm no stragglers:
   - `grep -r "UsingJobData(\"deviceName\"" src/` returns no matches
   - `grep -r "TryGetDeviceByName" src/SnmpCollector/Jobs/` returns no matches (MetricPollJob no longer uses it)
   - `grep -r "TryGetDeviceByName" src/SnmpCollector/Pipeline/` still returns IDeviceRegistry.cs and DeviceRegistry.cs (preserved for trap listener)
   - `grep -r "metric-poll-.*Name" src/SnmpCollector/` returns no matches (old name-based job keys gone)
</verification>

<success_criteria>
- DeviceRegistry primary lookup is by (IP, Port); secondary by Name preserved
- Duplicate IP+Port rejected at startup and during reload with clear error
- Duplicate Name allowed (no longer validated as unique)
- Job keys format: `metric-poll-{ip}_{port}-{pollIndex}` in both startup and runtime
- MetricPollJob uses TryGetByIpPort for device resolution
- All existing tests updated and passing; new tests for IP+Port lookup and duplicate rejection
- Full test suite green, clean build
</success_criteria>

<output>
After completion, create `.planning/quick/037-ip-port-primary-device-identity/037-SUMMARY.md`
</output>
