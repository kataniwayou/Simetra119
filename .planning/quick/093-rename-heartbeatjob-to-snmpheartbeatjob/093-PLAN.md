---
phase: quick-093
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Jobs/HeartbeatJob.cs
  - src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs
  - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
  - src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  - src/SnmpCollector/Services/PollSchedulerStartupService.cs
  - src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs
  - src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs
  - src/SnmpCollector/Pipeline/OidMapService.cs
  - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
  - src/SnmpCollector/appsettings.json
  - src/SnmpCollector/appsettings.Development.json
  - tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
  - tests/SnmpCollector.Tests/Jobs/SnmpHeartbeatJobTests.cs
  - tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
  - tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
autonomous: true

must_haves:
  truths:
    - "All references to HeartbeatJob in src/ and tests/ are renamed to SnmpHeartbeatJob"
    - "All references to HeartbeatJobOptions are renamed to SnmpHeartbeatJobOptions"
    - "Config section name is SnmpHeartbeatJob in appsettings files"
    - "Solution builds and all 524 tests pass"
  artifacts:
    - path: "src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs"
      provides: "Renamed heartbeat job class"
    - path: "src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs"
      provides: "Renamed options class with SectionName = SnmpHeartbeatJob"
    - path: "tests/SnmpCollector.Tests/Jobs/SnmpHeartbeatJobTests.cs"
      provides: "Renamed test class"
  key_links:
    - from: "src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs"
      to: "src/SnmpCollector/appsettings.json"
      via: "SectionName constant matching JSON key"
      pattern: "SnmpHeartbeatJob"
    - from: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      to: "src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs"
      via: "Quartz job registration"
      pattern: "SnmpHeartbeatJob"
---

<objective>
Rename HeartbeatJob to SnmpHeartbeatJob across the entire C# source and test codebase to better reflect domain semantics.

Purpose: The name "HeartbeatJob" is too generic. "SnmpHeartbeatJob" clarifies that this is the SNMP-specific heartbeat polling job, distinguishing it from other heartbeat concepts (liveness, leader election heartbeats).
Output: All source files, test files, and config files updated with the new name; solution builds and all tests pass.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Rename files and classes in src/</name>
  <files>
    src/SnmpCollector/Jobs/HeartbeatJob.cs
    src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs
    src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs
    src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
    src/SnmpCollector/Services/PollSchedulerStartupService.cs
    src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs
    src/SnmpCollector/Pipeline/IHeartbeatLivenessService.cs
    src/SnmpCollector/Pipeline/OidMapService.cs
    src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
    src/SnmpCollector/appsettings.json
    src/SnmpCollector/appsettings.Development.json
  </files>
  <action>
    1. Use `git mv` to rename the two source files:
       - `git mv src/SnmpCollector/Jobs/HeartbeatJob.cs src/SnmpCollector/Jobs/SnmpHeartbeatJob.cs`
       - `git mv src/SnmpCollector/Configuration/HeartbeatJobOptions.cs src/SnmpCollector/Configuration/SnmpHeartbeatJobOptions.cs`

    2. In SnmpHeartbeatJob.cs (formerly HeartbeatJob.cs):
       - Rename class `HeartbeatJob` to `SnmpHeartbeatJob`
       - Update `ILogger<HeartbeatJob>` to `ILogger<SnmpHeartbeatJob>`
       - Update any self-referencing comments

    3. In SnmpHeartbeatJobOptions.cs (formerly HeartbeatJobOptions.cs):
       - Rename class `HeartbeatJobOptions` to `SnmpHeartbeatJobOptions`
       - Change `SectionName` constant from `"HeartbeatJob"` to `"SnmpHeartbeatJob"`

    4. In ServiceCollectionExtensions.cs:
       - Replace all `HeartbeatJob` references with `SnmpHeartbeatJob`
       - Replace all `HeartbeatJobOptions` references with `SnmpHeartbeatJobOptions`
       - Change Quartz job key from `"heartbeat"` to `"snmp-heartbeat"`
       - Update intervalRegistry.Register key accordingly

    5. In PollSchedulerStartupService.cs:
       - Replace `HeartbeatJob` with `SnmpHeartbeatJob` (and HeartbeatJobOptions with SnmpHeartbeatJobOptions if referenced)

    6. In LivenessHealthCheck.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob`

    7. In IHeartbeatLivenessService.cs:
       - Update doc comments only (do NOT rename this interface — it's about heartbeat liveness, not the job)

    8. In OidMapService.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob`

    9. In OtelMetricHandler.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob`

    10. In appsettings.json and appsettings.Development.json:
        - Rename the `"HeartbeatJob"` JSON section key to `"SnmpHeartbeatJob"`

    11. Verify no remaining `HeartbeatJob` references in src/ (except IHeartbeatLivenessService which is a different concept):
        `grep -r "HeartbeatJob" src/ --include="*.cs" --include="*.json" | grep -v "IHeartbeatLiveness"`
  </action>
  <verify>
    Run: `cd src/SnmpCollector && dotnet build --no-restore 2>&1 | tail -5`
    Expected: Build succeeded with 0 errors.
    Run: `grep -r "HeartbeatJob" src/ --include="*.cs" --include="*.json" | grep -v "IHeartbeatLiveness" | grep -v "bin/" | grep -v "obj/"` should return empty.
  </verify>
  <done>All src/ files renamed and updated. Build succeeds. No stale HeartbeatJob references remain in source.</done>
</task>

<task type="auto">
  <name>Task 2: Rename files and references in tests/</name>
  <files>
    tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
    tests/SnmpCollector.Tests/Jobs/SnmpHeartbeatJobTests.cs
    tests/SnmpCollector.Tests/HealthChecks/LivenessHealthCheckTests.cs
    tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs
    tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
  </files>
  <action>
    1. Use `git mv` to rename the test file:
       - `git mv tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs tests/SnmpCollector.Tests/Jobs/SnmpHeartbeatJobTests.cs`

    2. In SnmpHeartbeatJobTests.cs (formerly HeartbeatJobTests.cs):
       - Rename class `HeartbeatJobTests` to `SnmpHeartbeatJobTests`
       - Replace all `HeartbeatJob` with `SnmpHeartbeatJob`
       - Replace all `HeartbeatJobOptions` with `SnmpHeartbeatJobOptions`
       - Update `ILogger<HeartbeatJob>` to `ILogger<SnmpHeartbeatJob>`

    3. In LivenessHealthCheckTests.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob` (and HeartbeatJobOptions with SnmpHeartbeatJobOptions)

    4. In OtelMetricHandlerTests.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob`

    5. In OidMapServiceTests.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob`

    6. In OidResolutionBehaviorTests.cs:
       - Replace `HeartbeatJob` references with `SnmpHeartbeatJob`

    7. Verify no remaining stale references:
       `grep -r "HeartbeatJob" tests/ --include="*.cs" | grep -v "bin/" | grep -v "obj/"` should return empty.
  </action>
  <verify>
    Run: `cd tests/SnmpCollector.Tests && dotnet test --no-restore 2>&1 | tail -10`
    Expected: All 524 tests pass, 0 failures.
    Run: `grep -r "HeartbeatJob" tests/ --include="*.cs" | grep -v "bin/" | grep -v "obj/"` should return empty.
  </verify>
  <done>All test files renamed and updated. All 524 tests pass. No stale HeartbeatJob references in tests/.</done>
</task>

</tasks>

<verification>
1. `dotnet build` succeeds across the solution
2. `dotnet test` passes all 524 tests
3. `grep -r "HeartbeatJob" src/ tests/ --include="*.cs" --include="*.json" | grep -v "IHeartbeatLiveness" | grep -v "bin/" | grep -v "obj/"` returns empty
4. `git diff --name-status` shows proper renames (R status) for the 3 renamed files
</verification>

<success_criteria>
- SnmpHeartbeatJob.cs exists, HeartbeatJob.cs does not (in src/)
- SnmpHeartbeatJobOptions.cs exists, HeartbeatJobOptions.cs does not (in src/)
- SnmpHeartbeatJobTests.cs exists, HeartbeatJobTests.cs does not (in tests/)
- Config sections use "SnmpHeartbeatJob" key
- Solution builds with 0 errors
- All 524 tests pass
- Zero remaining HeartbeatJob references (except IHeartbeatLivenessService)
</success_criteria>

<output>
After completion, create `.planning/quick/093-rename-heartbeatjob-to-snmpheartbeatjob/093-SUMMARY.md`
</output>
