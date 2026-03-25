# Phase 84: Config and Interface Foundation - Research

**Researched:** 2026-03-25
**Domain:** .NET Options pattern, interface contracts, DI registration (C# / ASP.NET Core)
**Confidence:** HIGH

## Summary

This phase adds three things: a new nullable string field on `LeaseOptions`, a new interface
`IPreferredStampReader`, and startup logic to resolve `_isPreferredPod` from `PHYSICAL_HOSTNAME`.
The entire domain is internal to this codebase — no third-party library investigation required.

The codebase already uses `PHYSICAL_HOSTNAME` (mapped from `spec.nodeName`) everywhere for node
identity. The CONTEXT.md says "NODE_NAME" in some places but the actual env var in the deployment
manifest and all production code is `PHYSICAL_HOSTNAME`. That is the env var to read.

The Options pattern, validators, and DI registration order are well-established in
`ServiceCollectionExtensions.cs`. New additions follow the same patterns exactly.

**Primary recommendation:** Follow existing patterns verbatim. One field on `LeaseOptions`,
one interface in `SnmpCollector.Telemetry`, one stub implementation (no-op / null object),
DI registration inside the existing `if (IsInCluster())` block.

## Standard Stack

### Core (already in use — no new packages)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| `Microsoft.Extensions.Options` | bundled | Options binding, ValidateOnStart | Already used by all options classes |
| `Microsoft.Extensions.DependencyInjection` | bundled | DI registration | Already used by `ServiceCollectionExtensions` |
| `System.ComponentModel.DataAnnotations` | bundled | `[Required]`, `[Range]` attributes | Already used by `LeaseOptions` |
| `Microsoft.Extensions.Logging` | bundled | `ILogger<T>` for startup log line | Already used throughout |

No new packages. This phase is pure code structure.

### Installation

```bash
# No new packages required
```

## Architecture Patterns

### Recommended Project Structure

New files for this phase:

```
src/SnmpCollector/
├── Configuration/
│   └── LeaseOptions.cs              # MODIFY: add PreferredNode property
├── Telemetry/
│   ├── IPreferredStampReader.cs     # NEW: single-bool interface
│   └── NullPreferredStampReader.cs  # NEW: no-op implementation (feature off)
```

No new top-level folders. All new types live in their natural existing namespaces.

### Pattern 1: Extending LeaseOptions with a nullable optional field

**What:** Add `public string? PreferredNode { get; set; }` — no `[Required]`, no `[Range]`.
The field is intentionally nullable and optional. Feature is disabled when null or empty.

**When to use:** When a config field is backward-compatible and optional (feature-flag style).

**Example:**
```csharp
// In LeaseOptions.cs — follow the existing style exactly
/// <summary>
/// Optional. When set, pods whose PHYSICAL_HOSTNAME matches this value are
/// treated as the preferred leader candidate. Feature is disabled when absent or empty.
/// Derived heartbeat lease name: "{Name}-preferred".
/// </summary>
public string? PreferredNode { get; set; }
```

No DataAnnotations needed. The property is nullable and defaults to null — no validator change.

### Pattern 2: Minimal interface in SnmpCollector.Telemetry

**What:** `IPreferredStampReader` sits alongside `ILeaderElection` in the `Telemetry` folder.
It exposes exactly one member to keep the gate path free of network calls.

**Example:**
```csharp
namespace SnmpCollector.Telemetry;

/// <summary>
/// Contract between the preferred-leader heartbeat service (writer) and
/// K8sLeaseElection gate consumers (reader).
/// Implementations MUST be thread-safe: IsPreferredStampFresh is read on
/// the leader-election hot path.
/// </summary>
public interface IPreferredStampReader
{
    /// <summary>
    /// Returns true when a fresh heartbeat stamp from the preferred pod has been
    /// observed recently. Written by the heartbeat service; read by election gates.
    /// </summary>
    bool IsPreferredStampFresh { get; }
}
```

### Pattern 3: Null-object / no-op implementation for disabled feature

**What:** A `NullPreferredStampReader` that always returns `false`. Registered in DI when
the feature is disabled (local dev or `PreferredNode` absent). Downstream phases replace
this with a real implementation without touching the gate consumers.

**Example:**
```csharp
namespace SnmpCollector.Telemetry;

/// <summary>
/// No-op implementation of <see cref="IPreferredStampReader"/> used when
/// the preferred-leader feature is disabled (local dev or PreferredNode absent).
/// Always returns false — no pod is considered preferred.
/// </summary>
public sealed class NullPreferredStampReader : IPreferredStampReader
{
    public bool IsPreferredStampFresh => false;
}
```

### Pattern 4: _isPreferredPod resolution at startup

**What:** Read `PHYSICAL_HOSTNAME` env var once, compare against `LeaseOptions.PreferredNode`,
store as a readonly bool. Log the outcome at startup so operators can confirm which pod is preferred.

This logic belongs on a new `PreferredLeaderService` (stub BackgroundService, registered only
in K8s mode) or directly in the K8s registration block. Based on the CONTEXT decision that
`_isPreferredPod` storage is at Claude's discretion, the cleanest approach is a dedicated
`PreferredLeaderService` that: (a) resolves identity, (b) logs it, (c) implements
`IPreferredStampReader` as a stub (`false`) pending Phase 85.

**Example:**
```csharp
// In K8s registration block of ServiceCollectionExtensions:
services.AddSingleton<PreferredLeaderService>();
services.AddSingleton<IPreferredStampReader>(sp =>
    sp.GetRequiredService<PreferredLeaderService>());
// NOT AddHostedService yet -- no background loop until Phase 85
```

For local dev (non-K8s): register `NullPreferredStampReader` unconditionally.

### Pattern 5: DI registration inside the existing IsInCluster() branch

**What:** All K8s-only services are registered inside the `if (IsInCluster())` block in
`AddSnmpConfiguration`. New types follow this exactly. The `NullPreferredStampReader` fallback
goes in the `else` branch.

**Example — K8s branch (inside existing `if (IsInCluster())`):**
```csharp
// After K8sLeaseElection registration:
services.AddSingleton<PreferredLeaderService>();
services.AddSingleton<IPreferredStampReader>(
    sp => sp.GetRequiredService<PreferredLeaderService>());
```

**Example — local dev branch (inside existing `else`):**
```csharp
services.AddSingleton<IPreferredStampReader, NullPreferredStampReader>();
```

### Anti-Patterns to Avoid

- **Creating a new SiteAffinityOptions class:** The phase decision explicitly says to extend
  `LeaseOptions`. Do not introduce a new options class.
- **Reading PHYSICAL_HOSTNAME repeatedly:** Resolve once at startup, store as `readonly bool`.
  Do not re-evaluate per request.
- **Crashing when PHYSICAL_HOSTNAME is empty:** Log warning and disable feature (set
  `_isPreferredPod = false`). Do not throw.
- **Re-registering PreferredLeaderService as IHostedService in this phase:** No background
  loop runs until Phase 85. Register as singleton only — not as hosted service.
- **Reading NODE_NAME:** The deployment manifest injects `spec.nodeName` as `PHYSICAL_HOSTNAME`,
  not `NODE_NAME`. The CONTEXT says "NODE_NAME" conceptually but the actual env var name in
  this codebase is `PHYSICAL_HOSTNAME`. Verify: `grep PHYSICAL_HOSTNAME deploy/k8s/snmp-collector/deployment.yaml` confirms.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Nullable optional config field | Custom config loader | Plain nullable property on existing class | ASP.NET Core Options binding handles null gracefully |
| No-op implementation | Complex conditional | `NullPreferredStampReader` returning `false` | Null-object pattern; zero branching in consumers |
| Startup validation | Custom probe | `ValidateOnStart()` + `IValidateOptions<T>` | Already established; fail-fast before host accepts work |

**Key insight:** This phase is declarations only. No behavior. The risk is over-engineering
the stub — keep it minimal so downstream phases can replace just the implementation without
touching contracts.

## Common Pitfalls

### Pitfall 1: Wrong env var name

**What goes wrong:** Reading `NODE_NAME` instead of `PHYSICAL_HOSTNAME`.
**Why it happens:** CONTEXT.md and requirements use "NODE_NAME" as a conceptual name, but
the actual deployment manifest injects `spec.nodeName` as `PHYSICAL_HOSTNAME` (verified in
`deploy/k8s/snmp-collector/deployment.yaml` line 38-41).
**How to avoid:** Use `Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME")` — matches
existing usages in `ServiceCollectionExtensions.cs` lines 79, 140, 151.
**Warning signs:** If the startup log shows "PHYSICAL_HOSTNAME is empty, preferred feature disabled"
on a pod that definitely has a node name, the wrong env var name was used.

### Pitfall 2: Registering PreferredLeaderService as IHostedService in Phase 84

**What goes wrong:** If registered as a hosted service, the background loop runs (or fails to
start) with no actual heartbeat lease logic, causing confusing errors.
**Why it happens:** The pattern for K8sLeaseElection uses `AddHostedService`. Copying blindly.
**How to avoid:** Register as singleton only in Phase 84. `AddHostedService` is added in Phase 85.

### Pitfall 3: Two instances of PreferredLeaderService

**What goes wrong:** `IPreferredStampReader` consumers get a different instance than
`PreferredLeaderService` itself (same root cause as the ILeaderElection anti-pattern
documented in `LeaderElectionTests.cs` SC#5).
**Why it happens:** `AddSingleton<IPreferredStampReader, PreferredLeaderService>()` followed
by `AddSingleton<PreferredLeaderService>()` creates two instances.
**How to avoid:** Register concrete first, then forward:
```csharp
services.AddSingleton<PreferredLeaderService>();
services.AddSingleton<IPreferredStampReader>(sp =>
    sp.GetRequiredService<PreferredLeaderService>());
```

### Pitfall 4: ValidateOnStart on LeaseOptions will fire the existing LeaseOptionsValidator

**What goes wrong:** Adding `PreferredNode` might trigger unexpected validation failures if
`LeaseOptionsValidator` is extended carelessly.
**Why it happens:** `LeaseOptionsValidator` is already registered and runs at startup.
**How to avoid:** Do not modify `LeaseOptionsValidator` in this phase. `PreferredNode` needs
no cross-field validation — it's an optional string. Existing validator only checks
`Name`, `Namespace`, and `DurationSeconds > RenewIntervalSeconds`, which remain unchanged.

### Pitfall 5: Placing IPreferredStampReader in the wrong namespace

**What goes wrong:** Interface placed in `SnmpCollector.Configuration` instead of
`SnmpCollector.Telemetry`, causing consumers (K8sLeaseElection) to take a dependency
on the configuration namespace.
**Why it happens:** It could seem like a "config" thing.
**How to avoid:** `IPreferredStampReader` belongs in `SnmpCollector.Telemetry` alongside
`ILeaderElection` — it is a runtime state contract, not a config model.

## Code Examples

### Extending LeaseOptions (complete file after change)

```csharp
// Source: existing LeaseOptions.cs pattern
using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

public sealed class LeaseOptions
{
    public const string SectionName = "Lease";

    [Required]
    public required string Name { get; set; } = "snmp-collector-leader";

    [Required]
    public required string Namespace { get; set; } = "default";

    [Range(1, 300)]
    public int RenewIntervalSeconds { get; set; } = 10;

    [Range(1, 600)]
    public int DurationSeconds { get; set; } = 15;

    /// <summary>
    /// Optional. When set, the pod whose PHYSICAL_HOSTNAME matches this value
    /// is treated as the preferred leader candidate.
    /// Feature is disabled when absent or empty — backward compatible.
    /// Derived heartbeat lease name: "{Name}-preferred".
    /// </summary>
    public string? PreferredNode { get; set; }
}
```

### PreferredLeaderService stub (Phase 84 scope: identity resolution + log only)

```csharp
// Source: follows K8sLeaseElection pattern for constructor injection
namespace SnmpCollector.Telemetry;

public sealed class PreferredLeaderService : IPreferredStampReader
{
    private readonly bool _isPreferredPod;

    public PreferredLeaderService(
        IOptions<LeaseOptions> leaseOptions,
        ILogger<PreferredLeaderService> logger)
    {
        var preferredNode = leaseOptions.Value.PreferredNode;

        if (string.IsNullOrEmpty(preferredNode))
        {
            _isPreferredPod = false;
            // Feature disabled -- no log needed, expected default
            return;
        }

        var physicalHostname = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME");

        if (string.IsNullOrEmpty(physicalHostname))
        {
            logger.LogWarning(
                "PreferredNode is configured ({PreferredNode}) but PHYSICAL_HOSTNAME env var is empty. " +
                "Preferred-leader feature disabled for this pod.",
                preferredNode);
            _isPreferredPod = false;
            return;
        }

        // Exact case-sensitive match (per CONTEXT decision)
        _isPreferredPod = physicalHostname == preferredNode;

        logger.LogInformation(
            "Preferred-leader identity: PHYSICAL_HOSTNAME={HostName}, PreferredNode={PreferredNode}, IsPreferredPod={IsPreferredPod}",
            physicalHostname, preferredNode, _isPreferredPod);
    }

    // Phase 84: always false -- no heartbeat lease written yet (Phase 85)
    public bool IsPreferredStampFresh => false;

    /// <summary>
    /// Whether this pod's node name matches the configured PreferredNode.
    /// Resolved once at startup. Consumed by downstream heartbeat service (Phase 85).
    /// </summary>
    public bool IsPreferredPod => _isPreferredPod;
}
```

### DI registration additions in AddSnmpConfiguration

```csharp
// Inside if (k8s.KubernetesClientConfiguration.IsInCluster()) block,
// AFTER K8sLeaseElection registration:
services.AddSingleton<PreferredLeaderService>();
services.AddSingleton<IPreferredStampReader>(
    sp => sp.GetRequiredService<PreferredLeaderService>());

// Inside else block (local dev):
services.AddSingleton<IPreferredStampReader, NullPreferredStampReader>();
```

### Unit test pattern for _isPreferredPod logic

```csharp
// Pattern: construct PreferredLeaderService directly with mock IOptions
// No DI container needed -- follows LeaderElectionTests pattern
[Fact]
public void IsPreferredPod_WhenHostnameMatches_ReturnsTrue()
{
    Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", "node-1");
    var options = Options.Create(new LeaseOptions
    {
        Name = "snmp-collector-leader",
        Namespace = "simetra",
        PreferredNode = "node-1"
    });
    var svc = new PreferredLeaderService(options, NullLogger<PreferredLeaderService>.Instance);
    Assert.True(svc.IsPreferredPod);
}
```

Note: tests that set environment variables must restore them in teardown to avoid
cross-test contamination.

## State of the Art

| Old Approach | Current Approach | Notes |
|--------------|------------------|-------|
| Separate SiteAffinityOptions class | Extend LeaseOptions directly | Phase decision — avoids new section binding |
| NODE_NAME env var | PHYSICAL_HOSTNAME (already in deployment) | Already injected from spec.nodeName |

## Open Questions

1. **Property name: `PreferredNode` vs `PreferredNodeName`**
   - What we know: Both are valid. CONTEXT marks this as Claude's discretion.
   - Recommendation: `PreferredNode` — shorter, matches the K8s concept ("node" not "node name"),
     consistent with how operators think about it. `PreferredNodeName` is more explicit but verbose.

2. **Where `_isPreferredPod` lives: `PreferredLeaderService` vs a shared wrapper**
   - What we know: CONTEXT marks this as Claude's discretion.
   - Recommendation: `PreferredLeaderService` — it is the natural owner since it resolves
     identity. The heartbeat service (Phase 85) will be injected with `PreferredLeaderService`
     directly (or via its public `IsPreferredPod` property). No wrapper needed.

3. **DI registration order for new types**
   - What we know: CONTEXT marks this as Claude's discretion.
   - Recommendation: Register after `K8sLeaseElection` block (lines ~241-243 in
     `ServiceCollectionExtensions.cs`) and before ConfigMap watchers. The stamp reader
     has no dependency on watchers, so it can go anywhere after `IKubernetes` is registered.
     For clarity, group it with other election-related registrations.

## Sources

### Primary (HIGH confidence)

- Direct codebase inspection:
  - `src/SnmpCollector/Configuration/LeaseOptions.cs` — existing options class to extend
  - `src/SnmpCollector/Telemetry/ILeaderElection.cs` — interface style to mirror
  - `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — constructor injection and `_isLeader` pattern
  - `src/SnmpCollector/Telemetry/AlwaysLeaderElection.cs` — null-object pattern to mirror
  - `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — DI registration patterns
  - `src/SnmpCollector/Configuration/Validators/LeaseOptionsValidator.cs` — validator pattern
  - `tests/SnmpCollector.Tests/Telemetry/LeaderElectionTests.cs` — DI singleton test patterns
  - `deploy/k8s/snmp-collector/deployment.yaml` — confirms `PHYSICAL_HOSTNAME` env var name

### Secondary (MEDIUM confidence)

- None required. All findings are from direct codebase inspection.

### Tertiary (LOW confidence)

- None.

## Metadata

**Confidence breakdown:**

- Standard stack: HIGH — no new libraries, all existing patterns verified in source
- Architecture: HIGH — patterns verified by reading existing production code and tests
- Pitfalls: HIGH — pitfall 3 (two instances) is directly documented in the test file (SC#5);
  others verified from deployment manifest and source code
- Open questions: HIGH on facts, MEDIUM on recommendations (discretion items by design)

**Research date:** 2026-03-25
**Valid until:** 2026-06-25 (stable domain — .NET Options and DI patterns do not change rapidly)
