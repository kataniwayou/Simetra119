---
phase: quick-094
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
autonomous: true

must_haves:
  truths:
    - "PreferredHeartbeatJob does not register when LeaseOptions.PreferredNode is empty/null"
    - "PreferredHeartbeatJobOptions validation does not fire when PreferredNode is empty/null"
    - "initialJobCount does not include PreferredHeartbeatJob when it is not registered"
    - "No behavioral change when PreferredNode IS configured (existing path unchanged)"
  artifacts:
    - path: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
      provides: "Gated PreferredHeartbeatJob registration"
      contains: "PreferredNode"
  key_links:
    - from: "AddSnmpScheduling"
      to: "LeaseOptions.PreferredNode"
      via: "config bind + string.IsNullOrWhiteSpace check"
    - from: "AddSnmpConfiguration"
      to: "LeaseOptions.PreferredNode"
      via: "config bind + string.IsNullOrWhiteSpace check"
---

<objective>
Gate PreferredHeartbeatJob and its options validation behind LeaseOptions.PreferredNode being non-empty.

Purpose: When the preferred-leader feature is off (PreferredNode empty/null), the job fires every tick and hits a 404 on the K8s API. This is noisy and wasteful. Gate registration so the job and its validation only exist when the feature is actually configured.

Output: Modified ServiceCollectionExtensions.cs with conditional registration in both AddSnmpScheduling and AddSnmpConfiguration.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
@src/SnmpCollector/Configuration/LeaseOptions.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Gate PreferredHeartbeatJob registration and options validation on PreferredNode</name>
  <files>src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs</files>
  <action>
Three changes in ServiceCollectionExtensions.cs:

1. **AddSnmpConfiguration** (around line 217-220): Wrap the `PreferredHeartbeatJobOptions` registration block in a condition. Bind LeaseOptions from config to read PreferredNode, then only register if PreferredNode is non-empty AND IsInCluster():

```csharp
// Only validate PreferredHeartbeatJobOptions when the feature is active.
var leaseSection = configuration.GetSection(LeaseOptions.SectionName);
var preferredNode = leaseSection.GetValue<string>(nameof(LeaseOptions.PreferredNode));
if (k8s.KubernetesClientConfiguration.IsInCluster()
    && !string.IsNullOrWhiteSpace(preferredNode))
{
    services.AddOptions<PreferredHeartbeatJobOptions>()
        .Bind(configuration.GetSection(PreferredHeartbeatJobOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
}
```

Note: Read PreferredNode with `GetValue<string>` from the Lease section directly -- do NOT bind a full LeaseOptions object here because LeaseOptions has `required` properties that would throw if not fully configured. LeaseOptions is already bound properly later inside the IsInCluster() block.

2. **AddSnmpScheduling** (around line 509-511): Change the `initialJobCount` increment and the Quartz registration block (line 570-583) to also check PreferredNode. Read it the same way:

```csharp
var preferredNode = configuration.GetSection(LeaseOptions.SectionName)
    .GetValue<string>(nameof(LeaseOptions.PreferredNode));
var preferredFeatureActive = k8s.KubernetesClientConfiguration.IsInCluster()
    && !string.IsNullOrWhiteSpace(preferredNode);
```

Place this before `initialJobCount` is computed (before line 509). Then:
- Line 510-511: Change `if (k8s.KubernetesClientConfiguration.IsInCluster())` to `if (preferredFeatureActive)`.
- Line 570: Change `if (k8s.KubernetesClientConfiguration.IsInCluster())` to `if (preferredFeatureActive)`.

3. **Remove the now-unused** `preferredHbOptions` binding (lines 493-494) from the unconditional section and move it INSIDE the `if (preferredFeatureActive)` block in the Quartz config, just before it's used. This avoids binding options that will never be read:

```csharp
if (preferredFeatureActive)
{
    var preferredHbOptions = new PreferredHeartbeatJobOptions();
    configuration.GetSection(PreferredHeartbeatJobOptions.SectionName).Bind(preferredHbOptions);

    var preferredHbKey = new JobKey("preferred-heartbeat");
    // ... rest of registration
}
```

Update comments to explain the gating logic.
  </action>
  <verify>
Run from repo root:
- `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- must compile clean
- `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all 524 tests pass

Manually verify: grep for `preferredFeatureActive` and `PreferredNode` in the file to confirm both AddSnmpConfiguration and AddSnmpScheduling have the gate.
  </verify>
  <done>
PreferredHeartbeatJob and its options validation are only registered when PreferredNode is non-empty AND running in K8s. initialJobCount only increments for PreferredHeartbeatJob when it will actually be registered. Build passes, all 524 tests pass.
  </done>
</task>

</tasks>

<verification>
- `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles without errors or warnings
- `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all 524 tests pass
- `grep -n "preferredFeatureActive\|PreferredNode" src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` shows gating in both methods
</verification>

<success_criteria>
- PreferredHeartbeatJob Quartz registration gated on PreferredNode non-empty + IsInCluster()
- PreferredHeartbeatJobOptions ValidateOnStart gated on same condition
- initialJobCount only includes PreferredHeartbeatJob when it will be registered
- All 524 existing tests pass (no behavioral change)
- No new files created -- single file modification
</success_criteria>

<output>
After completion, create `.planning/quick/094-skip-heartbeat-job-when-preferred-node-empty/094-SUMMARY.md`
</output>
