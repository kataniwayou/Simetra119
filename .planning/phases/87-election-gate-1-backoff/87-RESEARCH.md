# Phase 87: K8sLeaseElection — Gate 1 (Backoff Before Acquire) - Research

**Researched:** 2026-03-26
**Domain:** K8sLeaseElection outer loop refactor, CancellationTokenSource lifecycle, BackgroundService patterns
**Confidence:** HIGH

---

## Summary

Phase 87 modifies `K8sLeaseElection.ExecuteAsync` to wrap the existing single `RunAndTryToHoldLeadershipForeverAsync` call in an outer `while` loop with a backoff check. The current file is 143 lines. The change is surgical: it adds two injected dependencies (`PreferredLeaderService` and `IPreferredStampReader`), a `_innerCts` field, and the outer loop logic inside `ExecuteAsync`. `StopAsync` requires no change beyond `_innerCts` disposal.

The `LeaderElector` library (`KubernetesClient` 18.0.13) fires `OnStoppedLeading` unconditionally in a `finally` block — it already fires on cancellation. The existing `OnStoppedLeading` handler only sets `_isLeader = false`, which is already idempotent and correct. No existing handler behavior needs to change.

The highest implementation risk is `_innerCts` lifecycle: it must be created fresh each iteration (not reused after cancellation), disposed after each use, and never left undisposed. The `stoppingToken` already cancels `base.StopAsync`; `_innerCts` is linked to it so outer shutdown propagates correctly.

**Primary recommendation:** Inject `PreferredLeaderService` (concrete, not interface) for `IsPreferredPod`, and `IPreferredStampReader` for `IsPreferredStampFresh`. Expose `_innerCts` via a `CancelInnerElection()` method for Phase 88 — do not expose the field directly.

---

## Standard Stack

No new libraries needed. All dependencies already present.

### Core (already in project)
| Component | Version/Source | Purpose |
|-----------|---------------|---------|
| `KubernetesClient` | 18.0.13 | `LeaderElector`, `RunAndTryToHoldLeadershipForeverAsync` |
| `Microsoft.Extensions.Hosting` | (framework) | `BackgroundService`, `CancellationToken stoppingToken` |
| `PreferredLeaderService` | project | `IsPreferredPod` — resolved at startup, never changes |
| `IPreferredStampReader` | project | `IsPreferredStampFresh` — volatile bool, thread-safe |

### No new packages required

**Installation:** none

---

## Architecture Patterns

### Recommended Project Structure (no change)
```
src/SnmpCollector/Telemetry/
├── K8sLeaseElection.cs   ← ONLY file modified in this phase
├── ILeaderElection.cs    ← no change
├── IPreferredStampReader.cs  ← no change
├── PreferredLeaderService.cs ← no change
└── NullPreferredStampReader.cs ← no change
```

### Pattern 1: Outer Loop with Linked Inner CTS

**What:** `ExecuteAsync` becomes a `while (!stoppingToken.IsCancellationRequested)` loop. Each iteration creates a fresh `_innerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)`, calls `RunAndTryToHoldLeadershipForeverAsync(_innerCts.Token)`, then disposes `_innerCts`.

**When to use:** Whenever you need the ability to restart a long-running operation without shutting down the entire service — exactly the Phase 88 voluntary yield requirement.

**Verified pseudocode (from CONTEXT.md specifics):**
```csharp
// _innerCts is a field: private CancellationTokenSource? _innerCts;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var identity = _podIdentityOptions.PodIdentity ?? Environment.MachineName;
    var leaseLock = new LeaseLock(_kubeClient, _leaseOptions.Namespace, _leaseOptions.Name, identity);
    var config = new LeaderElectionConfig(leaseLock)
    {
        LeaseDuration = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds),
        RetryPeriod = TimeSpan.FromSeconds(_leaseOptions.RenewIntervalSeconds),
        RenewDeadline = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds - 2)
    };

    var elector = new LeaderElector(config);

    elector.OnStartedLeading += () =>
    {
        _isLeader = true;
        _logger.LogInformation("Acquired leadership for lease {LeaseName}", _leaseOptions.Name);
    };

    elector.OnStoppedLeading += () =>
    {
        _isLeader = false;
        _logger.LogInformation("Lost leadership for lease {LeaseName}", _leaseOptions.Name);
    };

    elector.OnNewLeader += leader =>
    {
        _logger.LogInformation("New leader observed: {Leader}", leader);
    };

    while (!stoppingToken.IsCancellationRequested)
    {
        // Backoff: non-preferred pod delays when preferred pod is alive
        if (!_preferredLeaderService.IsPreferredPod && _stampReader.IsPreferredStampFresh)
        {
            _logger.LogDebug("Preferred pod is alive — delaying election attempt for {DurationSeconds}s", _leaseOptions.DurationSeconds);
            await Task.Delay(TimeSpan.FromSeconds(_leaseOptions.DurationSeconds), stoppingToken);
            continue;  // re-evaluate at top of loop
        }

        using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _innerCts = innerCts;

        try
        {
            await elector.RunAndTryToHoldLeadershipForeverAsync(innerCts.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Outer shutdown — exit loop cleanly
            break;
        }
        catch (OperationCanceledException)
        {
            // Inner cancel (Phase 88 voluntary yield) — loop continues, re-evaluate backoff
        }
        finally
        {
            _innerCts = null;
        }
    }
}
```

**Key decision:** `elector` is created once outside the loop. The `LeaderElector` is reused across inner restarts — creating it each iteration would be wasteful and is not needed.

**Key decision:** `using var innerCts = ...` ensures disposal even on exception paths. Assigning to `_innerCts` field inside the block gives Phase 88 something to cancel.

### Pattern 2: Phase 88 Exposure via Method

**What:** Expose `_innerCts` cancellation through a public method, not the field directly.

**Why method over field:** Field is nullable, has threading concerns. A method handles the null check internally and is the stable API contract Phase 88 can call.

```csharp
/// <summary>
/// Cancels the current inner election attempt, causing the outer loop
/// to restart and re-evaluate backoff. Used by Phase 88 voluntary yield.
/// No-op if no inner election is in progress.
/// </summary>
public void CancelInnerElection()
{
    _innerCts?.Cancel();
}
```

**Visibility:** `public` (Phase 88 calls it from outside, or `internal` if in same assembly — both work). The method name `CancelInnerElection` is descriptive of intent for Phase 88.

### Anti-Patterns to Avoid

- **Reusing a cancelled CTS:** `CancellationTokenSource` cannot be reset after cancellation. Always create fresh with `CreateLinkedTokenSource` each iteration.
- **Disposing `_innerCts` from `StopAsync`:** `StopAsync` calls `base.StopAsync` which cancels `stoppingToken`. Since `_innerCts` is linked to `stoppingToken`, it will be cancelled automatically. The `using` block in `ExecuteAsync` handles disposal. No additional disposal needed in `StopAsync`.
- **Creating `elector` inside the loop:** The `LeaderElector` instance and its event handlers should be created once. Recreating each iteration leaks event handler registrations if any reference is held externally, and is unnecessary since the elector's internal state resets naturally after `RunAndTryToHoldLeadershipForeverAsync` returns.
- **Using `_innerCts.Token` for the outer backoff delay:** The backoff `Task.Delay` should use `stoppingToken`, not `_innerCts.Token`. The inner CTS does not exist yet at the point of backoff delay.
- **`continue` vs fall-through after backoff:** Using `continue` re-checks the `while` condition (which checks `stoppingToken`), giving the shutdown signal a chance to interrupt between backoff cycles. Fall-through would immediately re-evaluate the backoff condition without the loop guard check — functionally equivalent but `continue` is clearer.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Linked cancellation | Manual flag + polling | `CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)` | Propagates parent token automatically; standard .NET pattern |
| Backoff | Custom timer/Quartz job | `await Task.Delay(duration, stoppingToken)` | Cancellable, allocates nothing, zero framework dependency |
| IsPreferredPod check | Re-read env var | `PreferredLeaderService.IsPreferredPod` | Already resolved at startup; `volatile bool` read is O(1) |

---

## Common Pitfalls

### Pitfall 1: _innerCts Field Nullable Threading

**What goes wrong:** `_innerCts` is written from `ExecuteAsync` and read from `CancelInnerElection()` (Phase 88). Without care, `CancelInnerElection` could read `null` or a disposed instance.

**Why it happens:** The field is set inside the loop iteration and cleared in the `finally` block. A concurrent call to `CancelInnerElection` can race with disposal.

**How to avoid:** The `?.Cancel()` null-conditional handles the `null` case. Cancelling a `CancellationTokenSource` after it has been disposed throws `ObjectDisposedException` — but in practice the `finally` clears `_innerCts = null` before `Dispose()` runs (via `using`), so the race window is tiny. For Phase 88 robustness, wrap `CancelInnerElection` in a try/catch for `ObjectDisposedException`.

```csharp
public void CancelInnerElection()
{
    try { _innerCts?.Cancel(); }
    catch (ObjectDisposedException) { /* already disposed, cancel was redundant */ }
}
```

**Warning signs:** `ObjectDisposedException` log at shutdown when Phase 88 and shutdown race.

### Pitfall 2: OnStoppedLeading Fires on _innerCts Cancellation

**What goes wrong:** When `_innerCts` is cancelled (Phase 88 voluntary yield), `LeaderElector.RunAndTryToHoldLeadershipForeverAsync` exits, and `OnStoppedLeading` fires — setting `_isLeader = false`. This is correct and expected. However, if `OnStoppedLeading` were ever expanded to do more than set `_isLeader = false` (e.g., lease deletion), it would incorrectly trigger on voluntary yield.

**Why it happens:** The library fires `OnStoppedLeading` in a `finally` block regardless of cancellation cause.

**How to avoid:** `OnStoppedLeading` MUST remain a pure `_isLeader = false` setter. Lease deletion lives only in `StopAsync`. This is already the case and must be preserved.

**Warning signs:** Lease deletion called at unexpected times; followers see spurious fast-failover signals.

### Pitfall 3: BackgroundService StopAsync Double-Call

**What goes wrong:** `GracefulShutdownService` explicitly calls `leaseService.StopAsync(CancellationToken.None)` as Step 1. The framework then calls `StopAsync` again (in reverse registration order). BackgroundService's `StopAsync` is idempotent by design — the second call is a no-op. This doesn't change with the outer loop.

**Why it happens:** `GracefulShutdownService` is registered last and stops first, explicitly calling `K8sLeaseElection.StopAsync` before the framework does.

**How to avoid:** No action needed. This is already documented in `GracefulShutdownService`'s XML doc. The outer loop adds no new risk here because `stoppingToken` cancellation (from `base.StopAsync`) will cause the outer `while` condition to exit cleanly.

**Warning signs:** None expected — idempotency already verified in production.

### Pitfall 4: DI — K8sLeaseElection Constructor Now Needs PreferredLeaderService

**What goes wrong:** `K8sLeaseElection` currently does not accept `PreferredLeaderService` or `IPreferredStampReader`. Adding them as constructor parameters requires updating the DI registration. The constructor currently takes 5 params; it will take 7.

**Why it happens:** DI auto-wires by type. Missing registration causes `InvalidOperationException` at startup.

**How to avoid:** The K8s branch in `ServiceCollectionExtensions` already registers both `PreferredLeaderService` and `IPreferredStampReader`. Since `K8sLeaseElection` is registered as a singleton, DI will resolve both dependencies correctly with no additional registration code.

**Warning signs:** `InvalidOperationException: Unable to resolve service for type 'PreferredLeaderService'` at startup.

### Pitfall 5: Task.Delay Continuation After stoppingToken Cancellation

**What goes wrong:** `await Task.Delay(duration, stoppingToken)` throws `OperationCanceledException` when `stoppingToken` is cancelled during the backoff wait. If not caught, this propagates out of `ExecuteAsync` as an unhandled exception.

**Why it happens:** `Task.Delay` with a cancelled token throws, not returns.

**How to avoid:** The `while (!stoppingToken.IsCancellationRequested)` guard catches this naturally — if `stoppingToken` fires, the delay throws, `ExecuteAsync` propagates the `OperationCanceledException`, and `BackgroundService` handles it. This is standard `BackgroundService` contract: `OperationCanceledException` on `stoppingToken` is the normal exit path, not an error. No try/catch needed around the `Task.Delay`.

**Warning signs:** Unnecessary try/catch around the delay that swallows the cancellation.

---

## Code Examples

### Existing ExecuteAsync (baseline)
```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs (line 69-106)
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var identity = _podIdentityOptions.PodIdentity ?? Environment.MachineName;
    var leaseLock = new LeaseLock(_kubeClient, _leaseOptions.Namespace, _leaseOptions.Name, identity);
    var config = new LeaderElectionConfig(leaseLock) { /* ... */ };
    var elector = new LeaderElector(config);

    elector.OnStartedLeading += () => { _isLeader = true; /* log */ };
    elector.OnStoppedLeading += () => { _isLeader = false; /* log */ };
    elector.OnNewLeader += leader => { /* log */ };

    await elector.RunAndTryToHoldLeadershipForeverAsync(stoppingToken);
}
```

### LeaderElector.RunAndTryToHoldLeadershipForeverAsync (library behavior)
```csharp
// Source: KubernetesClient 18.0.13 (verified via GitHub raw source)
public async Task RunAndTryToHoldLeadershipForeverAsync(CancellationToken cancellationToken = default)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await RunUntilLeadershipLostAsync(cancellationToken).ConfigureAwait(false);
    }
}

// RunUntilLeadershipLostAsync fires OnStoppedLeading in finally:
try
{
    OnStartedLeading?.Invoke();
    // renewal loop with cancellationToken.ThrowIfCancellationRequested()
}
finally
{
    OnStoppedLeading?.Invoke();
}
```

### CancellationTokenSource.CreateLinkedTokenSource pattern
```csharp
// Standard .NET pattern — HIGH confidence
// _innerCts is a volatile field: private volatile CancellationTokenSource? _innerCts;

while (!stoppingToken.IsCancellationRequested)
{
    using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
    _innerCts = innerCts;
    try
    {
        await SomeLongRunningAsync(innerCts.Token);
    }
    finally
    {
        _innerCts = null;
    }
}
```

### Constructor change (new parameters)
```csharp
// Phase 87 adds two new constructor parameters:
public K8sLeaseElection(
    IOptions<LeaseOptions> leaseOptions,
    IOptions<PodIdentityOptions> podIdentityOptions,
    IKubernetes kubeClient,
    IHostApplicationLifetime lifetime,
    ILogger<K8sLeaseElection> logger,
    PreferredLeaderService preferredLeaderService,   // NEW: IsPreferredPod
    IPreferredStampReader stampReader)               // NEW: IsPreferredStampFresh
```

No DI registration change needed — both are already registered in the K8s branch of `ServiceCollectionExtensions`.

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|-----------------|--------|
| Single `RunAndTryToHoldLeadershipForeverAsync(stoppingToken)` call | Outer loop with `_innerCts` per iteration | Enables Phase 88 voluntary yield via `CancelInnerElection()` |
| `OnStoppedLeading` is internal to LeaderElector | `OnStoppedLeading` fires on all exits including inner-CTS cancel | Handler must remain `_isLeader = false` only — no new side effects |

---

## Open Questions

1. **`_innerCts` field volatility**
   - What we know: `_innerCts` is read from `CancelInnerElection()` (Phase 88, separate thread potentially) and written from `ExecuteAsync`.
   - What's unclear: Whether `volatile` is sufficient or `Interlocked` is needed.
   - Recommendation: `volatile` is sufficient. `CancelInnerElection` does a null-conditional call (`?.Cancel()`), not a compare-exchange. A stale null read means the cancel is a no-op — safe for Phase 88 (the election will lose leadership naturally via TTL if the voluntary yield races with disposal).

2. **OperationCanceledException handling in outer loop**
   - What we know: `RunAndTryToHoldLeadershipForeverAsync` can throw `OperationCanceledException` when its token is cancelled.
   - What's unclear: Whether the library wraps this or throws raw.
   - Recommendation: The outer loop catch block should differentiate `stoppingToken.IsCancellationRequested` (exit loop) from inner cancel (continue loop), as shown in the pseudocode above. The `BackgroundService` base class handles `OperationCanceledException` on `stoppingToken` as a normal exit.

3. **Test strategy for K8sLeaseElection with outer loop**
   - What we know: Existing `LeaderElectionTests.cs` tests only `AlwaysLeaderElection` and DI patterns — no tests for `K8sLeaseElection` execution logic (requires real K8s or mock `IKubernetes`).
   - What's unclear: Whether mock-based unit tests for the outer loop behavior are expected in this phase.
   - Recommendation: Add unit tests using a stub `IPreferredStampReader` (or `NullPreferredStampReader`) and a mock `IKubernetes`. The test can verify: (a) when `IsPreferredStampFresh=true` and `IsPreferredPod=false`, `Task.Delay` is awaited before election; (b) when `IsPreferredStampFresh=false`, election proceeds immediately; (c) when `IsPreferredPod=true`, election proceeds immediately regardless of stamp. These are pure logic tests requiring only the backoff path, not actual lease operations.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — complete file read
- `src/SnmpCollector/Telemetry/IPreferredStampReader.cs` — interface contract
- `src/SnmpCollector/Telemetry/PreferredLeaderService.cs` — `IsPreferredPod`, `IsPreferredStampFresh`
- `src/SnmpCollector/Telemetry/NullPreferredStampReader.cs` — feature-off behavior
- `src/SnmpCollector/Lifecycle/GracefulShutdownService.cs` — StopAsync call pattern
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — DI registrations
- `.planning/phases/87-election-gate-1-backoff/87-CONTEXT.md` — locked decisions, pseudocode
- `tests/SnmpCollector.Tests/Telemetry/LeaderElectionTests.cs` — test patterns
- `tests/SnmpCollector.Tests/Telemetry/PreferredLeaderServiceTests.cs` — test patterns

### Secondary (MEDIUM confidence)
- `https://github.com/kubernetes-client/csharp/blob/master/src/KubernetesClient/LeaderElection/LeaderElector.cs` — fetched via WebFetch; `RunAndTryToHoldLeadershipForeverAsync` outer while loop and `OnStoppedLeading` in finally block confirmed

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all dependencies already in project, no new libraries
- Architecture (outer loop pattern): HIGH — verified against CONTEXT.md decisions and library source
- Pitfalls: HIGH — derived from code reading and known .NET CancellationTokenSource semantics
- Test strategy: MEDIUM — pattern inferred from existing test style, no existing K8sLeaseElection unit tests to reference

**Research date:** 2026-03-26
**Valid until:** 2026-04-26 (stable domain — no fast-moving dependencies)
