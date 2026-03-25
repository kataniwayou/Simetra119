# Phase 86: PreferredHeartbeatService Writer Path and Readiness Gate - Research

**Researched:** 2026-03-26
**Domain:** Kubernetes Lease write API (CoordinationV1), IHostApplicationLifetime readiness gate, volatile bool placement
**Confidence:** HIGH — verified against KubernetesClient 18.0.13 DLL and existing codebase patterns

---

## Summary

Phase 86 adds the writer path to the existing `PreferredHeartbeatJob`. The preferred pod creates
or renews the heartbeat lease on every tick (after `ApplicationStarted` fires) so non-preferred
pods can detect its presence via the reader (Phase 85). Three new technical concerns beyond
Phase 85:

1. **K8s Lease write API** — `CreateNamespacedLeaseAsync` and `ReplaceNamespacedLeaseAsync` are
   both present on `_kubeClient.CoordinationV1` in KubernetesClient 18.0.13. Replace requires
   `resourceVersion` in metadata. The correct create-or-replace pattern: try create (409 =
   already exists), on conflict read the current lease to get `resourceVersion`, then replace.
   Cache the `resourceVersion` so subsequent ticks can replace directly without an extra read.

2. **Readiness gate** — `IHostApplicationLifetime.ApplicationStarted` is a `CancellationToken`
   (not an event). Register a callback via `.Register(() => _isReady = true)`. The `volatile bool`
   can live on `PreferredLeaderService` (aligns with its pattern of holding pod identity state)
   or on the job itself. Recommendation: place it on the job — it is job-lifecycle state, not
   pod-identity state. `PreferredLeaderService` already holds `IsPreferredPod`; the readiness
   gate is a different concern.

3. **V1Lease object construction** — `V1LeaseSpec.RenewTime` and `AcquireTime` are `DateTime?`.
   Assigning `DateTime.UtcNow` produces `Kind == Utc` (verified). `leaseDurationSeconds` should
   match `LeaseOptions.DurationSeconds`. `acquireTime` is set only on first create.

**Primary recommendation:** Place the readiness `volatile bool` on the job. Use create-on-first-tick + replace-with-cached-resourceVersion on subsequent ticks. No new packages needed.

---

## Standard Stack

### Core — already in the project
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| KubernetesClient | 18.0.13 | `CoordinationV1.CreateNamespacedLeaseAsync`, `ReplaceNamespacedLeaseAsync` | Same client used by reader path and K8sLeaseElection |
| Microsoft.Extensions.Hosting | 9.0.0 | `IHostApplicationLifetime` for `ApplicationStarted` token | Already injected in `K8sLeaseElection` |
| Microsoft.Extensions.Options | 9.0.0 | `IOptions<PodIdentityOptions>` for `holderIdentity` | Existing pattern |
| Quartz.NET | 3.15.1 | Job scheduling | Existing infrastructure |

### No new packages required
All libraries are already referenced in `SnmpCollector.csproj`.

**Installation:** none needed.

---

## Architecture Patterns

### Recommended Project Structure

No new files needed. All changes are to `PreferredHeartbeatJob.cs` plus the readiness gate placement.

Modified files this phase:
```
src/SnmpCollector/
└── Jobs/
    └── PreferredHeartbeatJob.cs   # Add writer path, readiness gate, IHostApplicationLifetime injection
```

`PreferredLeaderService.cs` and `ServiceCollectionExtensions.cs` are NOT modified this phase
(job already registered; `IsPreferredPod` already exposed; no new options class needed).

### Pattern 1: IHostApplicationLifetime.ApplicationStarted Registration

**What:** `ApplicationStarted` is a `CancellationToken`. Register a callback that sets a `volatile bool` when the token is cancelled (i.e., when the host has started). The registration happens in the constructor.

**Why constructor:** The host starts after DI is fully built. The constructor runs during DI build. The callback fires after all hosted services have started (including QuartzHostedService), which is the "ready" condition.

**Where to store the bool:** On the job class itself as a `volatile bool` field. The readiness gate is job-execution state, not pod-identity state. `PreferredLeaderService` already has `IsPreferredPod` (pod identity concern); `_isReady` is a scheduler-lifecycle concern.

```csharp
// Source: verified against IHostApplicationLifetime reflection output (net9.0)
// IHostApplicationLifetime.ApplicationStarted is CancellationToken, not an event
private volatile bool _isSchedulerReady;

public PreferredHeartbeatJob(
    IKubernetes kubeClient,
    PreferredLeaderService preferredLeaderService,
    IOptions<LeaseOptions> leaseOptions,
    IOptions<PodIdentityOptions> podIdentityOptions,
    IHostApplicationLifetime lifetime,
    ILivenessVectorService liveness,
    ILogger<PreferredHeartbeatJob> logger)
{
    // ...
    lifetime.ApplicationStarted.Register(() => _isSchedulerReady = true);
}
```

**Note:** `ApplicationStarted` fires AFTER all `IHostedService.StartAsync` calls complete (including `QuartzHostedService.StartAsync`). The Quartz scheduler is running when the callback fires.

### Pattern 2: Create-or-Replace Pattern with Cached ResourceVersion

**What:** K8s PUT (`ReplaceNamespacedLeaseAsync`) requires `resourceVersion` in metadata for optimistic concurrency. The approach:

- Keep a `string? _cachedResourceVersion` field on the job (or a nullable `V1Lease` for the whole object).
- First call: try `CreateNamespacedLeaseAsync`. On 409 Conflict (lease already exists from a previous pod run or restart), read the current lease to get `resourceVersion`, then replace.
- After a successful create or replace: cache the `resourceVersion` returned in the response.
- Subsequent calls: replace using the cached `resourceVersion`. If a 409 or 404 occurs, reset cache and retry next tick.

**Why this pattern over "always read first":** The write path already runs before the read path. Reading first (every tick) would add a second API call on every tick, even when the lease exists and resourceVersion is known. Caching is simpler.

**Why not omit resourceVersion on replace:** Kubernetes enforces optimistic concurrency on PUT. Omitting `resourceVersion` returns `409 Conflict` with a message about missing resource version.

```csharp
// Source: KubernetesClient 18.0.13 API verification
// Verified method signatures from CoordinationV1OperationsExtensions reflection

private string? _cachedResourceVersion;

private async Task WriteHeartbeatLeaseAsync(CancellationToken ct)
{
    var leaseName = $"{_leaseOptions.Name}-preferred";
    var now = DateTime.UtcNow;

    var spec = new V1LeaseSpec
    {
        HolderIdentity = _podIdentity,
        LeaseDurationSeconds = _leaseOptions.DurationSeconds,
        RenewTime = now
        // AcquireTime set only on create (see pattern below)
    };

    if (_cachedResourceVersion is null)
    {
        // First tick or after cache invalidation: try create
        spec.AcquireTime = now;
        var leaseToCreate = new V1Lease
        {
            ApiVersion = "coordination.k8s.io/v1",
            Kind = "Lease",
            Metadata = new V1ObjectMeta
            {
                Name = leaseName,
                NamespaceProperty = _leaseOptions.Namespace
            },
            Spec = spec
        };

        try
        {
            var created = await _kubeClient.CoordinationV1.CreateNamespacedLeaseAsync(
                leaseToCreate,
                _leaseOptions.Namespace,
                cancellationToken: ct);
            _cachedResourceVersion = created.Metadata.ResourceVersion;
            return;
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Lease already exists — read to get current resourceVersion
            var existing = await _kubeClient.CoordinationV1.ReadNamespacedLeaseAsync(
                leaseName, _leaseOptions.Namespace, cancellationToken: ct);
            _cachedResourceVersion = existing.Metadata.ResourceVersion;
            // Fall through to replace below
        }
    }

    // Replace using cached resourceVersion
    var leaseToReplace = new V1Lease
    {
        ApiVersion = "coordination.k8s.io/v1",
        Kind = "Lease",
        Metadata = new V1ObjectMeta
        {
            Name = leaseName,
            NamespaceProperty = _leaseOptions.Namespace,
            ResourceVersion = _cachedResourceVersion
        },
        Spec = spec
    };

    try
    {
        var replaced = await _kubeClient.CoordinationV1.ReplaceNamespacedLeaseAsync(
            leaseToReplace,
            leaseName,
            _leaseOptions.Namespace,
            cancellationToken: ct);
        _cachedResourceVersion = replaced.Metadata.ResourceVersion;
    }
    catch (k8s.Autorest.HttpOperationException ex)
        when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict
           || ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        // ResourceVersion stale (concurrent write) or lease disappeared — invalidate cache
        _cachedResourceVersion = null;
        _logger.LogWarning(
            "Heartbeat lease {LeaseName} write conflict/missing — will retry next tick",
            leaseName);
    }
}
```

**Simpler alternative:** Since `[DisallowConcurrentExecution]` prevents overlap and only one pod writes, conflicts are rare. The only realistic conflict scenario is a pod restart (stale resourceVersion from the previous process). Reset the cache on conflict and the next tick will succeed.

### Pattern 3: Execute Method with Write-Before-Read Ordering

**What:** Decision from CONTEXT — write first (if preferred + ready), then read. Preferred pod stamps, then immediately reads its own stamp (confirms freshness). Non-preferred skips write silently.

```csharp
// Source: CONTEXT.md decision + Phase 85 PreferredHeartbeatJob pattern
public async Task Execute(IJobExecutionContext context)
{
    var jobKey = context.JobDetail.Key.Name;

    try
    {
        // Writer path: only on preferred pod, only when scheduler is ready
        if (_preferredLeaderService.IsPreferredPod && _isSchedulerReady)
        {
            await WriteHeartbeatLeaseAsync(context.CancellationToken);
        }
        // Silent skip for non-preferred pods — no log

        // Reader path: all pods, every tick (Phase 85, unchanged)
        await ReadAndUpdateStampFreshnessAsync(context.CancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "PreferredHeartbeat job {JobKey} failed", jobKey);
    }
    finally
    {
        _liveness.Stamp(jobKey);
    }
}
```

### Pattern 4: V1Lease Object Construction

**What:** Verified against KubernetesClient 18.0.13. `V1LeaseSpec.RenewTime` and `AcquireTime` are `DateTime?`. Assigning `DateTime.UtcNow` produces `Kind == Utc` — no `SpecifyKind` needed.

**Confirmed by probe:**
```
RenewTime type: System.Nullable`1[[System.DateTime, ...]]
Assigned RenewTime Kind: Utc
```

```csharp
// Source: KubernetesClient 18.0.13 reflection probe (verified 2026-03-26)
var spec = new V1LeaseSpec
{
    HolderIdentity = podIdentity,          // string — the pod identity from PodIdentityOptions
    LeaseDurationSeconds = durationSeconds, // int? — matches LeaseOptions.DurationSeconds
    AcquireTime = DateTime.UtcNow,         // DateTime? — Kind=Utc when using UtcNow
    RenewTime = DateTime.UtcNow            // DateTime? — Kind=Utc when using UtcNow
};
```

**leaseDurationSeconds value:** Use `_leaseOptions.DurationSeconds` (same value the leader election lease uses). This makes TTL expiry behavior consistent — if the preferred pod dies, the heartbeat lease expires in the same window as the leader lease TTL.

### Pattern 5: PodIdentity for holderIdentity

**What:** `holderIdentity` on the heartbeat lease should identify the writing pod. Use `PodIdentityOptions.PodIdentity`, which defaults to the `HOSTNAME` env var (K8s pod name) via PostConfigure.

**How to get it:** Inject `IOptions<PodIdentityOptions>` into `PreferredHeartbeatJob` constructor. Read once: `_podIdentity = podIdentityOptions.Value.PodIdentity ?? Environment.MachineName;`.

**Pattern from existing code:**
```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs (line 71)
var identity = _podIdentityOptions.PodIdentity ?? Environment.MachineName;
```

### Pattern 6: Method Scope of WriteHeartbeatLeaseAsync

**What:** Extract the write logic into a private `WriteHeartbeatLeaseAsync` method, parallel to the existing `ReadAndUpdateStampFreshnessAsync`. This keeps `Execute` clean.

**Why a private method:** The combined write + inner-try-catch for create/replace is ~30 lines. Keeping it inline in Execute makes the try/catch structure harder to follow.

### Anti-Patterns to Avoid

- **Placing `_isReady` on `PreferredLeaderService`:** Readiness is job-lifecycle state, not pod-identity state. Mixing concerns makes `PreferredLeaderService` aware of things it shouldn't be. Keep it on the job.
- **Omitting `resourceVersion` on `ReplaceNamespacedLeaseAsync`:** K8s returns 409 Conflict. Always pass the cached `resourceVersion`.
- **Always reading before writing:** Adds one extra K8s API call per tick. Unnecessary given `[DisallowConcurrentExecution]` and single-writer semantics.
- **Logging on every non-preferred skip:** The decision says silent skip. No log when `IsPreferredPod == false`.
- **Setting `acquireTime` on replace:** `acquireTime` tracks when the lease was first acquired. Only set it on the initial create, not on renewals.
- **Deleting the lease on shutdown:** Explicitly forbidden by the CONTEXT. TTL expiry is the mechanism. No changes to `GracefulShutdownService`.
- **Swallowing write errors silently:** Unlike the reader path (keep-last-value on transient error), the writer should log a warning on conflict/missing so the operator knows about retry. Transient network errors in the write path (not 409/404) should be logged at Warning and not crash the job.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting scheduler startup | Custom countdown / polling | `IHostApplicationLifetime.ApplicationStarted.Register(callback)` | Purpose-built .NET API; fires after all hosted services start |
| Create-or-replace idempotency | Custom lock or compare-and-swap | Try-create, 409 = read-then-replace | K8s optimistic concurrency semantics; `resourceVersion` is the mechanism |
| V1Lease timestamp format | Custom serialization | `DateTime.UtcNow` assigned directly to `V1LeaseSpec.RenewTime` | KubernetesClient handles serialization; verified `Kind=Utc` works |
| Pod identity string | Reading env var directly | `IOptions<PodIdentityOptions>.PodIdentity` | Already resolved via PostConfigure in ServiceCollectionExtensions |

---

## Common Pitfalls

### Pitfall 1: ReplaceNamespacedLeaseAsync without resourceVersion

**What goes wrong:** K8s API returns `409 Conflict` with message "the object has been modified; please apply your changes to the latest version and try again" (or "metadata.resourceVersion: Invalid value").

**Why it happens:** K8s uses optimistic concurrency for PUT operations. The server requires you to echo back the current `resourceVersion` to prove you've seen the latest state.

**How to avoid:** Always cache the `resourceVersion` returned from create or replace responses. Pass it in `Metadata.ResourceVersion` on every replace call.

**Warning signs:** Infinite 409 loop if cache is never populated; verified by checking the exception message.

### Pitfall 2: CreateNamespacedLeaseAsync signature — namespace not name

**What goes wrong:** `CreateNamespacedLeaseAsync(body, namespaceParameter, ...)` — the second parameter is the NAMESPACE, not the lease name. The name comes from `body.Metadata.Name`.

**Why it happens:** The extension method signature differs from `ReadNamespacedLeaseAsync(name, namespaceParameter, ...)` and `ReplaceNamespacedLeaseAsync(body, name, namespaceParameter, ...)`.

**Verified signatures:**
```
CreateNamespacedLeaseAsync(V1Lease body, string namespaceParameter, string dryRun, ...)
ReadNamespacedLeaseAsync(string name, string namespaceParameter, ...)
ReplaceNamespacedLeaseAsync(V1Lease body, string name, string namespaceParameter, ...)
DeleteNamespacedLeaseAsync(string name, string namespaceParameter, ...)
```

**How to avoid:** Ensure `body.Metadata.Name` is set to `leaseName` before calling `CreateNamespacedLeaseAsync`. The `namespaceParameter` arg is for the namespace only.

### Pitfall 3: ApplicationStarted fires BEFORE the job runs

**What goes wrong:** The callback fires when the host starts. The job fires when Quartz triggers it (after `StartNow()` with the scheduler already running). The readiness bool will be `true` before the first job execution in practice.

**Why it matters:** There is a small window between host start and first job trigger where `_isSchedulerReady` is already `true` but Quartz hasn't fired the job yet. This is the correct behavior — the check is "is the scheduler running", not "has the job run before".

**How to avoid:** No action needed. The pattern works as designed. Document it in code comments.

### Pitfall 4: acquireTime set on replace (renewal)

**What goes wrong:** `acquireTime` records when the lease was first obtained. Setting it on every renewal resets the acquisition timestamp, which can confuse operators reading the lease.

**Why it happens:** The field is nullable and easy to copy-paste from create to replace.

**How to avoid:** Only set `AcquireTime = DateTime.UtcNow` in the create branch, not in the replace branch.

### Pitfall 5: Exception scope — write exceptions escape to Execute's outer catch

**What goes wrong:** If `WriteHeartbeatLeaseAsync` throws an unexpected exception (not `OperationCanceledException`, not the handled 409/404), the outer `catch (Exception ex)` in `Execute` logs it as a job failure. This is acceptable — transient API errors are logged.

**Why it matters:** The liveness stamp still fires in `finally`, so the job is not marked stale. This is correct behavior.

**How to avoid:** Catch transient errors inside `WriteHeartbeatLeaseAsync` with a warning log (same as reader path). Only let `OperationCanceledException` propagate.

### Pitfall 6: _cachedResourceVersion is instance state on a Quartz job

**What goes wrong:** Quartz creates a new job instance per execution if registered with certain scopes. If the job is re-instantiated per tick, `_cachedResourceVersion` is lost every time.

**Why it doesn't apply here:** `[DisallowConcurrentExecution]` does NOT cause re-instantiation. Quartz instantiates `PreferredHeartbeatJob` once per DI container lifetime (it's registered as a transient by Quartz's DI integration, but the `IJobFactory` resolves the instance once per trigger fire). More importantly: the pattern handles a null cache gracefully (try create → 409 → read → replace). Even if the cache is lost between ticks, the next tick succeeds with a one-extra-read penalty.

**Safer alternative:** If instance lifetime is uncertain, skip caching: always read before write (the read gives the current `resourceVersion`). Costs one extra API call per tick on the preferred pod. Given the heartbeat interval of 15s, this is acceptable.

**Recommendation:** Use the cached pattern. Log a `Debug` message when the cache misses. If the codebase evolves to re-instantiate the job, the one-extra-read behavior is a safe fallback.

---

## Code Examples

### Verified: CreateNamespacedLeaseAsync method signature

```csharp
// Source: KubernetesClient 18.0.13 CoordinationV1OperationsExtensions reflection (verified 2026-03-26)
// Parameters: (V1Lease body, string namespaceParameter, string dryRun, string fieldManager,
//              string fieldValidation, bool? pretty, CancellationToken cancellationToken)
var created = await _kubeClient.CoordinationV1.CreateNamespacedLeaseAsync(
    leaseBody,
    _leaseOptions.Namespace,
    cancellationToken: ct);
```

### Verified: ReplaceNamespacedLeaseAsync method signature

```csharp
// Source: KubernetesClient 18.0.13 CoordinationV1OperationsExtensions reflection (verified 2026-03-26)
// Parameters: (V1Lease body, string name, string namespaceParameter, string dryRun,
//              string fieldManager, string fieldValidation, bool? pretty, CancellationToken cancellationToken)
var replaced = await _kubeClient.CoordinationV1.ReplaceNamespacedLeaseAsync(
    leaseBody,
    leaseName,
    _leaseOptions.Namespace,
    cancellationToken: ct);
```

### Verified: V1LeaseSpec field types (KubernetesClient 18.0.13)

```csharp
// Source: reflection probe against KubernetesClient.dll net9.0 target (verified 2026-03-26)
// RenewTime type:  System.Nullable<System.DateTime>
// AcquireTime type: System.Nullable<System.DateTime>
// LeaseDurationSeconds type: System.Nullable<System.Int32>
// Assigning DateTime.UtcNow => Kind = Utc (confirmed)
var spec = new V1LeaseSpec
{
    HolderIdentity = podIdentity,
    LeaseDurationSeconds = _leaseOptions.DurationSeconds,
    RenewTime = DateTime.UtcNow       // Kind=Utc, no SpecifyKind needed
};
// On create only:
spec.AcquireTime = DateTime.UtcNow;
```

### Verified: IHostApplicationLifetime.ApplicationStarted is CancellationToken

```csharp
// Source: typeof(IHostApplicationLifetime).GetProperties() reflection (net9.0, verified 2026-03-26)
// ApplicationStarted is CancellationToken, NOT an event or Action
// Register a callback:
lifetime.ApplicationStarted.Register(() => _isSchedulerReady = true);
```

### Verified: K8sLeaseElection PodIdentity usage (source pattern)

```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs (line 71)
var identity = _podIdentityOptions.PodIdentity ?? Environment.MachineName;
// Same pattern for holderIdentity in the heartbeat lease
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Reader-only PreferredHeartbeatJob (Phase 85) | Reader + Writer (Phase 86) | This phase | Preferred pod stamps the lease; non-preferred pods detect it |
| No readiness gate | `ApplicationStarted` callback sets `_isSchedulerReady` | This phase | No premature stamps before scheduler is live |

**No deprecated/removed items this phase.**

---

## Open Questions

1. **Job instance lifetime under Quartz DI**
   - What we know: Quartz's `ServiceProviderJobFactory` resolves jobs from the DI container. Jobs registered with `[DisallowConcurrentExecution]` are not instantiated concurrently but may be resolved fresh per execution depending on DI lifetime.
   - What's unclear: Exact DI lifetime for `PreferredHeartbeatJob` as registered in `AddQuartz`. If transient, `_cachedResourceVersion` is lost each tick.
   - Recommendation: Cache resourceVersion as described, but design `WriteHeartbeatLeaseAsync` so that a null cache is handled correctly (the read-on-409 path). Document in code comments that a null cache costs one extra API read per tick.

2. **Whether to separate write errors from outer catch**
   - What we know: The current job structure has a single outer catch in `Execute`. Write exceptions that are not 409/404/OperationCanceled would be logged by the outer catch.
   - What's unclear: Whether the team prefers granular logging in `WriteHeartbeatLeaseAsync` (log warning there) vs. letting the outer catch handle unexpected errors (log error there).
   - Recommendation: Log `Warning` inside `WriteHeartbeatLeaseAsync` for transient errors (same as reader path), and only let `OperationCanceledException` and unexpected errors escape. Consistent with Phase 85's reader error handling.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs` — existing reader implementation to extend
- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — `IHostApplicationLifetime` injection pattern, `PodIdentityOptions` usage, `CoordinationV1.DeleteNamespacedLeaseAsync` as API shape reference
- `src/SnmpCollector/Telemetry/PreferredLeaderService.cs` — `IsPreferredPod`, `volatile bool` pattern
- `src/SnmpCollector/Configuration/LeaseOptions.cs` — `DurationSeconds`, `Name`, `Namespace`, heartbeat lease name convention
- `src/SnmpCollector/Configuration/PodIdentityOptions.cs` — `PodIdentity` field for `holderIdentity`
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — registration patterns, no changes needed
- KubernetesClient 18.0.13 DLL reflection probe — confirmed `CreateNamespacedLeaseAsync`, `ReplaceNamespacedLeaseAsync` signatures; confirmed `V1LeaseSpec.RenewTime/AcquireTime` are `DateTime?` with `Kind=Utc` when using `DateTime.UtcNow`
- `IHostApplicationLifetime` reflection (net9.0) — confirmed `ApplicationStarted` is `CancellationToken`

### Secondary (MEDIUM confidence)
- `.planning/phases/86-preferredheartbeatservice-writer-path/86-CONTEXT.md` — locked decisions and Claude's-discretion areas
- `.planning/phases/85-preferredheartbeatservice-reader-path/85-RESEARCH.md` — prior phase context

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all verified against existing project
- Architecture: HIGH — create/replace signatures verified by reflection; ApplicationStarted CancellationToken confirmed
- Pitfalls: HIGH — resourceVersion requirement and CreateNamespacedLeaseAsync signature asymmetry both verified
- Readiness gate placement: HIGH — confirmed by IHostApplicationLifetime API shape
- Quartz job instance lifetime: MEDIUM — open question; pattern is defensive regardless

**Research date:** 2026-03-26
**Valid until:** 2026-04-25 (stable library versions; KubernetesClient 18.0.13 locked by csproj)
