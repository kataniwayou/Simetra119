# Phase 85: PreferredHeartbeatService Reader Path - Research

**Researched:** 2026-03-26
**Domain:** Quartz.NET job authoring, Kubernetes Lease API (coordination.k8s.io/v1), volatile bool state management
**Confidence:** HIGH — all findings sourced directly from the existing codebase

---

## Summary

This phase adds a new Quartz job (`PreferredHeartbeatJob`) that reads the heartbeat lease from
Kubernetes and updates a `volatile bool` in `PreferredLeaderService`. The reader path runs on
every pod on every fire. The writer path (gating on `IsPreferredPod`) is Phase 86 scope.

The codebase has three existing Quartz jobs with identical structural patterns:
`CorrelationJob`, `HeartbeatJob`, and `SnapshotJob`. All use `[DisallowConcurrentExecution]`,
inject `ILivenessVectorService`, stamp in a `finally` block, and read their `IntervalSeconds`
from a dedicated `*JobOptions` class. This phase follows that pattern exactly.

`PreferredLeaderService` already exists as a singleton stub (Phase 84). This phase replaces the
hardcoded `false` on `IsPreferredStampFresh` with a `volatile bool _isPreferredStampFresh` that
the job updates via a new method. The K8s lease API call to read the heartbeat lease uses
`_kubeClient.CoordinationV1.ReadNamespacedLeaseAsync`, which is the natural counterpart to the
`DeleteNamespacedLeaseAsync` already used in `K8sLeaseElection.StopAsync`.

**Primary recommendation:** Follow the `HeartbeatJob`/`SnapshotJobOptions` pattern verbatim for
the job and options class. Use `ReadNamespacedLeaseAsync` with a try/catch that treats 404 as
stale and swallows transient API errors (keep-last-value semantics).

---

## Standard Stack

### Core — already in the project
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Quartz.NET | existing | Job scheduling | All jobs use it; registered via `AddQuartz` |
| KubernetesClient (`k8s`) | existing | Lease API reads | Used by `K8sLeaseElection` and watcher services |
| Microsoft.Extensions.Options | existing | `IOptions<T>` binding | Same pattern as all existing job options |
| Microsoft.Extensions.Logging | existing | `ILogger<T>` | Universal across all jobs |

### No new packages required
All libraries needed for this phase are already referenced in the project.

**Installation:** none needed.

---

## Architecture Patterns

### Recommended File Layout

New files this phase:

```
src/SnmpCollector/
├── Configuration/
│   └── PreferredHeartbeatJobOptions.cs   # "PreferredHeartbeatJob" section, IntervalSeconds
├── Jobs/
│   └── PreferredHeartbeatJob.cs          # [DisallowConcurrentExecution] IJob, reads lease
```

Modified files this phase:

```
src/SnmpCollector/
├── Telemetry/
│   └── PreferredLeaderService.cs         # Add volatile bool + UpdateStampFreshness() method
├── Extensions/
│   └── ServiceCollectionExtensions.cs   # Register options + Quartz job/trigger in K8s block
└── appsettings.json                      # Add "PreferredHeartbeatJob": { "IntervalSeconds": 15 }
```

### Pattern 1: Options Class

**What:** Plain POCO with `SectionName` const, `IntervalSeconds` with `[Range]` validation, default value.

**When to use:** Every Quartz job that has a configurable interval.

```csharp
// Source: src/SnmpCollector/Configuration/HeartbeatJobOptions.cs (exact model)
public sealed class PreferredHeartbeatJobOptions
{
    public const string SectionName = "PreferredHeartbeatJob";

    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 15;
}
```

### Pattern 2: Job Class Structure

**What:** `[DisallowConcurrentExecution]` sealed class implementing `IJob`. Constructor injection.
Try/catch/finally. `_liveness.Stamp(jobKey)` in the finally block. `OperationCanceledException`
rethrown immediately.

**When to use:** Every Quartz job in this codebase.

```csharp
// Source: src/SnmpCollector/Jobs/HeartbeatJob.cs and CorrelationJob.cs (structural model)
[DisallowConcurrentExecution]
public sealed class PreferredHeartbeatJob : IJob
{
    private readonly IKubernetes _kubeClient;
    private readonly PreferredLeaderService _preferredLeaderService;
    private readonly LeaseOptions _leaseOptions;
    private readonly ILivenessVectorService _liveness;
    private readonly ILogger<PreferredHeartbeatJob> _logger;

    // ... constructor injection ...

    public async Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key.Name;
        try
        {
            await ReadAndUpdateStampFreshnessAsync(context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreferredHeartbeatJob {JobKey} failed", jobKey);
        }
        finally
        {
            _liveness.Stamp(jobKey);
        }
    }
}
```

### Pattern 3: Lease Reading via K8s API

**What:** `ReadNamespacedLeaseAsync` is the standard K8s client method for reading a Lease object.
The lease name for the heartbeat is `"{LeaseOptions.Name}-preferred"` (per `LeaseOptions.cs` doc comment:
"Derived heartbeat lease name: `{Name}-preferred`").

**404 handling:** The `KubernetesException` carries `Response.StatusCode`. Catch
`k8s.Autorest.HttpOperationException` (which `KubernetesException` inherits from, or the client
throws directly) and check for 404 — this is the pattern from existing watcher services.

**Transient failure:** Keep last known value (do NOT flip to stale on network error).

```csharp
// Source: K8s client CoordinationV1 API — same client used in K8sLeaseElection.StopAsync
var heartbeatLeaseName = $"{_leaseOptions.Name}-preferred";
try
{
    var lease = await _kubeClient.CoordinationV1.ReadNamespacedLeaseAsync(
        heartbeatLeaseName,
        _leaseOptions.Namespace,
        cancellationToken: ct);

    var stampTime = lease.Spec?.RenewTime ?? lease.Spec?.AcquireTime;
    if (stampTime is null)
    {
        UpdateFreshness(false);
        return;
    }

    var threshold = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds + 5);
    var age = DateTimeOffset.UtcNow - stampTime.Value;
    UpdateFreshness(age <= threshold);
}
catch (k8s.Autorest.HttpOperationException ex)
    when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    // 404 = lease not yet written by preferred pod = stale
    UpdateFreshness(false);
}
catch (OperationCanceledException)
{
    throw;
}
catch (Exception ex)
{
    // Transient K8s API failure: keep last known value, log warning
    _logger.LogWarning(ex,
        "PreferredHeartbeatJob: transient K8s API error reading lease {LeaseName} — keeping last value",
        heartbeatLeaseName);
}
```

**Note on `MicroTime` vs `DateTimeOffset`:** The K8s `V1LeaseSpec.RenewTime` and `AcquireTime`
fields are of type `DateTime?` in the KubernetesClient model. Convert to `DateTimeOffset` for UTC
comparison: `new DateTimeOffset(renewTime.Value, TimeSpan.Zero)` if `Kind == Unspecified`, or
use `DateTimeOffset.FromFileTime` — check the actual property type at implementation time.
The fields are documented as RFC3339 UTC, so treating them as UTC is safe.

### Pattern 4: volatile bool State Update in PreferredLeaderService

**What:** Replace the hardcoded `false` with a writable `volatile bool`. Add an
`UpdateStampFreshness(bool isFresh)` method. Log Info only on state transitions.

```csharp
// Source: K8sLeaseElection.cs — _isLeader volatile bool is the exact model
private volatile bool _isPreferredStampFresh; // initial value: false

public bool IsPreferredStampFresh => _isPreferredStampFresh;

public void UpdateStampFreshness(bool isFresh)
{
    var previous = _isPreferredStampFresh;
    _isPreferredStampFresh = isFresh;

    if (previous != isFresh)
    {
        _logger.LogInformation(
            "PreferredStamp freshness changed: {Previous} -> {Current}",
            previous, isFresh);
    }
}
```

**Why UpdateStampFreshness (not a property setter):** Methods are more natural for
"update with side-effect" (logging on transition). Consistent with how watcher services
call `.ReloadAsync()` rather than setting properties directly.

### Pattern 5: Quartz Registration in AddSnmpScheduling

**What:** Bind options locally (DI not built yet), register job + trigger inside `AddQuartz`,
register interval in `intervalRegistry`, increment `initialJobCount`.

```csharp
// Source: src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs (lines ~480-555)

// Bind options before AddQuartz block:
var preferredHbOptions = new PreferredHeartbeatJobOptions();
configuration.GetSection(PreferredHeartbeatJobOptions.SectionName).Bind(preferredHbOptions);

// In thread pool calculation:
var initialJobCount = 4; // CorrelationJob + HeartbeatJob + SnapshotJob + PreferredHeartbeatJob

// Inside AddQuartz lambda:
var preferredHbKey = new JobKey("preferred-heartbeat");
q.AddJob<PreferredHeartbeatJob>(j => j.WithIdentity(preferredHbKey));
q.AddTrigger(t => t
    .ForJob(preferredHbKey)
    .WithIdentity("preferred-heartbeat-trigger")
    .StartNow()
    .WithSimpleSchedule(s => s
        .WithIntervalInSeconds(preferredHbOptions.IntervalSeconds)
        .RepeatForever()
        .WithMisfireHandlingInstructionNextWithRemainingCount()));

intervalRegistry.Register("preferred-heartbeat", preferredHbOptions.IntervalSeconds);
```

### Pattern 6: Options Registration in AddSnmpConfiguration

**What:** Register the new options class with `ValidateDataAnnotations().ValidateOnStart()`.
This is inside the `IsInCluster()` block because `PreferredHeartbeatJobOptions` is only used
in K8s mode.

```csharp
// Source: ServiceCollectionExtensions.AddSnmpConfiguration (lines ~207-215)
// Place alongside HeartbeatJobOptions and SnapshotJobOptions registrations.
// PreferredHeartbeatJobOptions belongs inside the if (IsInCluster()) block.
services.AddOptions<PreferredHeartbeatJobOptions>()
    .Bind(configuration.GetSection(PreferredHeartbeatJobOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Correction:** Looking at ServiceCollectionExtensions more carefully: `HeartbeatJobOptions` and
`SnapshotJobOptions` are registered in `AddSnmpConfiguration` at lines 207-215, which is OUTSIDE
the `if (IsInCluster())` block — they're always registered. `PreferredHeartbeatJobOptions`
should follow the same unconditional registration pattern to keep options binding consistent,
even though the Quartz job itself only runs in K8s mode. (The options section simply won't exist
in appsettings.Development.json, and the default value of 15s will be used.) However, since
`IntervalSeconds` defaults to `15` and `[Range(1, int.MaxValue)]` allows any positive int, this
is safe.

**Decision for planner:** Register `PreferredHeartbeatJobOptions` alongside the other job options
(unconditional), but register the Quartz job/trigger only inside the `if (IsInCluster())` block.

### Pattern 7: DI Injection of PreferredLeaderService (concrete type, not interface)

**What:** The job needs to call `UpdateStampFreshness(bool)`, which is NOT on `IPreferredStampReader`.
Inject the concrete `PreferredLeaderService` type. The singleton is already registered
under the concrete type in the K8s path:
```csharp
services.AddSingleton<PreferredLeaderService>();
```
So `PreferredHeartbeatJob` can declare `PreferredLeaderService` as a constructor parameter
and DI will resolve it correctly.

### Anti-Patterns to Avoid

- **Injecting IPreferredStampReader into the job:** The interface only exposes `bool IsPreferredStampFresh` (read). The job needs to WRITE. Inject concrete `PreferredLeaderService`.
- **Flipping to false on transient K8s API errors:** Keep last known value. Only 404 = stale.
- **Logging every poll:** Log only on state transitions (fresh->stale, stale->fresh) at Info. Polling logs at Debug or not at all.
- **Separate jobs for read vs write:** The architecture decision is a single job with `IsPreferredPod` gate on write (Phase 86). This phase implements only the read side of that single job.
- **Registering PreferredHeartbeatJob in the local-dev path:** The K8s API client is not registered in local dev. The job must be inside `if (IsInCluster())` in `AddSnmpScheduling`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Job scheduling lifecycle | Custom BackgroundService loop | Quartz `IJob` + `AddQuartz` | Existing infrastructure; CancellationToken plumbed via context |
| Concurrent execution prevention | Manual locks | `[DisallowConcurrentExecution]` attribute | Quartz handles this; all other jobs use it |
| K8s API 404 vs error distinction | Manual HTTP parsing | `k8s.Autorest.HttpOperationException` status code check | Same pattern available from existing K8s client |
| State freshness as a property | Custom debounce / timer | `volatile bool` + transition logging | K8sLeaseElection uses identical pattern for `_isLeader` |

**Key insight:** Every recurring background concern (scheduling, cancellation, thread safety, liveness health check) is already solved by existing infrastructure. This phase is purely composition.

---

## Common Pitfalls

### Pitfall 1: initialJobCount thread pool undercount

**What goes wrong:** `AddSnmpScheduling` calculates `threadPoolSize` from `initialJobCount`. If `PreferredHeartbeatJob` is not counted, the thread pool may be one thread short.

**Why it happens:** The comment says "3 static jobs" but the code will add a 4th.

**How to avoid:** Change the comment and initial value from `3` to `4`:
```csharp
var initialJobCount = 4; // CorrelationJob + HeartbeatJob + SnapshotJob + PreferredHeartbeatJob
```

**Warning signs:** Thread contention at startup with 4 static jobs running concurrently.

### Pitfall 2: Injecting IKubernetes in the local-dev Quartz path

**What goes wrong:** `IKubernetes` is only registered when `IsInCluster()` is true. If `PreferredHeartbeatJob` is registered in the `AddQuartz` block unconditionally, Quartz will fail to construct the job in local dev.

**Why it happens:** `AddQuartz` registration and `AddSnmpConfiguration`'s `IsInCluster()` block are separate.

**How to avoid:** Register the Quartz job/trigger for `PreferredHeartbeatJob` only inside the `if (IsInCluster())` block in `AddSnmpScheduling`.

### Pitfall 3: Wrong exception type for K8s 404

**What goes wrong:** Catching `k8s.KubernetesException` when the client actually throws `k8s.Autorest.HttpOperationException`.

**Why it happens:** The KubernetesClient library throws `HttpOperationException` (from AutoRest) for HTTP-level errors. `KubernetesException` may not be the thrown type.

**How to avoid:** Catch `k8s.Autorest.HttpOperationException` and check `ex.Response.StatusCode == HttpStatusCode.NotFound`. Verify at implementation time by inspecting the client's method signature or a quick test run.

### Pitfall 4: MicroTime / DateTime UTC handling

**What goes wrong:** `V1LeaseSpec.RenewTime` is `DateTime?` (not `DateTimeOffset?`). If `Kind == DateTimeKind.Unspecified`, a direct `DateTimeOffset` comparison may be off.

**Why it happens:** The Kubernetes API returns RFC3339 UTC timestamps, but the C# model deserialization may leave `Kind == Unspecified`.

**How to avoid:** Use `DateTime.SpecifyKind(renewTime.Value, DateTimeKind.Utc)` before converting to `DateTimeOffset`, or compare against `DateTime.UtcNow` directly.

### Pitfall 5: Quartz job registered in wrong AddSnmpScheduling path

**What goes wrong:** `AddSnmpScheduling` does NOT have a separate `if (IsInCluster())` block today — it always registers all jobs. The K8s-specific job must be conditionally registered.

**Why it happens:** The existing three jobs (Correlation, Heartbeat, Snapshot) work in both local and K8s. `PreferredHeartbeatJob` needs `IKubernetes`, which is only available in K8s.

**How to avoid:** Add a conditional block inside `services.AddQuartz(q => { ... })` that checks `KubernetesClientConfiguration.IsInCluster()` before registering `PreferredHeartbeatJob`. Similarly wrap the interval registration and `initialJobCount` bump.

### Pitfall 6: appsettings key not added

**What goes wrong:** `PreferredHeartbeatJobOptions.IntervalSeconds` defaults to 15 but `ValidateOnStart` fires before the default is evaluated if the section is missing (depends on validation implementation).

**Why it happens:** `[Range(1, int.MaxValue)]` validation passes for the default value of 15, so in practice this is safe. But for operational clarity, add the section to `appsettings.json`.

**How to avoid:** Add `"PreferredHeartbeatJob": { "IntervalSeconds": 15 }` to `appsettings.json`.

---

## Code Examples

### Verified: Job options class pattern

```csharp
// Source: src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
public sealed class HeartbeatJobOptions
{
    public const string SectionName = "HeartbeatJob";

    [Range(1, int.MaxValue)]
    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
}
```

### Verified: volatile bool pattern from K8sLeaseElection

```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs (line 34)
private volatile bool _isLeader;
// ...
public bool IsLeader => _isLeader;
// Set in event handlers: _isLeader = true; / _isLeader = false;
```

### Verified: Quartz job registration template

```csharp
// Source: src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs (lines 529-541)
var heartbeatKey = new JobKey("heartbeat");
q.AddJob<HeartbeatJob>(j => j.WithIdentity(heartbeatKey));
q.AddTrigger(t => t
    .ForJob(heartbeatKey)
    .WithIdentity("heartbeat-trigger")
    .StartNow()
    .WithSimpleSchedule(s => s
        .WithIntervalInSeconds(heartbeatOptions.IntervalSeconds)
        .RepeatForever()
        .WithMisfireHandlingInstructionNextWithRemainingCount()));
intervalRegistry.Register("heartbeat", heartbeatOptions.IntervalSeconds);
```

### Verified: DeleteNamespacedLeaseAsync namespace and name pattern

```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs (lines 123-127)
await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
    _leaseOptions.Name,
    _leaseOptions.Namespace,
    cancellationToken: cancellationToken);
```
The `ReadNamespacedLeaseAsync` signature is identical: `(name, namespace, cancellationToken)`.
The heartbeat lease name = `$"{_leaseOptions.Name}-preferred"` (from LeaseOptions XML doc comment).

### Verified: Liveness stamp in finally block

```csharp
// Source: all jobs — CorrelationJob.cs, HeartbeatJob.cs, SnapshotJob.cs
finally
{
    _liveness.Stamp(jobKey);
    _correlation.OperationCorrelationId = null;
}
```
Note: `PreferredHeartbeatJob` does NOT use `ICorrelationService` (it's not a pipeline job).
The finally block stamps liveness only.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hardcoded `false` in `PreferredLeaderService` | `volatile bool _isPreferredStampFresh` + Quartz job | This phase | Enables real freshness signal from K8s |
| No `PreferredHeartbeatJobOptions` | New options class in `"PreferredHeartbeatJob"` section | This phase | Follows established pattern |

---

## Open Questions

1. **`HttpOperationException` vs `KubernetesException` catch type**
   - What we know: `K8sLeaseElection.StopAsync` does not catch 404 (it's a delete, not a read). No existing code in this project reads a lease and catches 404.
   - What's unclear: Exact exception type thrown by `ReadNamespacedLeaseAsync` when the lease does not exist.
   - Recommendation: Use `catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)`. This is the standard pattern for the KubernetesClient AutoRest-based API. If the build fails, check if the client wraps it in a `k8s.KubernetesException`.

2. **`V1LeaseSpec.RenewTime` type in the installed KubernetesClient version**
   - What we know: The K8s API spec defines `renewTime` as MicroTime (RFC3339). KubernetesClient typically models this as `DateTime?`.
   - What's unclear: Whether `Kind` is set to `Utc` by the deserializer.
   - Recommendation: Use `DateTime.SpecifyKind(value, DateTimeKind.Utc)` defensively before comparing with `DateTime.UtcNow`.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — Quartz job structural pattern
- `src/SnmpCollector/Jobs/CorrelationJob.cs` — minimal job pattern (sync, no correlation)
- `src/SnmpCollector/Jobs/SnapshotJob.cs` — IOptions injection pattern
- `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` — options class model
- `src/SnmpCollector/Configuration/SnapshotJobOptions.cs` — options with [Range] validation
- `src/SnmpCollector/Configuration/LeaseOptions.cs` — `DurationSeconds`, `Name`, `Namespace`, `PreferredNode`; heartbeat lease name comment
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — full registration patterns for Quartz jobs, options, and K8s-gated singletons
- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — `volatile bool` pattern, `DeleteNamespacedLeaseAsync` call signature, `CoordinationV1` API usage
- `src/SnmpCollector/Telemetry/PreferredLeaderService.cs` — current Phase 84 stub to be modified
- `src/SnmpCollector/Telemetry/IPreferredStampReader.cs` — interface contract (read-only, not writable)
- `src/SnmpCollector/Telemetry/NullPreferredStampReader.cs` — local dev stub (unchanged by this phase)
- `src/SnmpCollector/appsettings.json` — existing job sections for appsettings addition

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all libraries are existing project dependencies
- Architecture: HIGH — job/options/registration patterns verified from three existing jobs
- Pitfalls: HIGH — derived from reading actual source code and the specific integration points
- K8s lease read API: MEDIUM — `ReadNamespacedLeaseAsync` method exists on `CoordinationV1` (established K8s client API); 404 exception type is the one open question

**Research date:** 2026-03-26
**Valid until:** 2026-04-26 (stable codebase; patterns are locked by existing jobs)
