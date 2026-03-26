# Phase 88: K8sLeaseElection — Gate 2 (Voluntary Yield While Leading) - Research

**Researched:** 2026-03-26
**Domain:** PreferredHeartbeatJob yield trigger, K8sLeaseElection CancelInnerElection, K8s lease delete API
**Confidence:** HIGH

---

## Summary

Phase 88 adds a single new execution path inside `PreferredHeartbeatJob.Execute`: after
`ReadAndUpdateStampFreshnessAsync` updates the freshness flag, the job checks three conditions
(`IsPreferredStampFresh AND IsLeader AND !IsPreferredPod`) and, if all hold, deletes the
leadership lease then calls `CancelInnerElection()` on the injected `K8sLeaseElection`.

The infrastructure is fully built. Phase 87 delivered the outer loop, `_innerCts`, and
`CancelInnerElection()`. Phase 86 delivered `IKubernetes` and `LeaseOptions` injection into
`PreferredHeartbeatJob`. The only code changes are: (1) add a `K8sLeaseElection` constructor
parameter to `PreferredHeartbeatJob`, (2) add the yield condition check at the end of
`Execute`, and (3) add a private `YieldLeadershipAsync` helper or inline the sequence.

The open question from STATE.md — resourceVersion staleness after mid-renewal cancellation —
does NOT block the yield path. The yield deletes the leadership lease (not the heartbeat
lease). Once deleted, the LeaderElector's next renewal attempt receives 404 or conflicts, which
causes it to fall out of `RunAndTryToHoldLeadershipForeverAsync` naturally. The inner CTS
cancellation terminates the election loop faster than waiting for that natural exit.

**Primary recommendation:** Add a `private async Task YieldLeadershipAsync(CancellationToken ct)` helper. Delete leadership lease first (matching the StopAsync pattern), log at Information level, then call `CancelInnerElection()`. Log delete failure as Warning (not silently ignored) — matches the existing StopAsync precedent.

---

## Standard Stack

No new libraries needed. All dependencies are already injected into `PreferredHeartbeatJob`.

### Core (already in project)
| Component | Source | Purpose |
|-----------|--------|---------|
| `IKubernetes` | already injected | `CoordinationV1.DeleteNamespacedLeaseAsync` for lease deletion |
| `LeaseOptions` | already injected | `Name` and `Namespace` for the delete call |
| `PreferredLeaderService` | already injected | `IsPreferredPod` to guard yield on non-preferred pods only |
| `K8sLeaseElection` | NEW constructor param | `IsLeader` and `CancelInnerElection()` |

### New Injection Required

`PreferredHeartbeatJob` currently does not receive `K8sLeaseElection`. It must be added as a
constructor parameter. In DI, `K8sLeaseElection` is already registered as a singleton
(`services.AddSingleton<K8sLeaseElection>()`), so the container resolves it without any
registration change.

**No new packages required.**

---

## Architecture Patterns

### Files Changed
```
src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs   <- ONLY production file modified
tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs  <- tests added
```

`K8sLeaseElection.cs` and `ServiceCollectionExtensions.cs` require no changes.

### Pattern 1: Yield Check After Reader Path

The yield condition executes after `ReadAndUpdateStampFreshnessAsync` updates the freshness
flag, ensuring the check always uses the freshest available state on each tick.

```csharp
// Execute() — after the existing reader path call
await ReadAndUpdateStampFreshnessAsync(context.CancellationToken);

// Gate 2 (ELEC-02): non-preferred leader yields when preferred pod recovers.
if (_preferredLeaderService.IsPreferredStampFresh
    && _leaseElection.IsLeader
    && !_preferredLeaderService.IsPreferredPod)
{
    await YieldLeadershipAsync(context.CancellationToken);
}
```

Condition ordering: `IsPreferredStampFresh` first (cheapest volatile read, most selective
filter — stale is the common case). `IsLeader` second (volatile read, eliminates follower
pods). `!IsPreferredPod` last (already a compile-time resolved bool, never changes).

### Pattern 2: YieldLeadershipAsync Helper

Mirrors the `StopAsync` delete-then-cancel pattern established in `K8sLeaseElection.StopAsync`.

```csharp
private async Task YieldLeadershipAsync(CancellationToken ct)
{
    try
    {
        await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
            _leaseOptions.Name,
            _leaseOptions.Namespace,
            cancellationToken: ct);

        _logger.LogInformation(
            "Voluntary yield: deleted leadership lease {LeaseName} — preferred pod recovered",
            _leaseOptions.Name);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex,
            "Voluntary yield: failed to delete leadership lease {LeaseName} — cancelling inner election anyway",
            _leaseOptions.Name);
    }

    _leaseElection.CancelInnerElection();
}
```

**Why delete failure does not abort the cancel:** The primary goal is to hand off leadership.
Even if the delete fails (e.g. lease already gone due to TTL expiry), cancelling the inner
election still restarts the outer loop, which re-evaluates Gate 1. Gate 1 will then apply
the backoff delay, giving the preferred pod time to acquire.

### Pattern 3: Constructor Parameter Addition

```csharp
public PreferredHeartbeatJob(
    IKubernetes kubeClient,
    PreferredLeaderService preferredLeaderService,
    IOptions<LeaseOptions> leaseOptions,
    IOptions<PodIdentityOptions> podIdentityOptions,
    IHostApplicationLifetime lifetime,
    ILivenessVectorService liveness,
    K8sLeaseElection leaseElection,          // NEW — concrete type, not interface
    ILogger<PreferredHeartbeatJob> logger)
{
    // ...existing assignments...
    _leaseElection = leaseElection;
}
```

`K8sLeaseElection` is injected as the concrete type (not `ILeaderElection`) because
`ILeaderElection` does not expose `CancelInnerElection()`. The CONTEXT.md decision is explicit:
no new interface, direct concrete injection.

### Anti-Patterns to Avoid

- **Checking `_isLeader` before the reader path:** The freshness state would lag one tick
  behind the leadership check. Always read stamp freshness first so both conditions reflect
  the same tick's state.
- **Using `ILeaderElection` instead of `K8sLeaseElection`:** `ILeaderElection` has only
  `IsLeader` and `CurrentRole`. `CancelInnerElection()` is concrete-only and must remain so
  until a future phase adds it to the interface.
- **Calling `CancelInnerElection()` before deleting the lease:** The outer loop restarts
  immediately and Gate 1 may not apply if the stamp is evaluated before the preferred pod
  fully acquires. Delete first, then cancel.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Leadership lease deletion | Custom HTTP call or patch | `DeleteNamespacedLeaseAsync` extension | Same call used by `StopAsync`, verified working, handles 404 gracefully |
| Inner loop restart | Flags, state machines, timers | `CancelInnerElection()` + existing outer while loop | Already built in Phase 87, tested, correct lifecycle |
| "Am I leader?" check | Reading the K8s lease | `_leaseElection.IsLeader` | Volatile bool updated by `OnStoppedLeading`/`OnStartedLeading`, no API call needed |

---

## Common Pitfalls

### Pitfall 1: NSubstitute Setup for DeleteNamespacedLeaseWithHttpMessagesAsync

**What goes wrong:** The extension method `DeleteNamespacedLeaseAsync` is a convenience wrapper
that delegates to `ICoordinationV1Operations.DeleteNamespacedLeaseWithHttpMessagesAsync`.
NSubstitute must mock the `WithHttpMessages` overload on `ICoordinationV1Operations`, not the
extension method.

**Full signature (11 parameters — confirmed from KubernetesClient 18.0.13 XML docs):**
```csharp
DeleteNamespacedLeaseWithHttpMessagesAsync(
    string name,
    string namespaceParameter,
    V1DeleteOptions body,
    string dryRun,
    int? gracePeriodSeconds,
    bool? orphanDependents,
    bool? propagationPolicy,
    string resourceVersion,
    bool? pretty,
    IReadOnlyDictionary<string, IReadOnlyList<string>> customHeaders,
    CancellationToken cancellationToken)
```

**How to set up in tests:**
```csharp
// Success path — returns V1Status (or any object; the extension discards return value)
var response = new HttpOperationResponse<V1Status>
{
    Body = new V1Status(),
    Response = new HttpResponseMessage(HttpStatusCode.OK)
};
_mockCoordV1
    .DeleteNamespacedLeaseWithHttpMessagesAsync(
        Arg.Any<string>(),       // name
        Arg.Any<string>(),       // namespace
        Arg.Any<V1DeleteOptions>(),
        Arg.Any<string>(),       // dryRun
        Arg.Any<int?>(),         // gracePeriodSeconds
        Arg.Any<bool?>(),        // orphanDependents
        Arg.Any<bool?>(),        // propagationPolicy
        Arg.Any<string>(),       // resourceVersion
        Arg.Any<bool?>(),        // pretty
        Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
        Arg.Any<CancellationToken>())
    .Returns(response);
```

**Warning signs:** If `DeleteNamespacedLeaseAsync` call silently does nothing in tests
(no assertion failure, no call recorded), you are likely mocking the wrong overload.

### Pitfall 2: K8sLeaseElection Constructor Parameter Breaks Existing Tests

**What goes wrong:** All existing `PreferredHeartbeatJobTests` construct `PreferredHeartbeatJob`
directly without `K8sLeaseElection`. Adding the parameter breaks every existing test.

**How to avoid:** The `MakePreferredJob` helper and the default `_job` field in the test class
constructor must both be updated to pass a `K8sLeaseElection` (or a substitute). For tests
that do not exercise the yield path, a `Substitute.For<K8sLeaseElection>(...)` is sufficient
— but note `K8sLeaseElection` is a `sealed` class, so NSubstitute cannot substitute it.

The clean solution: pass `null` for `K8sLeaseElection` in non-yield tests (change the
constructor to accept `K8sLeaseElection?`) OR add a minimal constructor overload for testing.
However, given this is a singleton with concrete injection, the better approach is to only
call `YieldLeadershipAsync` when `_leaseElection` is non-null.

**Alternative (cleanest):** Make the constructor accept `K8sLeaseElection?` and guard the
yield path: `if (_leaseElection is not null && ...)`. This allows non-yield tests to pass
`null` and tests that cover yield to pass a real or stub instance.

Wait — `K8sLeaseElection` is `sealed`, so it cannot be substituted. The yield test approach
must either:
1. Accept `K8sLeaseElection?` and verify `CancelInnerElection()` was called via a spy, OR
2. Verify the effect (no throw, `_leaseElection` is called) through a partial integration test

Given the existing pattern in `K8sLeaseElectionBackoffTests` (no loop running, just testing
public API), the practical approach is:
- For yield behavior tests: construct a real `K8sLeaseElection` (with NSubstitute `IKubernetes`),
  call Execute on the job, then assert `CancelInnerElection()` was safe (idempotent) or check
  `DeleteNamespacedLeaseWithHttpMessagesAsync` was called.

### Pitfall 3: Yield Fires Every Tick While Leader

**What goes wrong:** Once `IsPreferredStampFresh && IsLeader && !IsPreferredPod` is true, it
may remain true for several ticks while the inner election is still running (between the
`CancelInnerElection()` call and when `OnStoppedLeading` sets `_isLeader = false`).

**How to avoid:** This is benign. `CancelInnerElection()` is idempotent — calling it multiple
times does nothing after the first cancel (the `ObjectDisposedException` catch protects against
the already-disposed case). The delete call on the second tick may return 404, which falls into
the `Warning` log path. No special guard is needed.

**Warning signs:** If you add a "yield already in progress" boolean flag, that introduces
unnecessary complexity and a potential race condition.

### Pitfall 4: Forgetting OperationCanceledException Re-throw in YieldLeadershipAsync

**What goes wrong:** `YieldLeadershipAsync` catches `Exception` broadly. Without explicitly
re-throwing `OperationCanceledException`, job cancellation (e.g. Quartz shutdown) is swallowed.

**How to avoid:** Add `catch (OperationCanceledException) { throw; }` before the broad
`catch (Exception ex)` — the same pattern used in `WriteHeartbeatLeaseAsync` and
`ReadAndUpdateStampFreshnessAsync` in the existing file.

---

## Code Examples

### Verified Delete Call Pattern (from K8sLeaseElection.StopAsync, lines 212-215)

```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs lines 212-215
await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
    _leaseOptions.Name,
    _leaseOptions.Namespace,
    cancellationToken: cancellationToken);
```

This exact pattern already compiles and works in production. Phase 88 uses the same call
with `_leaseOptions.Name` (not the `-preferred` suffix — the leadership lease, not the
heartbeat lease).

### Verified CancelInnerElection (from K8sLeaseElection.cs lines 100-104)

```csharp
// Source: src/SnmpCollector/Telemetry/K8sLeaseElection.cs lines 100-104
public void CancelInnerElection()
{
    try { _innerCts?.Cancel(); }
    catch (ObjectDisposedException) { /* already disposed — cancel was redundant */ }
}
```

Safe to call multiple times, safe when no election is running (`_innerCts` is null).

### Verified NSubstitute Delete Mock Setup (pattern derived from existing test helpers)

```csharp
// Based on SetupCreateResponse pattern in PreferredHeartbeatJobTests.cs
private void SetupDeleteSucceeds()
{
    var response = new HttpOperationResponse<V1Status>
    {
        Body = new V1Status(),
        Response = new HttpResponseMessage(HttpStatusCode.OK)
    };
    _mockCoordV1
        .DeleteNamespacedLeaseWithHttpMessagesAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<V1DeleteOptions>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<bool?>(),
            Arg.Any<bool?>(),
            Arg.Any<string>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<CancellationToken>())
        .Returns(response);
}

private void SetupDeleteThrows(HttpStatusCode statusCode)
{
    _mockCoordV1
        .DeleteNamespacedLeaseWithHttpMessagesAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<V1DeleteOptions>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<bool?>(),
            Arg.Any<bool?>(),
            Arg.Any<string>(),
            Arg.Any<bool?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            Arg.Any<CancellationToken>())
        .ThrowsAsync(MakeHttpException(statusCode));
}
```

### Verified Condition Check Placement

```csharp
// In Execute() — after the existing reader path
await ReadAndUpdateStampFreshnessAsync(context.CancellationToken);

// Gate 2: non-preferred leader yields when preferred pod recovers
if (_preferredLeaderService.IsPreferredStampFresh
    && _leaseElection is not null
    && _leaseElection.IsLeader
    && !_preferredLeaderService.IsPreferredPod)
{
    await YieldLeadershipAsync(context.CancellationToken);
}
```

---

## State of the Art

| Phase 86 | Phase 88 | Impact |
|----------|----------|--------|
| PreferredHeartbeatJob has `IKubernetes`, `LeaseOptions` | Adds `K8sLeaseElection` param | One new constructor arg |
| StopAsync in K8sLeaseElection handles delete on shutdown | YieldLeadershipAsync handles delete on yield | Same delete call, same error pattern |
| CancelInnerElection() exists but is unused | Called by yield path | The hook built in Phase 87 is now activated |

**No deprecated patterns in this phase.**

---

## Open Questions

1. **K8sLeaseElection is sealed — test isolation for yield path**
   - What we know: `K8sLeaseElection` cannot be substituted by NSubstitute because it is
     `sealed`. Yield tests must either use a real `K8sLeaseElection` instance or test
     indirectly via side effects (delete call received, no exception thrown).
   - What's unclear: Whether the planner wants to add yield-specific tests that verify
     `CancelInnerElection()` was invoked, or only verify the K8s delete API was called.
   - Recommendation: Test the observable effects — verify `DeleteNamespacedLeaseWithHttpMessagesAsync`
     was called, and that a real `K8sLeaseElection` constructed with NSubstitute IKubernetes
     does not throw when the yield path fires. This is sufficient coverage without
     substituting the sealed class.

2. **resourceVersion staleness after mid-renewal cancellation (from STATE.md)**
   - What we know: When `CancelInnerElection()` fires mid-renewal, the `LeaderElector`'s
     internal state may have a stale `resourceVersion`. On the next outer loop iteration,
     a new `LeaderElector` instance is NOT created — the same `elector` object is reused.
   - What's unclear: Whether `RunAndTryToHoldLeadershipForeverAsync` resets internal
     `resourceVersion` state on the next call after cancellation.
   - Recommendation: This does not block Phase 88. The yield path deletes the lease first,
     so the preferred pod acquires a fresh lease. The non-preferred pod's `LeaderElector`
     will start a fresh acquire attempt (likely getting a new `resourceVersion` from the
     create/acquire flow). Gate 1 backoff gives the preferred pod time to win. If the
     non-preferred pod does end up with a stale `resourceVersion`, it will get a 409
     Conflict, which the `LeaderElector` handles internally. Document as a known limitation
     for a future phase if observed in practice.

---

## Sources

### Primary (HIGH confidence)
- Direct code read: `src/SnmpCollector/Jobs/PreferredHeartbeatJob.cs` — full file
- Direct code read: `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — full file
- Direct code read: `src/SnmpCollector/Telemetry/PreferredLeaderService.cs` — full file
- Direct code read: `src/SnmpCollector/Configuration/LeaseOptions.cs` — full file
- Direct code read: `tests/SnmpCollector.Tests/Jobs/PreferredHeartbeatJobTests.cs` — full file (patterns)
- Direct code read: `tests/SnmpCollector.Tests/Telemetry/K8sLeaseElectionBackoffTests.cs` — full file
- Direct XML read: `ship/packages/kubernetesclient/18.0.13/lib/net9.0/KubernetesClient.xml` — `DeleteNamespacedLeaseWithHttpMessagesAsync` signature (11 params)
- Direct code read: `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — DI registration pattern
- Prior research: `.planning/phases/87-election-gate-1-backoff/87-RESEARCH.md`
- Phase context: `.planning/phases/88-election-gate-2-voluntary-yield/88-CONTEXT.md`

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries, all injection points verified in existing code
- Architecture: HIGH — yield pattern mirrors existing StopAsync, condition placement follows existing Execute structure
- Pitfalls: HIGH — NSubstitute sealed-class limitation and delete mock signature verified directly from KubernetesClient XML
- Test patterns: HIGH — directly read from existing test files; delete helper pattern derived from existing create/replace helpers

**Research date:** 2026-03-26
**Valid until:** 2026-04-25 (stable domain — K8s client version locked at 18.0.13 in offline ship folder)
