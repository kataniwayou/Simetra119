# Phase 7: Leader Election and Role-Gated Export - Research

**Researched:** 2026-03-05
**Domain:** Kubernetes Lease API leader election + OpenTelemetry metric exporter gating in .NET 9 Generic Host
**Confidence:** HIGH (reference implementation verified in Simetra codebase; OTel and KubernetesClient internals verified via official sources)

---

## Summary

Phase 7 introduces leader-gated metric export for the multi-instance Kubernetes deployment: exactly one pod exports business metrics (snmp_gauge, snmp_counter, snmp_info) while all pods export pipeline and System.Runtime metrics. The reference Simetra project contains a working, directly portable implementation of all three major components: `K8sLeaseElection` (BackgroundService + ILeaderElection), `AlwaysLeaderElection` (local dev fallback), and `MetricRoleGatedExporter` (meter-filtering wrapper). These are source-ready for adaptation into SnmpCollector with namespace/class renames and minor structural changes.

The critical architectural problem is meter discrimination: SnmpCollector Phase 1 registered a single `TelemetryConstants.MeterName = "SnmpCollector"` for everything. Phase 7 must split this into two meters — one for business metrics (leader-gated) and one that pipeline/runtime metrics remain on (always exported). This requires updating `TelemetryConstants`, `SnmpMetricFactory`, and the `AddSnmpTelemetry` wiring to replace the direct `AddOtlpExporter` with a `MetricRoleGatedExporter` wrapping a manually constructed `OtlpMetricExporter`.

The one confirmed blocker from STATE.md: `MetricRoleGatedExporter` uses reflection to set the `internal set` `ParentProvider` on the inner `OtlpMetricExporter`. This is verified as the only option — the OTel SDK sets `ParentProvider` only on the direct exporter passed to `PeriodicExportingMetricReader` (via the internal-only `SetParentProvider` infrastructure). The Simetra implementation handles this correctly with a lazy one-time propagation pattern on first Export call.

**Primary recommendation:** Port the Simetra leader election and MetricRoleGatedExporter directly. Add a second MeterName constant for business metrics. Replace `AddOtlpExporter()` with `AddReader(sp => new PeriodicExportingMetricReader(new MetricRoleGatedExporter(...)))`. Add a reflection-breakage detection test that asserts `ParentProvider` is set after the first export.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| KubernetesClient | 18.0.13 (project current) | K8s API access + LeaderElector | Official C# K8s client; only library with `LeaderElector` + `LeaseLock` API matching reference implementation |
| OpenTelemetry | 1.15.0 (project current) | BaseExporter<Metric>, PeriodicExportingMetricReader | Already in project; wrapping pattern works with this version |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.0 (project current) | OtlpMetricExporter (manually constructed for wrapping) | Already in project |
| Microsoft.Extensions.Hosting | 9.0.0 (project current) | BackgroundService, IHostedService, IHostApplicationLifetime | Already in project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| KubernetesClient | 19.0.2 (latest as of 2026-03-05) | Same; newer Kubernetes API compatibility | If upgrading; no breaking changes to LeaderElection API confirmed |
| OpenTelemetry.Instrumentation.Runtime | 1.15.0 (project current) | System.Runtime metrics (always exported by all instances) | Already in project; HA-06 compliance |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| K8s Lease API via KubernetesClient | etcd-based locking, Zookeeper | Far more infrastructure complexity; K8s Lease is native to K8s, no extra dependencies |
| MetricRoleGatedExporter (wrapper pattern) | Two separate MeterProviders | Two MeterProviders with `AddOpenTelemetry().WithMetrics()` are not officially supported in one DI registration via Extensions.Hosting; wrapper is correct approach |
| Reflection to set inner ParentProvider | InternalsVisibleTo hack | Cannot add InternalsVisibleTo from OpenTelemetry assembly; reflection is the only viable option |
| Dynamic role in SnmpLogEnrichmentProcessor via Func<string> | Continue static string | Phase 1 decision [01-03] used static string; Phase 7 must pass `() => leaderElection.CurrentRole` delegate to make role dynamic in logs |

**Installation (new NuGet package required):**
```bash
dotnet add src/SnmpCollector/SnmpCollector.csproj package KubernetesClient --version 18.0.13
```

---

## Architecture Patterns

### Recommended Project Structure
```
src/SnmpCollector/
├── Telemetry/
│   ├── TelemetryConstants.cs          # Add LeaderMeterName constant (Phase 7)
│   ├── ILeaderElection.cs             # New: interface (IsLeader, CurrentRole)
│   ├── AlwaysLeaderElection.cs        # New: local dev fallback (IsLeader=true)
│   ├── K8sLeaseElection.cs            # New: BackgroundService + ILeaderElection
│   ├── MetricRoleGatedExporter.cs     # New: BaseExporter<Metric> wrapper
│   ├── SnmpMetricFactory.cs           # Update: use LeaderMeterName not MeterName
│   ├── SnmpLogEnrichmentProcessor.cs  # Update: take Func<string> role not string
│   ├── SnmpConsoleFormatter.cs        # Update: resolve role from ILeaderElection dynamically
│   ├── PipelineMetricService.cs       # No change (pipeline meter stays on MeterName)
│   └── ...
├── Configuration/
│   ├── LeaseOptions.cs                # New: Lease:Name, Lease:Namespace, timing
│   ├── SiteOptions.cs                 # Add PodIdentity property (lease identity)
│   └── Validators/
│       └── LeaseOptionsValidator.cs   # New: validate Name/Namespace not empty
└── Extensions/
    └── ServiceCollectionExtensions.cs # Update AddSnmpTelemetry: leader election + reader
```

### Pattern 1: Two-Meter Design for Selective Gating

**What:** Split `TelemetryConstants.MeterName` into two constants. Business instruments (snmp_gauge, snmp_counter, snmp_info) use `LeaderMeterName`. Pipeline and runtime instruments use `MeterName` (unchanged). `MetricRoleGatedExporter` gates only `LeaderMeterName`.

**When to use:** Required. This is the discriminator for the gating logic.

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Telemetry/TelemetryConstants.cs (adapted)
public static class TelemetryConstants
{
    /// <summary>
    /// Pipeline metrics meter — exported by ALL instances (pipeline + runtime health).
    /// Used by PipelineMetricService for snmp.event.*, snmp.poll.*, snmp.trap.* counters.
    /// </summary>
    public const string MeterName = "SnmpCollector";

    /// <summary>
    /// Business metrics meter — exported ONLY by the leader instance.
    /// Used by SnmpMetricFactory for snmp_gauge, snmp_counter, snmp_info instruments.
    /// </summary>
    public const string LeaderMeterName = "SnmpCollector.Leader";
}
```

### Pattern 2: ILeaderElection Interface and Implementations

**What:** A single interface with `IsLeader` (bool) and `CurrentRole` (string). Two implementations: `AlwaysLeaderElection` (static true) and `K8sLeaseElection` (BackgroundService using Kubernetes Lease API).

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Telemetry/ILeaderElection.cs (direct port)
public interface ILeaderElection
{
    bool IsLeader { get; }
    string CurrentRole { get; }
}

// AlwaysLeaderElection: local dev (non-K8s)
public sealed class AlwaysLeaderElection : ILeaderElection
{
    public bool IsLeader => true;
    public string CurrentRole => "leader";
}
```

### Pattern 3: K8sLeaseElection as BackgroundService + ILeaderElection

**What:** Single class deriving from `BackgroundService` and implementing `ILeaderElection`. Volatile bool `_isLeader` written only by event handlers. `StopAsync` override deletes the lease explicitly for near-instant failover (HA-08).

**Key timing parameters (Claude's Discretion — see recommendations below):**
- `LeaseDuration`: 15s (standard K8s default)
- `RetryPeriod` (renewal interval): 10s
- `RenewDeadline`: 13s (must be < LeaseDuration; 2s before expiry)

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Telemetry/K8sLeaseElection.cs (direct port with namespace/type renames)
public sealed class K8sLeaseElection : BackgroundService, ILeaderElection
{
    private volatile bool _isLeader;

    public bool IsLeader => _isLeader;
    public string CurrentRole => _isLeader ? "leader" : "follower";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = _leaseOptions.PodIdentity
            ?? Environment.GetEnvironmentVariable("HOSTNAME")
            ?? Environment.MachineName;

        var leaseLock = new LeaseLock(
            _kubeClient,
            _leaseOptions.Namespace,
            _leaseOptions.Name,
            identity);

        var config = new LeaderElectionConfig(leaseLock)
        {
            LeaseDuration = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds),
            RetryPeriod   = TimeSpan.FromSeconds(_leaseOptions.RenewIntervalSeconds),
            RenewDeadline = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds - 2)
        };

        var elector = new LeaderElector(config);
        elector.OnStartedLeading += () => { _isLeader = true; _logger.LogInformation("Acquired lease {Lease}", _leaseOptions.Name); };
        elector.OnStoppedLeading += () => { _isLeader = false; _logger.LogInformation("Lost lease {Lease}", _leaseOptions.Name); };
        elector.OnNewLeader      += leader => _logger.LogInformation("New leader observed: {Leader}", leader);

        await elector.RunAndTryToHoldLeadershipForeverAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken); // Cancel ExecuteAsync first
        if (_isLeader)
        {
            try
            {
                await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
                    _leaseOptions.Name, _leaseOptions.Namespace,
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Lease {Lease} explicitly deleted for near-instant failover", _leaseOptions.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete lease {Lease} — followers acquire after TTL expiry", _leaseOptions.Name);
            }
        }
        _isLeader = false;
    }
}
```

### Pattern 4: MetricRoleGatedExporter — Reflection-Based ParentProvider Propagation

**What:** Wraps `OtlpMetricExporter` in a `BaseExporter<Metric>`. On each `Export` call, checks `IsLeader`. If follower, filters out `LeaderMeterName` metrics. If leader, passes everything through. On first `Export` call (lazy), uses reflection to set the inner exporter's `ParentProvider` so it includes `service.name` resource attributes in OTLP output.

**Critical verified fact:** `BaseExporter<Metric>.ParentProvider` has `internal set`. The OTel SDK sets `ParentProvider` only on the *direct* exporter given to `PeriodicExportingMetricReader.SetParentProvider()` — the outer wrapper gets it, but the inner `OtlpMetricExporter` does not. Reflection is the only mechanism to propagate it to the inner exporter. The Simetra implementation handles this correctly with a lazy one-time flag.

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Telemetry/MetricRoleGatedExporter.cs (direct port)
public sealed class MetricRoleGatedExporter : BaseExporter<Metric>
{
    private static readonly PropertyInfo ParentProviderProperty =
        typeof(BaseExporter<Metric>).GetProperty("ParentProvider")!;

    private readonly BaseExporter<Metric> _inner;
    private readonly ILeaderElection _leaderElection;
    private readonly string _gatedMeterName;
    private bool _parentProviderPropagated;

    public override ExportResult Export(in Batch<Metric> batch)
    {
        // Lazy one-time propagation: SDK sets our ParentProvider during MeterProvider construction;
        // we propagate it to the inner exporter on first Export call so OTLP includes resource attributes.
        if (!_parentProviderPropagated && ParentProvider != null)
        {
            ParentProviderProperty.SetValue(_inner, ParentProvider);
            _parentProviderPropagated = true;
        }

        if (_leaderElection.IsLeader)
        {
            return _inner.Export(batch);
        }

        // Follower: pass through everything except the gated business meter
        var ungated = new List<Metric>();
        foreach (var metric in batch)
        {
            if (!string.Equals(metric.MeterName, _gatedMeterName, StringComparison.Ordinal))
                ungated.Add(metric);
        }

        if (ungated.Count == 0)
            return ExportResult.Success;  // Return Success not Failure -- no retry needed

        var filteredBatch = new Batch<Metric>(ungated.ToArray(), ungated.Count);
        return _inner.Export(filteredBatch);
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)  => _inner.ForceFlush(timeoutMilliseconds);
    protected override bool OnShutdown(int timeoutMilliseconds)    => _inner.Shutdown(timeoutMilliseconds);
    protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}
```

### Pattern 5: DI Singleton Registration — Single Instance for Both Interfaces

**What:** Register `K8sLeaseElection` as a concrete singleton FIRST. Then resolve the same instance for `ILeaderElection` and `IHostedService`. This guarantees SC#5: both interfaces resolve to the same object.

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Extensions/ServiceCollectionExtensions.cs (direct port)
// In AddSnmpTelemetry(), after KubernetesClientConfiguration.IsInCluster() check:
if (KubernetesClientConfiguration.IsInCluster())
{
    var kubeConfig = KubernetesClientConfiguration.InClusterConfig();
    builder.Services.AddSingleton<IKubernetes>(new Kubernetes(kubeConfig));

    // CRITICAL: Register concrete type FIRST, then resolve same instance for both interfaces.
    // If you use AddSingleton<ILeaderElection, K8sLeaseElection>() and AddHostedService<K8sLeaseElection>()
    // separately, the DI container creates TWO instances -- the hosted service sets _isLeader on one,
    // but ILeaderElection consumers read from the other. The hosted service never updates the reader's state.
    builder.Services.AddSingleton<K8sLeaseElection>();
    builder.Services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());
}
else
{
    builder.Services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
}
```

### Pattern 6: AddReader with MetricRoleGatedExporter (Replacing AddOtlpExporter)

**What:** Phase 1 used `metrics.AddOtlpExporter()`. Phase 7 replaces this with `metrics.AddReader(sp => ...)` that manually constructs `OtlpMetricExporter` and wraps it in `MetricRoleGatedExporter`. Cannot use `AddOtlpExporter()` because it creates the exporter internally, preventing wrapping.

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Extensions/ServiceCollectionExtensions.cs
.WithMetrics(metrics =>
{
    metrics.AddMeter(TelemetryConstants.MeterName);        // Pipeline metrics (always exported)
    metrics.AddMeter(TelemetryConstants.LeaderMeterName);  // Business metrics (leader-gated)
    metrics.AddRuntimeInstrumentation();                   // System.Runtime (always exported)

    // Manual construction required — AddOtlpExporter() prevents wrapping.
    metrics.AddReader(sp =>
    {
        var leaderElection = sp.GetRequiredService<ILeaderElection>();
        var otlpExporter = new OtlpMetricExporter(new OtlpExporterOptions
        {
            Endpoint = new Uri(otlpOptions.Endpoint)
        });
        var roleGated = new MetricRoleGatedExporter(
            otlpExporter, leaderElection, TelemetryConstants.LeaderMeterName);
        return new PeriodicExportingMetricReader(roleGated);
    });
})
```

### Pattern 7: Dynamic Role in SnmpLogEnrichmentProcessor

**What:** Phase 1 decision [01-03] used a static `string role` parameter. Phase 7 changes the constructor to accept `Func<string> roleProvider` so the role updates dynamically as leadership changes.

**Example:**
```csharp
// Updated SnmpLogEnrichmentProcessor constructor signature
public SnmpLogEnrichmentProcessor(
    ICorrelationService correlationService,
    string siteName,
    Func<string> roleProvider)  // Changed from string role

// In OnEnd:
attributes.Add(new KeyValuePair<string, object?>("role", _roleProvider()));

// In AddSnmpTelemetry DI wiring:
logging.AddProcessor(sp =>
{
    var siteOptions = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
    var correlationService = sp.GetRequiredService<ICorrelationService>();
    var leaderElection = sp.GetRequiredService<ILeaderElection>();
    return new SnmpLogEnrichmentProcessor(
        correlationService,
        siteOptions.Name,
        () => leaderElection.CurrentRole);  // Dynamic role via closure
});
```

### Pattern 8: SnmpConsoleFormatter Dynamic Role

**What:** `SnmpConsoleFormatter.Write` currently reads `_siteOptions?.Value.Role` (the static "standalone" default from Phase 1). Phase 7 must change this to read from `ILeaderElection.CurrentRole` resolved from DI.

**Example:**
```csharp
// In SnmpConsoleFormatter.EnsureServicesResolved():
_leaderElection = sp.GetService<ILeaderElection>();

// In Write():
var role = _leaderElection?.CurrentRole ?? _siteOptions?.Value.Role ?? "unknown";
```

### Pattern 9: LeaseOptions Config Class

**Recommended timing parameters (Claude's Discretion — rationale below):**
- `DurationSeconds = 15`: Standard K8s client-go default; long enough for renewal jitter, short enough for fast failover
- `RenewIntervalSeconds = 10`: Leader renews every 10s; must be less than DurationSeconds
- `RenewDeadlineSeconds = 13`: If leader cannot renew within 13s of last renewal, it releases the lease; computed as DurationSeconds - 2

**Example:**
```csharp
// Source: Simetra reference — src/Simetra/Configuration/LeaseOptions.cs (adapted for SnmpCollector)
public sealed class LeaseOptions
{
    public const string SectionName = "Lease";

    [Required] public required string Name { get; set; } = "snmp-collector-leader";
    [Required] public required string Namespace { get; set; } = "default";

    // Pod identity used as lease holder identity.
    // PodIdentity in SiteOptions or HOSTNAME env var serves as unique pod discriminator.
    // Not stored here; derived from SiteOptions.PodIdentity or HOSTNAME at runtime.

    [Range(1, 300)] public int RenewIntervalSeconds { get; set; } = 10;
    [Range(1, 600)] public int DurationSeconds { get; set; } = 15;
}
```

**appsettings.json additions:**
```json
{
  "Lease": {
    "Name": "snmp-collector-leader",
    "Namespace": "default",
    "DurationSeconds": 15,
    "RenewIntervalSeconds": 10
  }
}
```

**SiteOptions PodIdentity addition:**
```csharp
// Add to existing SiteOptions:
/// <summary>Pod name used as the Kubernetes lease holder identity. Defaults to HOSTNAME env var.</summary>
public string? PodIdentity { get; set; }
```

And in `AddSnmpConfiguration`, add a PostConfigure:
```csharp
services.PostConfigure<SiteOptions>(options =>
{
    options.PodIdentity ??= Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
});
```

### Anti-Patterns to Avoid

- **Two DI registrations without the concrete-first pattern:** Using `AddSingleton<ILeaderElection, K8sLeaseElection>()` and `AddHostedService<K8sLeaseElection>()` creates two separate instances. The hosted service updates `_isLeader` on its own instance; `ILeaderElection` consumers read from a different instance that never becomes leader. Leadership state never propagates.
- **Using `AddOtlpExporter()` and then trying to wrap it:** The SDK creates the exporter internally; you cannot access it to wrap it. Must manually construct `OtlpMetricExporter`.
- **Returning `ExportResult.Failure` for filtered-out followers:** This triggers SDK retry backoff. Return `ExportResult.Success` when silently dropping — the data was intentionally not exported, not a network failure.
- **Setting `_gatedMeterName` to `TelemetryConstants.MeterName`:** This gates pipeline metrics too. Only gate `LeaderMeterName`.
- **Forgetting to add `LeaderMeterName` to `AddMeter`:** Without `metrics.AddMeter(TelemetryConstants.LeaderMeterName)`, the meter is not listened to at all — instruments from `SnmpMetricFactory` silently produce nothing.

---

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Kubernetes distributed lock | Custom ConfigMap CAS loop, custom etcd client | `KubernetesClient.LeaderElection.LeaderElector` + `LeaseLock` | Handles acquire/renew/release retry, TTL, event callbacks — battle-tested |
| K8s API client config | Manual HTTP calls to k8s API | `KubernetesClientConfiguration.InClusterConfig()` + `Kubernetes(config)` | Handles service account token, CA cert, base URL automatically |
| Lease acquire/renew loop | Custom timer-based polling with Kubernetes API | `LeaderElector.RunAndTryToHoldLeadershipForeverAsync(ct)` | Handles retry on leadership loss, jitter, cancellation token |
| Leader detection inside pod | Parse pod annotations, env vars, consensus protocol | `KubernetesClientConfiguration.IsInCluster()` + lease holder identity check | Three-line check: env vars + service account files |

**Key insight:** The KubernetesClient `LeaderElection` namespace gives you a production-grade elector in ~30 lines of `BackgroundService`. The complexity is in getting the DI wiring right (single-instance pattern), not in the election logic itself.

---

## Common Pitfalls

### Pitfall 1: Two DI Instances of K8sLeaseElection (Most Critical)

**What goes wrong:** The hosted service's `_isLeader` volatile field is updated by event handlers on one object instance; `ILeaderElection` consumers read `IsLeader` from a different object instance that is never the leader. Business metrics are never exported by any pod (always follower from the consumer's perspective).

**Why it happens:** The naive registration pattern `AddSingleton<ILeaderElection, K8sLeaseElection>()` + `AddHostedService<K8sLeaseElection>()` creates two separate singleton lifetimes — one for the interface, one for the hosted service. DI resolves them as separate objects.

**How to avoid:** Register concrete type FIRST, then resolve same instance for both:
```csharp
builder.Services.AddSingleton<K8sLeaseElection>();
builder.Services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());
```

**Verification:** `host.Services.GetRequiredService<ILeaderElection>() == host.Services.GetRequiredService<K8sLeaseElection>()` must return `true`. SC#5 verifies this.

**Warning signs:** Both pods report `IsLeader=false`; no business metrics appear in Prometheus from either pod.

---

### Pitfall 2: ParentProvider Reflection Breakage (Blocker from STATE.md)

**What goes wrong:** A future OTel SDK upgrade makes `ParentProvider` truly inaccessible via reflection (renamed, removed, or moved to a different class). The `MetricRoleGatedExporter` stops propagating the resource to the inner OTLP exporter. OTLP exports succeed but lack `service.name`/`service.instance.id` resource attributes. Prometheus time series lose pod identity.

**Why it happens:** Reflection against `internal set` properties is an undocumented, unsupported API contract. It was stable through 1.15.0 (verified: `public BaseProvider? ParentProvider { get; internal set; }` in `BaseExporter.cs`).

**How to avoid:** Add a reflection-breakage detection test as part of Phase 7:
```csharp
[Fact]
public void ParentProviderProperty_IsAccessibleViaReflection()
{
    var prop = typeof(BaseExporter<Metric>).GetProperty("ParentProvider");
    Assert.NotNull(prop);            // Property exists
    Assert.True(prop.CanRead);       // Getter is accessible
    Assert.True(prop.CanWrite);      // Setter is accessible via reflection
    // Note: prop.SetMethod.IsPublic == false (internal), but CanWrite == true via reflection
}
```
This test fails if OTel removes or restructures the property, providing early warning before runtime failure.

**Warning signs:** OTLP metrics arrive at collector but lack `service_name` label; Prometheus shows metrics without pod identity labels.

---

### Pitfall 3: Forgetting to Update SnmpMetricFactory Meter

**What goes wrong:** `SnmpMetricFactory` creates instruments on `TelemetryConstants.MeterName` ("SnmpCollector"). If not changed to `TelemetryConstants.LeaderMeterName` ("SnmpCollector.Leader"), the business metrics are registered under the pipeline meter — no gating occurs, both pods export all business metrics.

**Why it happens:** `SnmpMetricFactory` was built in Phase 1 before the two-meter design was established. The constant name is the same in source; the bug is silent.

**How to avoid:** In Phase 7, update `SnmpMetricFactory` constructor to use `LeaderMeterName`:
```csharp
_meter = meterFactory.Create(TelemetryConstants.LeaderMeterName); // Changed from MeterName
```

**Warning signs:** Two sets of snmp_gauge/snmp_counter/snmp_info series appear in Prometheus — SC#1 fails.

---

### Pitfall 4: Network Partition Handling — Recommendation: Stop on Renewal Failure

**Decision (Claude's Discretion):** When the leader cannot renew its lease (network partition), it should stop exporting business metrics after the `RenewDeadline` passes. This is the safer option for metric deduplication.

**Rationale:** If the leader keeps exporting during a partition, a new leader acquires the lease after `LeaseDuration` expires and also exports — both pods export simultaneously for up to `LeaseDuration - RenewDeadline` seconds (2s in the recommended config). The `LeaderElector` already handles this correctly: `OnStoppedLeading` fires when `RenewDeadline` is exceeded, which sets `_isLeader = false`. No extra code needed — the election library gives us the correct semantics automatically.

**Warning signs:** Metric gaps (preferred over duplicates) appear during partition recovery. This is the correct behavior.

---

### Pitfall 5: Metric Gaps vs Overlap During Leader Transition

**Decision (Claude's Discretion):** Accept brief metric gaps (not overlaps) during leader transition.

**Rationale:** Prometheus deduplication by time-series labels means overlapping series from two pods produce two distinct label sets (different `service_instance_id`), which IS a duplicate. A brief gap (new leader starts exporting on next periodic export interval, ~60s default or configured interval) is preferable to permanent duplicate series. The new leader should export immediately without warm-up: OTel instruments retain their accumulated cumulative state in memory, so the first export from the new leader produces a correct cumulative point.

**Implication:** No "warm-up wait" needed in K8sLeaseElection. `OnStartedLeading` sets `_isLeader = true` immediately; the next periodic export cycle picks it up.

---

### Pitfall 6: LeaderMeterName Not Added to AddMeter

**What goes wrong:** `metrics.AddMeter(TelemetryConstants.MeterName)` is present but `metrics.AddMeter(TelemetryConstants.LeaderMeterName)` is missing. The `LeaderMeterName` meter is not subscribed to by the MeterProvider — instruments created by `SnmpMetricFactory` silently produce nothing. No business metrics appear in Prometheus from any pod.

**Why it happens:** Easy omission when adding the second meter constant.

**How to avoid:** Both `AddMeter` calls are required in `AddSnmpTelemetry`.

**Warning signs:** No `snmp_gauge`, `snmp_counter`, or `snmp_info` series appear in Prometheus at all (even for the leader pod).

---

### Pitfall 7: Static Role in SnmpConsoleFormatter After Phase 7

**What goes wrong:** `SnmpConsoleFormatter.Write` reads `_siteOptions?.Value.Role` which is the static "standalone" from Phase 1. After Phase 7, the role changes dynamically (leader/follower). Console log lines continue to show "standalone" regardless of actual leader status.

**Why it happens:** Phase 1 [01-03] decision used static role; Phase 7 [01-03 follow-up] must make it dynamic. The formatter resolves services lazily — must add `ILeaderElection` to its lazy resolution list.

**How to avoid:** In `SnmpConsoleFormatter.EnsureServicesResolved()`, resolve `ILeaderElection` and use `_leaderElection?.CurrentRole ?? _siteOptions?.Value.Role` in `Write`.

---

### Pitfall 8: IsInCluster() Returns False in K8s Without Correct Service Account

**What goes wrong:** `KubernetesClientConfiguration.IsInCluster()` returns false even inside a K8s pod if: (a) the pod's service account is not mounted, (b) the namespace token file is missing at `/var/run/secrets/kubernetes.io/serviceaccount/token`, or (c) `KUBERNETES_SERVICE_HOST`/`KUBERNETES_SERVICE_PORT` env vars are not set (they are auto-injected by K8s but can be missing in some custom setups).

**How to avoid:** Ensure the K8s deployment YAML mounts the service account token (default in K8s) and that `RBAC` grants `get/update/create/delete` on `leases` resource in `coordination.k8s.io` API group. The `AlwaysLeaderElection` fallback means failing to detect in-cluster degrades gracefully for local dev.

**RBAC required:**
```yaml
rules:
- apiGroups: ["coordination.k8s.io"]
  resources: ["leases"]
  verbs: ["get", "create", "update", "delete"]
```

---

## Code Examples

### LeaseOptions Configuration Class
```csharp
// NEW FILE: src/SnmpCollector/Configuration/LeaseOptions.cs
using System.ComponentModel.DataAnnotations;

namespace SnmpCollector.Configuration;

public sealed class LeaseOptions
{
    public const string SectionName = "Lease";

    [Required] public required string Name { get; set; } = "snmp-collector-leader";
    [Required] public required string Namespace { get; set; } = "default";

    [Range(1, 300)] public int RenewIntervalSeconds { get; set; } = 10;
    [Range(1, 600)] public int DurationSeconds { get; set; } = 15;
}
```

### Lease Options Validator
```csharp
// NEW FILE: src/SnmpCollector/Configuration/Validators/LeaseOptionsValidator.cs
// Validates Name and Namespace not empty/whitespace (DataAnnotations [Required] only checks null)
public sealed class LeaseOptionsValidator : IValidateOptions<LeaseOptions>
{
    public ValidateOptionsResult Validate(string? name, LeaseOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Name))
            failures.Add("Lease:Name is required");
        if (string.IsNullOrWhiteSpace(options.Namespace))
            failures.Add("Lease:Namespace is required");
        if (options.DurationSeconds <= options.RenewIntervalSeconds)
            failures.Add("Lease:DurationSeconds must be greater than Lease:RenewIntervalSeconds");
        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
```

### Breakage Detection Test for ParentProvider Reflection
```csharp
// In SnmpCollector.Tests — verifies OTel SDK internals haven't changed
[Fact]
public void MetricRoleGatedExporter_ParentProviderReflection_IsStillAccessible()
{
    // Verifies that BaseExporter<Metric>.ParentProvider is accessible via reflection.
    // If OTel SDK removes or renames this property, this test fails, providing early warning
    // before the MetricRoleGatedExporter silently stops propagating resource attributes.
    var prop = typeof(BaseExporter<Metric>)
        .GetProperty("ParentProvider", BindingFlags.Public | BindingFlags.Instance);

    Assert.NotNull(prop);
    Assert.True(prop!.CanRead, "ParentProvider must have a readable getter");
    Assert.True(prop.CanWrite, "ParentProvider must be writable via reflection (internal set)");
    // Confirm it is NOT publicly settable (it should remain internal, not public)
    Assert.False(prop.SetMethod?.IsPublic ?? true,
        "ParentProvider setter should remain internal (not public) — if this fails, reflection is no longer needed");
}
```

### Two-Instance Integration Test Structure
```csharp
// Conceptual structure for SC#1 verification (07-05-PLAN.md)
// Cannot use real K8s in unit tests — use two host instances with AlwaysLeaderElection on one
// and a stub ILeaderElection(IsLeader=false) on the other.
// Verify: only the "leader" instance exports LeaderMeterName metrics.

public class MetricRoleGatedExporterIntegrationTests
{
    [Fact]
    public async Task FollowerInstance_DoesNotExport_BusinessMetrics()
    {
        var capturedBatches = new List<Batch<Metric>>();
        var followerElection = new StubLeaderElection(isLeader: false);
        var captureExporter = new CapturingMetricExporter(capturedBatches);
        var gated = new MetricRoleGatedExporter(
            captureExporter, followerElection, TelemetryConstants.LeaderMeterName);

        // ... record a business metric (snmp_gauge) and a pipeline metric (snmp.event.published)
        // ... call Export
        // Assert: capturedBatches contains pipeline metric but NOT business metric
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Phase 1: Direct `AddOtlpExporter()` on metrics | Phase 7: `AddReader()` with `MetricRoleGatedExporter` wrapping `OtlpMetricExporter` | Phase 7 (this phase) | Enables per-meter export gating |
| Phase 1: Static `string role` in `SnmpLogEnrichmentProcessor` | Phase 7: `Func<string> roleProvider` delegate | Phase 7 (this phase) | Enables dynamic role in log enrichment |
| Phase 1: `siteOptions.Role` in `SnmpConsoleFormatter` | Phase 7: `ILeaderElection.CurrentRole` | Phase 7 (this phase) | Console log lines show actual leader/follower role |
| Phase 1: Single `TelemetryConstants.MeterName` | Phase 7: `MeterName` (pipeline) + `LeaderMeterName` (business) | Phase 7 (this phase) | Enables selective gating without affecting pipeline metrics |
| No leader election | `K8sLeaseElection` as BackgroundService | Phase 7 (this phase) | Kubernetes-native HA for metric export |

**Deprecated/outdated after Phase 7:**
- `SiteOptions.Role` property: still exists but its value ("standalone") is no longer used for console formatter or log enrichment — `ILeaderElection.CurrentRole` takes precedence. May be removed in a future cleanup phase.

---

## Claude's Discretion Decisions

The following are locked-in recommendations for the planner to use directly.

### Lease Timing Parameters
- **LeaseDuration: 15s** — K8s client-go default; well-understood industry standard
- **RenewIntervalSeconds: 10s** — Leader renews every 10s; 5s buffer before expiry
- **RenewDeadline: 13s** (= DurationSeconds - 2) — Leader has 13s to renew before others may acquire

### Network Partition Handling
- **Stop exporting on renewal failure.** The `LeaderElector` fires `OnStoppedLeading` when `RenewDeadline` is exceeded. This sets `_isLeader = false` and the next export cycle skips business metrics. No extra code needed. Brief gap preferred over prolonged duplicate.

### Lease Identity
- **Use `SiteOptions.PodIdentity` (defaulting to `HOSTNAME` env var, then `Environment.MachineName`).**  The `HOSTNAME` env var in K8s is the pod name — globally unique within a namespace. This is the K8s convention for lease holder identity (consistent with `client-go` patterns).

### Metric Gap vs Overlap
- **Accept brief gap.** New leader exports immediately on `OnStartedLeading` (no warm-up). Gap = one periodic export interval (default 60s) at most. Better than overlapping duplicate series in Prometheus.

### Role Change Observability
- **Log only (no counters, no labels).** `OnStartedLeading` and `OnStoppedLeading` log at `Information` level. Adding a counter (e.g., `leader.transitions`) would be on `LeaderMeterName`, creating a chicken-and-egg: the leader transition counter only appears in Prometheus when you're the leader, which is after you've already transitioned. Log-only is operationally sufficient for observing transitions.

### New Leader Warm-Up
- **Export immediately.** OTel cumulative instruments retain their in-memory accumulated state. First export from new leader correctly shows the cumulative sum. No warm-up period needed.

### Export Gating Mechanism
- **Custom exporter wrapper (`MetricRoleGatedExporter`)** — confirmed as the correct approach. Cannot use conditional handler or AddMeter guards because System.Runtime metrics are on a separate meter (not our code) and cannot be conditionally excluded via AddMeter.

### Business vs Pipeline Meter Discrimination
- **Separate meter names** (`TelemetryConstants.MeterName` for pipeline, `TelemetryConstants.LeaderMeterName` for business). The gated exporter filters by `metric.MeterName`. Instrument name prefix approach (`snmp_gauge_leader.*`) would require parsing instrument names — fragile. Separate meters are clean and match the Simetra reference.

### Accumulated State on Role Change
- **Retain full accumulated state.** OTel cumulative metrics (gauge, counter) accumulate state in the SDK's `AggregatorStore`. When a pod becomes leader, it exports its full accumulated state as the starting point. This may produce a "jump" in Prometheus at first export if the pod was running as follower for a while and instruments were being updated. This is correct behavior — Prometheus handles cumulative resets via `rate()` and `increase()` functions.

### AlwaysLeaderElection Implementation Depth
- **Simple IsLeader=true, CurrentRole="leader" only.** No lifecycle simulation. Local dev runs as always-leader. Testing the election behavior requires a K8s environment or a mock `ILeaderElection` stub in tests.

### K8s Auto-Detection
- **`KubernetesClientConfiguration.IsInCluster()`** — verified as the correct mechanism. Checks `KUBERNETES_SERVICE_HOST` + `KUBERNETES_SERVICE_PORT` env vars AND service account token/cert files. No explicit config flag needed.

### Console Formatter Role Update Behavior
- **Dynamic role via `ILeaderElection.CurrentRole`** resolved lazily from `IServiceProvider` in `EnsureServicesResolved()`. The formatter already has the lazy resolution pattern — just add `ILeaderElection` to the resolved services list.

### Force Leader Debug Mechanism
- **No force leader mechanism.** AlwaysLeaderElection covers local dev. No config override needed for production.

---

## Open Questions

1. **Batch<Metric> Constructor with Array — Verify API**
   - What we know: Simetra reference uses `new Batch<Metric>(ungated.ToArray(), ungated.Count)` to construct a filtered batch
   - What's unclear: Whether `Batch<Metric>` has this constructor in OTel 1.15.0 or if it was added/removed
   - Recommendation: Verify during 07-03-PLAN.md implementation; if the constructor is not available, use an alternative approach (see below)
   - Alternative if unavailable: Enumerate the original batch and build a custom filtered list, then use `ReadOnlySpan<Metric>` if a batch cannot be constructed from array

2. **OtlpExporterOptions Endpoint Property**
   - What we know: Simetra uses `new OtlpExporterOptions { Endpoint = new Uri(...) }` when constructing `OtlpMetricExporter` manually
   - What's unclear: Whether `OtlpExporterOptions` still has `Endpoint` as a direct settable property in 1.15.0 (some versions moved to `OtlpExporterOptions.Endpoint` being derived from a default endpoint)
   - Recommendation: Verify during 07-03-PLAN.md; the Simetra reference works on 1.15.0 so this pattern is confirmed valid

3. **SnmpConsoleFormatter Role After Phase 7**
   - What we know: Formatter currently reads `_siteOptions?.Value.Role` (static "standalone")
   - What's unclear: Whether to remove `SiteOptions.Role` entirely or keep it as a fallback
   - Recommendation: Keep `SiteOptions.Role` as fallback (defensive); use `ILeaderElection.CurrentRole` as primary source when `ILeaderElection` is resolvable; this avoids breaking the formatter during early startup before DI is fully initialized

---

## Sources

### Primary (HIGH confidence)
- Simetra reference codebase — `src/Simetra/Telemetry/ILeaderElection.cs`, `AlwaysLeaderElection.cs`, `K8sLeaseElection.cs`, `MetricRoleGatedExporter.cs`, `RoleGatedExporter.cs` — direct source code reviewed; verified working implementation
- Simetra reference codebase — `src/Simetra/Extensions/ServiceCollectionExtensions.cs` — DI singleton pattern, AddReader wiring, two-meter design verified
- GitHub: `open-telemetry/opentelemetry-dotnet` — `src/OpenTelemetry/BaseExporter.cs` — `ParentProvider { get; internal set; }` confirmed, no `SetParentProvider` method exists on `BaseExporter`
- GitHub: `open-telemetry/opentelemetry-dotnet` — `src/OpenTelemetry/Metrics/Reader/BaseExportingMetricReader.cs` — `SetParentProvider` internal override sets `this.exporter.ParentProvider = parentProvider` via internal setter; called during `MeterProviderSdk` construction
- GitHub: `open-telemetry/opentelemetry-dotnet` — `src/OpenTelemetry/Metrics/MeterProviderSdk.cs` — `reader.SetParentProvider(this)` called in constructor; confirms wrapper exporter gets `ParentProvider` but inner exporter does not
- OTel 1.15.0 confirmed as current stable (NuGet, released 2026-01-21) — matches project version
- GitHub: `kubernetes-client/csharp` — `src/KubernetesClient/LeaderElection/LeaderElector.cs` — `RunAndTryToHoldLeadershipForeverAsync` verified as valid; event handlers (`OnStartedLeading`, `OnStoppedLeading`, `OnNewLeader`, `OnError`) verified
- GitHub: `kubernetes-client/csharp` — `src/KubernetesClient/KubernetesClientConfiguration.InCluster.cs` — `IsInCluster()` checks `KUBERNETES_SERVICE_HOST` + `KUBERNETES_SERVICE_PORT` + token file + CA cert file

### Secondary (MEDIUM confidence)
- NuGet Gallery — KubernetesClient 19.0.2 is latest (2026-02-24); 18.0.13 used in Simetra reference is still valid; no breaking changes to LeaderElection API confirmed (verified via changelog review)
- OTel extending-the-sdk README — confirmed `ParentProvider.GetResource()` is standard pattern for exporters; `ParentProvider` is set by SDK infrastructure automatically on direct exporter
- kubernetes-client.github.io API docs — `LeaseLock(IKubernetes, string namespace, string name, string identity)` constructor confirmed

### Tertiary (LOW confidence)
- Batch<Metric> constructor from array — pattern seen in Simetra reference code but not verified against OTel 1.15.0 API reference directly; flagged as Open Question

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — OTel 1.15.0 and KubernetesClient 18.0.13 verified; reference implementation reviewed directly
- Architecture: HIGH — Simetra reference implementation is a working, battle-tested port target; two-meter design verified against OTel docs
- Pitfalls: HIGH — Single-instance DI pattern is a known, documented DI pitfall; ParentProvider reflection is directly verified against OTel source
- Claude's Discretion decisions: MEDIUM — Lease timing from K8s client-go defaults (well-known); gap vs overlap strategy derived from OTel cumulative semantics and Prometheus behavior

**Research date:** 2026-03-05
**Valid until:** 2026-04-05 (stable libraries; OTel 1.16.0 could change ParentProvider internals — run breakage detection test on upgrade)

**Reference implementation files used:**
- `/c/Users/UserL/source/repos/Simetra117/src/Simetra/Telemetry/ILeaderElection.cs`
- `/c/Users/UserL/source/repos/Simetra117/src/Simetra/Telemetry/AlwaysLeaderElection.cs`
- `/c/Users/UserL/source/repos/Simetra117/src/Simetra/Telemetry/K8sLeaseElection.cs`
- `/c/Users/UserL/source/repos/Simetra117/src/Simetra/Telemetry/MetricRoleGatedExporter.cs`
- `/c/Users/UserL/source/repos/Simetra117/src/Simetra/Configuration/LeaseOptions.cs`
- `/c/Users/UserL/source/repos/Simetra117/src/Simetra/Extensions/ServiceCollectionExtensions.cs`
