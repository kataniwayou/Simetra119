# Architecture Research

**Domain:** Preferred leader election with site-affinity — two-lease mechanism for SNMP monitoring
**Researched:** 2026-03-25
**Confidence:** HIGH (all claims derived from direct inspection of named source files)

---

## Context: What Already Exists

The system already has a working single-lease leader election:

- `K8sLeaseElection : BackgroundService, ILeaderElection` — runs a `LeaderElector` loop, exposes `volatile bool _isLeader`
- `ILeaderElection` — `IsLeader (bool)`, `CurrentRole (string)`
- `AlwaysLeaderElection` — local dev stub, always returns `IsLeader = true`
- `MetricRoleGatedExporter` — reads `ILeaderElection.IsLeader` to gate business metrics
- `CommandWorkerService` — reads `ILeaderElection.IsLeader` to gate SNMP SET dispatch
- `GracefulShutdownService` — calls `K8sLeaseElection.StopAsync` (deletes leadership lease)
- `ServiceCollectionExtensions` — registers K8s or local-dev election based on `IsInCluster()`

**None of these consumers change.** The interface contract (`ILeaderElection.IsLeader`) is preserved. What changes is the logic inside `K8sLeaseElection` — specifically, whether and when it competes for the leadership lease.

---

## System Overview: Two-Lease Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Kubernetes coordination.k8s.io/v1 Leases                       │
│                                                                  │
│  ┌──────────────────────────┐  ┌───────────────────────────┐    │
│  │  "snmp-collector-leader" │  │ "snmp-collector-preferred"│    │
│  │  Leadership lease        │  │ Heartbeat lease           │    │
│  │  (existing LeaderElector)│  │ (new — preferred pod only)│    │
│  └──────────────────────────┘  └───────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
           │                                │
           │                                │
           ▼                                ▼
  K8sLeaseElection (modified)     PreferredHeartbeatService (new)
  ┌─────────────────────────────────────────────────────────────┐
  │  Reads: IPreferredStampReader.IsPreferredStampFresh()       │
  │  Gate 1 (before try-to-acquire): if preferred stamp is      │
  │    fresh AND this pod is not preferred → stay follower       │
  │  Gate 2 (while leading): if preferred stamp becomes fresh   │
  │    AND this pod is not preferred → yield (delete lease)     │
  └─────────────────────────────────────────────────────────────┘
           │
           ▼
  ILeaderElection.IsLeader  (unchanged interface)
           │
  ┌────────┴───────────┐
  ▼                    ▼
MetricRoleGatedExporter  CommandWorkerService
(unchanged consumers)    (unchanged consumers)
```

---

## New Components

### 1. `SiteAffinityOptions` (Configuration)

**Location:** `src/SnmpCollector/Configuration/SiteAffinityOptions.cs`

New options class, bound from `"SiteAffinity"` config section.

```csharp
public sealed class SiteAffinityOptions
{
    public const string SectionName = "SiteAffinity";

    /// <summary>
    /// Node name of the preferred pod. Compared against NODE_NAME env var.
    /// Null/empty = no preference (any pod may lead freely, two-lease mechanism inactive).
    /// </summary>
    public string? PreferredNode { get; set; }

    /// <summary>
    /// Heartbeat lease resource name. Preferred pod stamps this lease while healthy.
    /// Non-preferred pods read it to determine backoff/yield behavior.
    /// </summary>
    public string HeartbeatLeaseName { get; set; } = "snmp-collector-preferred";

    /// <summary>
    /// Duration of the heartbeat lease in seconds.
    /// Non-preferred pods treat the stamp as "fresh" if age < HeartbeatDurationSeconds.
    /// Must be long enough to survive readiness probe timing and renewal jitter.
    /// </summary>
    [Range(5, 300)]
    public int HeartbeatDurationSeconds { get; set; } = 30;

    /// <summary>
    /// How often the preferred pod renews the heartbeat lease, in seconds.
    /// Must be less than HeartbeatDurationSeconds.
    /// </summary>
    [Range(1, 60)]
    public int HeartbeatRenewIntervalSeconds { get; set; } = 10;
}
```

**Rationale:** Isolated from `LeaseOptions` (leadership lease concerns) and `PodIdentityOptions` (pod name). SiteAffinity is a distinct operational concern.

---

### 2. `IPreferredStampReader` (Interface)

**Location:** `src/SnmpCollector/Telemetry/IPreferredStampReader.cs`

```csharp
public interface IPreferredStampReader
{
    /// <summary>
    /// Returns true if the preferred pod's heartbeat lease exists and was renewed
    /// within HeartbeatDurationSeconds. Non-preferred pods read this to decide
    /// whether to back off or yield leadership.
    /// </summary>
    bool IsPreferredStampFresh { get; }
}
```

Separates the read concern (consulted by `K8sLeaseElection`) from the write concern (`PreferredHeartbeatService`). Enables unit-testing `K8sLeaseElection` without a real Kubernetes client.

---

### 3. `PreferredHeartbeatService` (New BackgroundService)

**Location:** `src/SnmpCollector/Telemetry/PreferredHeartbeatService.cs`

Implements `BackgroundService` and `IPreferredStampReader`.

**Responsibilities:**

- Determines on startup whether `NODE_NAME == SiteAffinityOptions.PreferredNode`
- If preferred: runs a renewal loop that continuously holds the heartbeat lease via `LeaderElector` (or a direct patch-loop against the Lease API), starting only after the readiness gate is satisfied
- If not preferred: runs a watch/poll loop that reads the heartbeat lease and updates `volatile bool _isStampFresh` based on whether `renewTime` is within `HeartbeatDurationSeconds`
- Implements `IPreferredStampReader.IsPreferredStampFresh` from `_isStampFresh`

**Two sub-behaviors in one service:**

```
ExecuteAsync
    │
    ├─ if IsPreferredPod
    │       await WaitForReadinessGateAsync(stoppingToken)
    │       loop: renew heartbeat lease every HeartbeatRenewIntervalSeconds
    │
    └─ if not IsPreferredPod
            loop: read heartbeat lease every HeartbeatRenewIntervalSeconds
                  update _isStampFresh based on lease.RenewTime age
```

Alternative: split into two classes (`PreferredHeartbeatWriter` and `PreferredHeartbeatReader`), registered conditionally in DI. Either approach is valid. Single-class is simpler when the determination is made once at startup; two classes are cleaner if each path grows significantly. **Recommend single class initially.** The split is a mechanical refactor if warranted later.

**Readiness gate:** The preferred pod should not begin stamping the heartbeat lease until it is genuinely ready (readiness probe passes). The gate prevents a pod that is still loading ConfigMaps from prematurely claiming site-affinity and causing the current leader to yield to an unready pod. Implementation options:

- Poll the readiness endpoint internally (coupling but deterministic)
- Use `IHostApplicationLifetime.ApplicationStarted` as a proxy (fires after all `StartAsync` calls — reasonable approximation if readiness aligns with hosted-service startup)
- Inject `IReadinessHealthCheck` (direct, no HTTP round-trip)

**Recommended:** Inject a boolean signal from `ReadinessHealthCheck` or use a shared `TaskCompletionSource<bool>` that `ReadinessHealthCheck` resolves once healthy. This avoids the HTTP round-trip and is testable. The exact mechanism is a phase-level implementation decision.

---

### 4. Modified `K8sLeaseElection` (Existing Class)

**Location:** `src/SnmpCollector/Telemetry/K8sLeaseElection.cs`

**New constructor dependency:** `IPreferredStampReader` and `SiteAffinityOptions`

**New field:**

```csharp
private readonly bool _isPreferredPod;
```

Computed once in constructor:
```csharp
_isPreferredPod = string.IsNullOrEmpty(_siteAffinityOptions.PreferredNode)
    || string.Equals(
        Environment.GetEnvironmentVariable("NODE_NAME"),
        _siteAffinityOptions.PreferredNode,
        StringComparison.OrdinalIgnoreCase);
```

When `PreferredNode` is null/empty, `_isPreferredPod = true` for all pods — the mechanism is inactive and behavior is identical to the current implementation.

**Gate 1 — backoff before acquire:** The `LeaderElector` library's `RunAndTryToHoldLeadershipForeverAsync` does not expose a "pause before trying" hook. The correct integration point is a **wrapper loop** around the elector:

```
ExecuteAsync:
    while not cancelled:
        if not _isPreferredPod and _preferredStampReader.IsPreferredStampFresh:
            await Task.Delay(RetryPeriod, stoppingToken)  // back off, check again
            continue
        // else: run one elector cycle
        await RunElectorCycleAsync(stoppingToken)
```

`RunElectorCycleAsync` runs `elector.RunAndTryToHoldLeadershipForeverAsync` with a fresh `CancellationTokenSource` that is cancelled when: (a) `stoppingToken` fires, or (b) a yield condition is detected (Gate 2). After cancellation, the outer loop re-evaluates.

**Gate 2 — yield while leading:** Inside the elector's `OnStartedLeading` handler (or a monitoring loop that runs concurrently while leading), periodically check:

```
if not _isPreferredPod and _preferredStampReader.IsPreferredStampFresh:
    // yield: cancel the elector's inner CTS → triggers OnStoppedLeading
    // K8sLeaseElection.StopAsync already deletes the lease for near-instant failover
    _innerCts.Cancel()
```

**How to avoid fighting the library:** `LeaderElector.RunAndTryToHoldLeadershipForeverAsync` exits when its `CancellationToken` is cancelled. The approach is to hold a `CancellationTokenSource _innerCts` that is linked to `stoppingToken`. Cancelling `_innerCts` terminates the elector and returns control to the outer loop, which then re-evaluates. This is cooperative with the library — no patching, no reflection.

**Yield sequence:**

```
1. _innerCts.Cancel()                        → elector exits
2. OnStoppedLeading fires → _isLeader = false
3. Outer loop resumes
4. Calls K8sLeaseElection.StopAsync() pattern: deletes lease (near-instant failover)
5. Back-off begins (preferred pod can now acquire)
```

Note: The existing `StopAsync` (which deletes the lease) must remain callable from `GracefulShutdownService`. The yield path should replicate or reuse the lease deletion logic rather than calling `StopAsync` directly (which would also cancel the host).

---

## Modified Existing Components

### `ServiceCollectionExtensions` (in `AddSnmpConfiguration`)

The K8s registration block gains three additions:

1. Bind and validate `SiteAffinityOptions`
2. Register `PreferredHeartbeatService` as singleton + hosted service + `IPreferredStampReader`
3. Pass `IPreferredStampReader` to `K8sLeaseElection` (already resolved via singleton pattern)

Registration order:

```csharp
// Existing (unchanged)
services.AddSingleton<K8sLeaseElection>();
services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());

// New
services.AddSingleton<PreferredHeartbeatService>();
services.AddSingleton<IPreferredStampReader>(
    sp => sp.GetRequiredService<PreferredHeartbeatService>());
services.AddHostedService(sp => sp.GetRequiredService<PreferredHeartbeatService>());
```

`K8sLeaseElection` already receives `IPreferredStampReader` via constructor injection once it is added to its parameter list — DI resolves it automatically.

**Startup sequencing concern:** `K8sLeaseElection` and `PreferredHeartbeatService` are both `BackgroundService` instances registered as hosted services. The host starts them concurrently. `K8sLeaseElection` reads `IPreferredStampReader.IsPreferredStampFresh`, which starts as `false` (volatile field, default value). This means:

- Non-preferred pods: `IsPreferredStampFresh = false` at startup → they will attempt to acquire leadership. This is correct — if the preferred pod is not yet stamping, the election should proceed normally.
- Preferred pod: starts stamping after readiness gate → once stamp is fresh, non-preferred pods back off.

No additional startup ordering is needed; the default-`false` state is the correct behavior.

### `GracefulShutdownService`

No change required. The existing shutdown sequence (Step 1: `K8sLeaseElection.StopAsync`) deletes the leadership lease. The heartbeat lease managed by `PreferredHeartbeatService` will expire naturally (within `HeartbeatDurationSeconds`) after the pod stops. If instant heartbeat cleanup is desired, `GracefulShutdownService` can call `PreferredHeartbeatService.StopAsync` in a new shutdown step before the lease release step, but this is optional.

### `AlwaysLeaderElection`

No change. Local dev path is unaffected; `SiteAffinityOptions` is only bound in the K8s registration block.

---

## Data Flow: Preferred Stamp

```
NODE_NAME == PreferredNode?
    │
    ├─ YES (preferred pod)
    │       PreferredHeartbeatService
    │           waits for readiness gate
    │           ─────────────────────────────────────────────────
    │           loop every HeartbeatRenewIntervalSeconds:
    │               patch/hold "snmp-collector-preferred" Lease
    │               (renewTime = now, leaseDuration = HeartbeatDurationSeconds)
    │           ─────────────────────────────────────────────────
    │           IPreferredStampReader.IsPreferredStampFresh = true (for self)
    │           (not used by preferred pod — K8sLeaseElection ignores stamp when preferred)
    │
    └─ NO (non-preferred pod)
            PreferredHeartbeatService
                ─────────────────────────────────────────────────
                loop every HeartbeatRenewIntervalSeconds:
                    read "snmp-collector-preferred" Lease
                    if exists and (now - renewTime) < HeartbeatDurationSeconds:
                        _isStampFresh = true
                    else:
                        _isStampFresh = false
                ─────────────────────────────────────────────────
                IPreferredStampReader.IsPreferredStampFresh = _isStampFresh

                K8sLeaseElection reads IsPreferredStampFresh:
                    Gate 1: if true → do not attempt leadership acquire
                    Gate 2: if true while leading → yield (cancel inner elector)
```

---

## Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| `SiteAffinityOptions` | Config: preferred node name, lease names, durations | Injected into `PreferredHeartbeatService`, `K8sLeaseElection` |
| `PreferredHeartbeatService` | Write heartbeat (preferred pod) OR read stamp freshness (non-preferred) | Kubernetes Lease API, `IHostApplicationLifetime` |
| `IPreferredStampReader` | Expose `IsPreferredStampFresh` to K8sLeaseElection | Implemented by `PreferredHeartbeatService` |
| `K8sLeaseElection` (modified) | Leadership election with backoff/yield gates | `IPreferredStampReader`, Kubernetes Lease API, `ILeaderElection` consumers |
| `ILeaderElection` | Expose `IsLeader` to downstream consumers | `MetricRoleGatedExporter`, `CommandWorkerService` (unchanged) |

---

## Architectural Patterns

### Pattern 1: Inner CancellationTokenSource for cooperative yield

**What:** `K8sLeaseElection` holds a `CancellationTokenSource _innerCts` that is linked to `stoppingToken`. The elector runs with `_innerCts.Token`. When the yield condition is met, `_innerCts.Cancel()` terminates the elector cooperatively. The outer loop then re-evaluates the gate.

**When to use:** Any time a library's async loop needs to be interrupted from the outside without fighting the library's internal state machine.

**Trade-offs:** The elector's `OnStoppedLeading` fires on cancellation, so `_isLeader` is set to `false` correctly. The outer loop must recreate `_innerCts` and a fresh `LeaderElector` on each iteration (the `LeaderElector` is not reusable after cancellation). This means a small allocation per election cycle restart — negligible in practice given the timescales involved.

### Pattern 2: Interface-separated stamp reader

**What:** `IPreferredStampReader` is a narrow interface exposing only `IsPreferredStampFresh`. `PreferredHeartbeatService` implements it. `K8sLeaseElection` depends on the interface, not the concrete class.

**When to use:** When two services have a one-directional dependency and the consuming service needs to be unit-tested. Prevents `K8sLeaseElection` tests from requiring a real Kubernetes client or a running `PreferredHeartbeatService`.

**Trade-offs:** One extra interface file. Negligible.

### Pattern 3: Preference-inactive no-op via null check

**What:** When `SiteAffinityOptions.PreferredNode` is null or empty, `_isPreferredPod = true` for all pods and the stamp reader is never consulted in any gate. The two-lease mechanism is completely inactive. Existing behavior is preserved.

**When to use:** Whenever a new mechanism must be strictly additive and not break existing single-pod or un-configured deployments.

**Trade-offs:** Slightly more conditional logic in `K8sLeaseElection.ExecuteAsync`. Encapsulate the check in a private method (`ShouldBackOff()`) to keep the main loop readable.

---

## Anti-Patterns

### Anti-Pattern 1: Running two LeaderElectors for the heartbeat lease

**What people do:** Reuse `LeaderElector` for both the leadership lease and the heartbeat lease on the preferred pod.

**Why it's wrong:** `LeaderElector.RunAndTryToHoldLeadershipForeverAsync` is designed for competitive election. The heartbeat lease has no competition — only one pod ever writes it. Using `LeaderElector` for it adds unnecessary retry logic, event callbacks, and timing constraints. A simple renewal loop (patch the Lease directly every N seconds) is correct and much simpler.

**Do this instead:** `PreferredHeartbeatService` uses direct `IKubernetes.CoordinationV1.PatchNamespacedLeaseAsync` or `ReplaceNamespacedLeaseAsync` on a timer. No `LeaderElector` involved.

### Anti-Pattern 2: Reading the heartbeat lease from K8sLeaseElection directly

**What people do:** Inject `IKubernetes` into `K8sLeaseElection` and call `ReadNamespacedLeaseAsync` inside the gate check.

**Why it's wrong:** The gate is checked frequently (every `RetryPeriod`). Making a Kubernetes API call on every check is chatty, adds latency to the election loop, and couples the election service to API latency. The poll loop in `PreferredHeartbeatService` amortizes the API calls and gives `K8sLeaseElection` a local, in-memory boolean to read.

**Do this instead:** `IPreferredStampReader.IsPreferredStampFresh` is a volatile bool read. Zero network calls in the gate.

### Anti-Pattern 3: Modifying ILeaderElection interface

**What people do:** Add `IsPreferred` or `PreferredNodeName` to `ILeaderElection` to expose site-affinity status to consumers.

**Why it's wrong:** `MetricRoleGatedExporter` and `CommandWorkerService` need exactly one bit: `IsLeader`. Site-affinity is an internal election concern. Widening the interface couples consumers to an implementation detail they do not need and breaks `AlwaysLeaderElection` (which would need stub implementations).

**Do this instead:** Keep `ILeaderElection` unchanged. Site-affinity state is internal to `K8sLeaseElection` and `PreferredHeartbeatService`.

### Anti-Pattern 4: Preferred pod starts stamping before readiness

**What people do:** Begin the heartbeat renewal loop immediately in `ExecuteAsync` without waiting for readiness.

**Why it's wrong:** A pod still loading ConfigMaps or resolving device DNS records will pass the site-affinity check and cause the current non-preferred leader to yield — handing leadership to a pod that cannot yet poll devices. This is a split-brain degradation scenario.

**Do this instead:** `PreferredHeartbeatService` waits for a readiness signal before beginning to stamp. The non-preferred leader sees a stale/absent stamp and retains leadership until the preferred pod is genuinely ready.

---

## Integration Points Summary

### Files modified

| File | Change | Risk |
|------|--------|------|
| `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` | Add `IPreferredStampReader` + `SiteAffinityOptions` constructor params; add outer loop with Gate 1 and Gate 2; add `_innerCts` management | Medium — core election logic |
| `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` | Bind `SiteAffinityOptions`; register `PreferredHeartbeatService` as singleton + hosted service + `IPreferredStampReader` | Low — additive registration |

### Files created

| File | Description |
|------|-------------|
| `src/SnmpCollector/Configuration/SiteAffinityOptions.cs` | New options: `PreferredNode`, `HeartbeatLeaseName`, `HeartbeatDurationSeconds`, `HeartbeatRenewIntervalSeconds` |
| `src/SnmpCollector/Telemetry/IPreferredStampReader.cs` | Narrow interface: `IsPreferredStampFresh (bool)` |
| `src/SnmpCollector/Telemetry/PreferredHeartbeatService.cs` | `BackgroundService` + `IPreferredStampReader`: writer path (preferred pod), reader/poll path (non-preferred pod) |
| `src/SnmpCollector/Configuration/Validators/SiteAffinityOptionsValidator.cs` | Cross-field validation: `HeartbeatRenewIntervalSeconds < HeartbeatDurationSeconds` |

### Files not modified

| File | Reason unchanged |
|------|-----------------|
| `ILeaderElection.cs` | Interface contract preserved; consumers are not affected |
| `AlwaysLeaderElection.cs` | Local dev path; site-affinity is K8s-only |
| `MetricRoleGatedExporter.cs` | Reads `ILeaderElection.IsLeader` — unchanged |
| `CommandWorkerService.cs` | Reads `ILeaderElection.IsLeader` — unchanged |
| `GracefulShutdownService.cs` | Leadership lease deletion path unchanged; heartbeat lease expiry is tolerable |

---

## Build Order

Dependencies are listed explicitly. Each step is independently committable and testable.

**Step 1: `SiteAffinityOptions` + validator**

New config class and validator. No behavioral change. Verify bounds validation (`RenewInterval < Duration`) in unit test.

**Step 2: `IPreferredStampReader`**

New interface. No implementation yet. Confirms the dependency direction before writing code.

**Step 3: `PreferredHeartbeatService`**

New `BackgroundService` implementing `IPreferredStampReader`. Write the reader path (non-preferred poll loop) first — it is pure read, no write concern. Stub the readiness gate (always-pass initially). Write the writer path (preferred renewal loop). Add readiness gate last.

**Step 4: `ServiceCollectionExtensions`**

Bind `SiteAffinityOptions`. Register `PreferredHeartbeatService` (singleton + hosted + `IPreferredStampReader`). Verify the app starts without error. At this point, the heartbeat lease writer/reader runs but `K8sLeaseElection` does not yet consult it.

**Step 5: `K8sLeaseElection` (gates)**

Add `IPreferredStampReader` and `SiteAffinityOptions` as constructor parameters. Implement outer loop with `_innerCts`. Add Gate 1 (backoff before acquire). Add Gate 2 (yield while leading). This is the highest-risk step — the existing election loop is modified.

**Step 6: Integration test**

Deploy two pods to a test cluster with `PreferredNode` set. Verify:
- Preferred pod acquires leadership after readiness
- Non-preferred pod backs off once preferred stamp is fresh
- Non-preferred pod becomes leader when preferred pod is drained/restarted (stamp expires within `HeartbeatDurationSeconds`)
- Preferred pod reclaims leadership after restart and readiness

---

## Scaling Considerations

This mechanism is designed for exactly two pods (preferred + standby). It does not generalize to N pods competing with preference ordering. If more than two pods run:

- All non-preferred pods back off when the preferred stamp is fresh
- Among non-preferred pods, the existing `LeaderElector` race resolves normally when the preferred pod is absent
- No changes needed for this case — the gates apply uniformly to all non-preferred pods

---

## Sources

All claims derive from direct inspection of:
- `src/SnmpCollector/Telemetry/K8sLeaseElection.cs` — `LeaderElector` usage, `_isLeader` volatile flag, `StopAsync` lease deletion, `RunAndTryToHoldLeadershipForeverAsync` call site
- `src/SnmpCollector/Telemetry/ILeaderElection.cs` — interface surface, two-member contract
- `src/SnmpCollector/Telemetry/AlwaysLeaderElection.cs` — local dev stub pattern
- `src/SnmpCollector/Configuration/LeaseOptions.cs` — existing lease config shape, `SectionName` pattern
- `src/SnmpCollector/Configuration/PodIdentityOptions.cs` — `HOSTNAME` env var usage pattern
- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — `ILeaderElection.IsLeader` read pattern (line 46)
- `src/SnmpCollector/Services/CommandWorkerService.cs` — `ILeaderElection.IsLeader` gate pattern (line 136)
- `src/SnmpCollector/Lifecycle/GracefulShutdownService.cs` — `K8sLeaseElection` concrete-type resolution, shutdown step ordering
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — K8s registration block (lines 224–258), singleton-then-interface pattern, `IsInCluster()` guard

Confidence: HIGH for all integration points — derived from code, not inference.

---
*Architecture research for: preferred leader election with site-affinity — SnmpCollector two-lease mechanism*
*Researched: 2026-03-25*
