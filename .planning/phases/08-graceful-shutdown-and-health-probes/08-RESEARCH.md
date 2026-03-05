# Phase 8: Graceful Shutdown and Health Probes - Research

**Researched:** 2026-03-05
**Domain:** .NET 9 Generic Host graceful shutdown orchestration + Kubernetes health probe HTTP endpoints
**Confidence:** HIGH

---

## Summary

Phase 8 has a direct reference implementation in the Simetra project (`src/Simetra/`) that covers every required component: `GracefulShutdownService`, `StartupHealthCheck`, `ReadinessHealthCheck`, `LivenessHealthCheck`, `ILivenessVectorService`/`LivenessVectorService`, `IJobIntervalRegistry`/`JobIntervalRegistry`, `LivenessOptions`, and the DI wiring pattern. All components are source-portable with namespace substitution and SnmpCollector-specific logic adjustments.

The most consequential architectural decision in this phase is the HTTP transport for health probes. Simetra uses `Microsoft.NET.Sdk.Web` and `MapHealthChecks()` from ASP.NET Core. SnmpCollector uses `Microsoft.NET.Sdk` (Generic Host, no web) per decision [01-01]. Adding HTTP health probe endpoints requires either (a) switching the project SDK and Dockerfile base image, or (b) adding a `FrameworkReference` to `Microsoft.AspNetCore.App` in the `.csproj` while keeping `Microsoft.NET.Sdk`. Both approaches are valid and verified. The FrameworkReference approach is lighter — it does not switch the SDK or change how the project builds, but it does require switching the Dockerfile runtime image from `mcr.microsoft.com/dotnet/runtime:9.0` to `mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim` so that the ASP.NET Core shared framework is present at runtime. This is the recommended approach: minimal surgical change, no project type change.

The graceful shutdown sequence is fully resolved: 5 ordered steps with per-step CTS budgets, telemetry flush as independent CTS (always runs), `GracefulShutdownService` registered last in DI (stops first), `HostOptions.ShutdownTimeout = 30s`. The Simetra implementation is directly portable. The key SnmpCollector difference is that Step 4 drain uses the existing `IDeviceChannelManager.CompleteAll()` + a new `WaitForDrainAsync()` (which must be added to `IDeviceChannelManager` — it exists in Simetra but not yet in SnmpCollector).

**Primary recommendation:** Add a `FrameworkReference` to `Microsoft.AspNetCore.App`, switch the Dockerfile runtime image to `aspnet:9.0-bookworm-slim`, and port the Simetra health check and lifecycle implementation directly with SnmpCollector-specific adaptations to the startup and readiness check logic.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Diagnostics.HealthChecks | Built into ASP.NET Core shared framework | `IHealthCheck`, `HealthCheckResult`, `AddHealthChecks()` | Standard .NET health check abstraction |
| Microsoft.AspNetCore.Diagnostics.HealthChecks | Built into ASP.NET Core shared framework | `MapHealthChecks()`, `HealthCheckOptions` | K8s HTTP probe endpoint routing |
| Microsoft.Extensions.Hosting | 9.0.0 (already present) | `HostOptions.ShutdownTimeout`, `IHostedService` | Already in project; controls 30s shutdown budget |
| Quartz | 3.15.1 (already present via Quartz.Extensions.Hosting) | `ISchedulerFactory.GetScheduler()`, `IScheduler.Standby()` | Already in project; standby prevents new job fires |
| OpenTelemetry | 1.15.0 (already present) | `MeterProvider.ForceFlush()` | Already in project; telemetry flush in Step 5 |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.AspNetCore.App (FrameworkReference) | Implicit with .NET 9 SDK | Enables `WebApplication`, `MapHealthChecks`, Kestrel | Required to add HTTP health probe surface to Generic Host project |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| FrameworkReference + aspnet runtime image | Switch Sdk to Microsoft.NET.Sdk.Web | Both work. SDK switch is more invasive — changes implicit includes, project type. FrameworkReference is surgical. |
| FrameworkReference + aspnet runtime image | Raw TCP socket listener for probes | TCP probes are K8s `tcpSocket` not `httpGet`. httpGet is standard and provides URL-level granularity. Not recommended. |
| FrameworkReference + aspnet runtime image | Custom minimal HTTP listener (HttpListener) | Hand-rolling HTTP is unnecessary complexity when `FrameworkReference + MapHealthChecks` solves it cleanly. |
| `MeterProvider.ForceFlush()` in GracefulShutdownService | Let OTel dispose handle flush | OTel dispose is not time-budgeted and may not complete before process exit. ForceFlush with explicit timeout is deterministic. |

**Installation:**
```xml
<!-- Add to SnmpCollector.csproj -->
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

```dockerfile
# Dockerfile: change runtime image from:
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
# to:
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final
```

---

## Architecture Patterns

### Recommended Project Structure
```
src/SnmpCollector/
├── Lifecycle/
│   └── GracefulShutdownService.cs    # New: 5-step shutdown orchestrator, registered LAST
├── HealthChecks/
│   ├── StartupHealthCheck.cs          # New: OID map loaded + poll definitions registered
│   ├── ReadinessHealthCheck.cs        # New: trap listener bound + device registry populated
│   └── LivenessHealthCheck.cs         # New: per-job staleness vs interval * graceMultiplier
├── Pipeline/
│   ├── ILivenessVectorService.cs      # New: Stamp(jobKey), GetAllStamps()
│   ├── LivenessVectorService.cs       # New: ConcurrentDictionary<string, DateTimeOffset>
│   ├── IJobIntervalRegistry.cs        # New: Register(jobKey, seconds), TryGetInterval()
│   ├── JobIntervalRegistry.cs         # New: Dictionary<string, int> — populated in AddSnmpScheduling
│   ├── IDeviceChannelManager.cs       # Add: WaitForDrainAsync(CancellationToken)
│   └── DeviceChannelManager.cs        # Add: WaitForDrainAsync implementation
├── Configuration/
│   └── LivenessOptions.cs             # New: GraceMultiplier (default 2.0, Range 1.0-100.0)
├── Jobs/
│   ├── MetricPollJob.cs               # Update: stamp ILivenessVectorService in finally block
│   └── CorrelationJob.cs              # Update: stamp ILivenessVectorService in finally block
├── Extensions/
│   └── ServiceCollectionExtensions.cs # Update: AddSnmpScheduling registers ILivenessVectorService
│                                       #   + IJobIntervalRegistry, intervals registered per job
│                                       #   + AddSnmpHealthChecks() + AddSnmpLifecycle()
├── Program.cs                         # Update: WebApplication.CreateBuilder replaces Host.CreateApplicationBuilder
│                                       #   + MapHealthChecks for /healthz/startup, /healthz/ready, /healthz/live
│                                       #   + HostOptions.ShutdownTimeout = 30s
└── Dockerfile                         # Update: aspnet:9.0-bookworm-slim runtime image
```

### Pattern 1: GracefulShutdownService — Registered Last, Stops First

**What:** `IHostedService` with empty `StartAsync`. All logic in `StopAsync`. Registered last via `AddHostedService<GracefulShutdownService>()` so the .NET Generic Host (which stops in reverse registration order) calls its `StopAsync` first.

**When to use:** Required — SHUT-01.

**Example (from Simetra `src/Simetra/Lifecycle/GracefulShutdownService.cs`):**
```csharp
// Source: Simetra reference — direct port with SnmpCollector namespace
public sealed class GracefulShutdownService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown sequence starting");

        // Step 1: Release lease (3s budget) — SHUT-02
        await ExecuteWithBudget("ReleaseLease", TimeSpan.FromSeconds(3), async () =>
        {
            var leaseService = _serviceProvider.GetService<K8sLeaseElection>();
            if (leaseService is not null)
                await leaseService.StopAsync(CancellationToken.None);
            else
                _logger.LogDebug("No K8sLeaseElection registered (local dev), skipping lease release");
        }, cancellationToken);

        // Step 2: Stop SNMP trap listener (3s budget) — SHUT-03
        await ExecuteWithBudget("StopListener", TimeSpan.FromSeconds(3), async () =>
        {
            var listener = _serviceProvider.GetServices<IHostedService>()
                .OfType<SnmpTrapListenerService>()
                .FirstOrDefault();
            if (listener is not null)
                await listener.StopAsync(CancellationToken.None);
        }, cancellationToken);

        // Step 3: Scheduler standby (3s budget) — SHUT-04
        await ExecuteWithBudget("PauseScheduler", TimeSpan.FromSeconds(3), async () =>
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            await scheduler.Standby();
        }, cancellationToken);

        // Step 4: Drain in-flight operations (8s budget) — SHUT-05
        await ExecuteWithBudget("DrainChannels", TimeSpan.FromSeconds(8), async () =>
        {
            _channelManager.CompleteAll();
            await _channelManager.WaitForDrainAsync(CancellationToken.None);
        }, cancellationToken);

        // Step 5: Flush telemetry (independent CTS — ALWAYS runs) — SHUT-06
        await FlushTelemetryAsync();

        _logger.LogInformation("Graceful shutdown sequence completed");
    }

    private async Task ExecuteWithBudget(
        string stepName, TimeSpan budget, Func<Task> action, CancellationToken outerToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        stepCts.CancelAfter(budget);
        try { await action(); }
        catch (OperationCanceledException) when (stepCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Shutdown step {StepName} exceeded budget of {BudgetSeconds}s, abandoning",
                stepName, budget.TotalSeconds);
        }
        catch (Exception ex) { _logger.LogError(ex, "Shutdown step {StepName} failed", stepName); }
    }

    private async Task FlushTelemetryAsync()
    {
        // SHUT-06: Independent CTS — NOT linked to outer shutdown token
        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(() =>
            {
                var meterProvider = _serviceProvider.GetService<MeterProvider>();
                meterProvider?.ForceFlush(timeoutMilliseconds: 5000);
                // No TracerProvider — LOG-07: SnmpCollector has no traces
            }, flushCts.Token);
            _logger.LogInformation("Telemetry flush completed");
        }
        catch (OperationCanceledException) { _logger.LogWarning("Telemetry flush exceeded 5s budget"); }
        catch (Exception ex) { _logger.LogError(ex, "Telemetry flush failed"); }
    }
}
```

### Pattern 2: Double-Stop Idempotency

**What:** `GracefulShutdownService` calls `K8sLeaseElection.StopAsync()` and `SnmpTrapListenerService.StopAsync()` explicitly (Steps 1 and 2). The .NET host will also call their `StopAsync` again in reverse registration order after all hosted services stop. Both services are derived from `BackgroundService`, whose `StopAsync` is idempotent — calling it twice is harmless (the CancellationTokenSource is already cancelled on the second call, awaiting an already-completed task).

**Key note from Simetra GracefulShutdownService comment:**
> K8sLeaseElection and SnmpListenerService extend BackgroundService, whose StopAsync is idempotent (cancels the stoppingToken via an internal CancellationTokenSource, then awaits ExecuteTask — both are safe to call multiple times). The framework will call their StopAsync AGAIN in reverse registration order after this service completes, but that second call is a harmless no-op.

### Pattern 3: Liveness Vector — Stamp in Finally Block

**What:** Every Quartz job (MetricPollJob, CorrelationJob) calls `_liveness.Stamp(jobKey)` in the `finally` block of `Execute()`. The `jobKey` is `context.JobDetail.Key.Name` (e.g., `"metric-poll-device1-0"`, `"correlation"`). This ensures the stamp is written regardless of success or failure.

**Example (from Simetra `src/Simetra/Jobs/CorrelationJob.cs` and `MetricPollJob.cs`):**
```csharp
public async Task Execute(IJobExecutionContext context)
{
    var jobKey = context.JobDetail.Key.Name;
    try
    {
        // ... job logic ...
    }
    catch (OperationCanceledException) { throw; } // propagate shutdown
    catch (Exception ex) { _logger.LogError(ex, "Job {JobKey} failed", jobKey); }
    finally
    {
        // HLTH-05: Stamp liveness vector on completion (always, even on failure)
        _liveness.Stamp(jobKey);
    }
}
```

### Pattern 4: Job Interval Registry — Populated During DI Registration

**What:** `JobIntervalRegistry` is a `Dictionary<string, int>` singleton populated *during* `AddSnmpScheduling()` (before DI container is built), then registered as an `IJobIntervalRegistry` singleton. This is the only point where interval values are available — they come from options bound before DI.

**Example (from Simetra `src/Simetra/Extensions/ServiceCollectionExtensions.cs`):**
```csharp
// In AddSnmpScheduling:
var intervalRegistry = new JobIntervalRegistry();

services.AddQuartz(q =>
{
    // Register CorrelationJob
    var correlationKey = new JobKey("correlation");
    q.AddJob<CorrelationJob>(...);
    q.AddTrigger(t => t.ForJob(correlationKey)
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(correlationOptions.IntervalSeconds)...));
    intervalRegistry.Register("correlation", correlationOptions.IntervalSeconds);

    // Register MetricPollJob per device per poll group
    for (var di = 0; di < devicesOptions.Devices.Count; di++)
    {
        var device = devicesOptions.Devices[di];
        for (var pi = 0; pi < device.MetricPolls.Count; pi++)
        {
            var poll = device.MetricPolls[pi];
            var jobKey = $"metric-poll-{device.Name}-{pi}";
            // ... add job and trigger ...
            intervalRegistry.Register(jobKey, poll.IntervalSeconds);
        }
    }
});

services.AddSingleton<IJobIntervalRegistry>(intervalRegistry);
```

### Pattern 5: Health Check Registration with Tag Filtering

**What:** Three health checks registered with distinct tags. `MapHealthChecks` filters by tag so each endpoint runs only its own check.

**Example (from Simetra `src/Simetra/Extensions/ServiceCollectionExtensions.cs` and `Program.cs`):**
```csharp
// In AddSnmpHealthChecks():
services.AddHealthChecks()
    .AddCheck<StartupHealthCheck>("startup", tags: new[] { "startup" })
    .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "ready" })
    .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" });

// In Program.cs (WebApplication):
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});
// ... same for /healthz/ready (tag "ready") and /healthz/live (tag "live")
```

### Pattern 6: Program.cs — WebApplication Replaces Host

**What:** `Host.CreateApplicationBuilder(args)` becomes `WebApplication.CreateBuilder(args)`. The `WebApplicationBuilder` is an `IHostApplicationBuilder` so all existing extension method calls (`AddSnmpTelemetry`, `AddSnmpConfiguration`, etc.) compile without change. `host.RunAsync()` becomes `app.Run()`.

**Example:**
```csharp
// BEFORE (current Program.cs):
var builder = Host.CreateApplicationBuilder(args);
builder.AddSnmpTelemetry();
builder.Services.AddSnmpConfiguration(builder.Configuration);
builder.Services.AddSnmpPipeline();
builder.Services.AddSnmpScheduling(builder.Configuration);
var host = builder.Build();
// ...
await host.RunAsync();

// AFTER (Phase 8):
var builder = WebApplication.CreateBuilder(args);
builder.AddSnmpTelemetry();
builder.Services.AddSnmpConfiguration(builder.Configuration);
builder.Services.AddSnmpPipeline();
builder.Services.AddSnmpScheduling(builder.Configuration);
builder.Services.AddSnmpHealthChecks();  // NEW
builder.Services.AddSnmpLifecycle();     // NEW (registers GracefulShutdownService LAST)
var app = builder.Build();

// Generate first correlationId
var correlationService = app.Services.GetRequiredService<ICorrelationService>();
correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"));

// Map health check endpoints
app.MapHealthChecks("/healthz/startup", ...);
app.MapHealthChecks("/healthz/ready", ...);
app.MapHealthChecks("/healthz/live", ...);

try { app.Run(); }
catch (OptionsValidationException ex) { /* existing fail-fast handler */ throw; }
```

### Pattern 7: WaitForDrainAsync — Add to IDeviceChannelManager

**What:** SnmpCollector's `IDeviceChannelManager` only has `CompleteAll()` (marks writers done). Simetra's version also has `WaitForDrainAsync()` that awaits `Channel.Reader.Completion` for all channels. This must be added to SnmpCollector to support Step 4.

**Example (from Simetra `src/Simetra/Pipeline/DeviceChannelManager.cs`):**
```csharp
public async Task WaitForDrainAsync(CancellationToken cancellationToken)
{
    var completionTasks = _channels.Values
        .Select(channel => channel.Reader.Completion)
        .ToList();
    await Task.WhenAll(completionTasks).WaitAsync(cancellationToken);
    _logger.LogInformation("All device channels drained");
}
```

`Channel.Reader.Completion` is a `Task` that completes when the channel is marked done AND all pending items have been consumed. `WaitAsync(cancellationToken)` applies the 8s budget from `ExecuteWithBudget`.

### Pattern 8: SnmpCollector-Specific Health Check Logic

The Simetra health checks use different conditions than SnmpCollector needs. The CONTEXT.md specifies:

**StartupHealthCheck (HLTH-01):** Healthy when OID map loaded AND poll definitions registered.
- Simetra's checks correlation service — not applicable to SnmpCollector.
- SnmpCollector's version checks `IOidMapService.Entries.Count > 0` (or a dedicated "ready" flag) AND `IJobIntervalRegistry` has entries (poll jobs registered).

**ReadinessHealthCheck (HLTH-02):** Healthy when trap listener bound on UDP 162 AND device registry populated.
- Simetra checks device channels registered + Quartz scheduler running.
- SnmpCollector's version checks `IDeviceRegistry.AllDevices.Count > 0` AND trap listener "is running" signal (needs a flag on SnmpTrapListenerService or use IDeviceChannelManager.DeviceNames.Count > 0 as proxy).

**LivenessHealthCheck (HLTH-03):** Per-job staleness via `ILivenessVectorService` + `IJobIntervalRegistry`.
- Simetra's implementation is directly portable — no SnmpCollector-specific changes needed.

### Pattern 9: Startup vs Readiness "Ready" Signal

**What:** The startup and readiness checks need to verify that initialization has completed. For startup, the simplest approach: `IJobIntervalRegistry.TryGetInterval("correlation", out _)` — if the correlation job is registered, scheduling is done. For readiness, `IDeviceChannelManager.DeviceNames.Count > 0` — if channels exist, the device registry is populated and trap listener is initialized.

**Important note:** The readiness check for "trap listener bound on UDP 162" cannot be confirmed via the existing `SnmpTrapListenerService` — it has no `IsRunning` property. Options:
1. Add a `bool IsRunning` flag to `SnmpTrapListenerService` (set after UDP socket bind)
2. Use `DeviceNames.Count > 0` as proxy (channels exist = listener initialized, close enough for readiness)
3. Check the Quartz scheduler `IsStarted` state (already done in Simetra's ReadinessHealthCheck)

The Simetra reference checks `scheduler.IsStarted && !scheduler.IsShutdown` — this is a good proxy for "application is ready" and avoids needing a custom flag on the listener.

### Anti-Patterns to Avoid

- **Duplicate shutdown logic:** Do NOT add independent shutdown logic to `K8sLeaseElection.StopAsync` or `SnmpTrapListenerService.StopAsync` beyond what they already do. `GracefulShutdownService` is the SINGLE orchestrator — it calls their `StopAsync` explicitly. The framework's second call is a no-op.
- **Linking telemetry flush CTS to outer token:** Step 5's CTS must be `new CancellationTokenSource(5s)` NOT `CreateLinkedTokenSource(outerToken)`. If the outer host token is already cancelled (timeout), linking would immediately cancel the flush.
- **Registering GracefulShutdownService before other hosted services:** Must be LAST. Registration order = stop order reversed.
- **Calling `scheduler.Shutdown()` instead of `scheduler.Standby()`:** `Shutdown()` is permanent and prevents re-use. `Standby()` pauses triggers without destroying the scheduler — allows in-flight jobs to complete under `WaitForJobsToComplete = true`.
- **Using `IPublisher.Publish` in channel consumers:** Already handled; ChannelConsumerService uses `ISender.Send`. Not a Phase 8 concern but don't regress.
- **Guessing jobKey in liveness stamps:** `context.JobDetail.Key.Name` is the authoritative key. It must match the key registered in `JobIntervalRegistry`. In SnmpCollector, poll jobs use `$"metric-poll-{device.Name}-{pi}"`. The `intervalRegistry.Register(...)` call must use the exact same string.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| HTTP health probe endpoint | Raw HttpListener loop | `FrameworkReference + MapHealthChecks` | MapHealthChecks handles routing, status codes, serialization, tag filtering |
| Shutdown step timeouts | Manual Task.WhenAny with delay | `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter` | Standard .NET pattern, CTS is composable and doesn't require background tasks |
| Channel drain detection | Polling `Count` in a loop | `Channel.Reader.Completion` Task | Framework-native completion signal, no polling, no race conditions |
| Job staleness detection | Quartz `GetCurrentlyExecutingJobs` | Liveness vector (ConcurrentDictionary<string, DateTimeOffset>) | Quartz API shows running jobs; liveness vector shows last-COMPLETED — different semantics |
| Per-step timeout orchestration | Single global CTS for all steps | Per-step `CancellationTokenSource` linked to outer | Allows each step to have its own budget; outer timeout enforced by HostOptions.ShutdownTimeout |

**Key insight:** The Simetra reference is the "don't hand-roll" target — everything it does has a reason. Port it; don't reinvent.

---

## Common Pitfalls

### Pitfall 1: FrameworkReference Without Runtime Image Change

**What goes wrong:** Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` but keep the Dockerfile runtime image as `mcr.microsoft.com/dotnet/runtime:9.0`. The app builds fine but crashes at runtime with `System.IO.FileNotFoundException: Could not load file or assembly 'Microsoft.AspNetCore.Routing.dll'` or similar.

**Why it happens:** `dotnet/runtime` image does not include the ASP.NET Core shared framework. `MapHealthChecks`, Kestrel, routing — all in `Microsoft.AspNetCore.App` — are not present.

**How to avoid:** Always pair the FrameworkReference with `FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final`.

**Warning signs:** Build succeeds; `dotnet run` locally works (dev machine has full SDK); container fails on startup.

### Pitfall 2: GracefulShutdownService Not Registered Last

**What goes wrong:** `AddHostedService<GracefulShutdownService>()` is called before other hosted services. The .NET host stops services in reverse registration order, so GracefulShutdownService stops LAST — after Quartz, after the listener. By that point, the channels are already drained by the framework's normal shutdown sequence, and the 5-step orchestration is partially redundant or racing with the framework.

**Why it happens:** Easy to forget registration order matters for shutdown.

**How to avoid:** `AddSnmpLifecycle()` must be the LAST call in Program.cs DI registration. Add a code comment: `// MUST BE LAST — registered last = stops first`.

**Warning signs:** Lease release happens after telemetry flush; drain step completes instantly with no work (already drained by framework).

### Pitfall 3: Liveness Vector Stamps Never Happen

**What goes wrong:** Jobs never stamp the vector. `LivenessHealthCheck.GetAllStamps()` returns empty. Since empty stamps mean no stale jobs found (the loop has nothing to iterate), liveness always returns Healthy — even if jobs are hung.

**Why it happens:** Forgot to inject `ILivenessVectorService` into jobs, or placed `_liveness.Stamp(jobKey)` in the `try` block instead of `finally`.

**How to avoid:** Stamp in `finally` block of every job. Verify with a test that a job that throws still stamps.

**Warning signs:** LivenessHealthCheck always returns Healthy regardless of job state; `GetAllStamps()` is empty after jobs have run.

### Pitfall 4: Job Key Mismatch Between Registry and Vector

**What goes wrong:** `JobIntervalRegistry.Register("metric-poll-device1-0", 30)` but `MetricPollJob.Execute` stamps `_liveness.Stamp("metric-poll-device1-0")` with a different format, or vice versa. `LivenessHealthCheck.TryGetInterval(jobKey, out _)` always returns `false`, so all stamps are skipped (`continue` in the loop), and liveness always returns Healthy.

**Why it happens:** The job key in `JobDataMap` / `JobDetail.Key.Name` may not match what was registered. In SnmpCollector, the job key is `$"metric-poll-{device.Name}-{pi}"`. Both the `intervalRegistry.Register(...)` call and the stamp use `context.JobDetail.Key.Name`, so they must be consistent.

**How to avoid:** Use `context.JobDetail.Key.Name` as the stamp key (guaranteed to match Quartz registration). Register the same string in `intervalRegistry.Register(...)`.

**Warning signs:** `TryGetInterval` always returns false; liveness never reports stale jobs.

### Pitfall 5: StartupHealthCheck Condition Too Simple

**What goes wrong:** Startup check returns Healthy immediately (before OID map and poll definitions are ready), allowing K8s to send traffic before the application is operational. This violates HLTH-01.

**Why it happens:** `ICorrelationService.CurrentCorrelationId != null` is already true by the time probes start (set in Program.cs before `app.Run()`). A correlation-based startup check would always pass.

**How to avoid:** SnmpCollector's startup check must check `IJobIntervalRegistry` (has poll jobs registered, meaning scheduler is configured) AND that the device registry / OID map have loaded. Using `IJobIntervalRegistry` count > 0 confirms scheduling completed.

**Warning signs:** Startup probe returns 200 instantly; no actual startup gate.

### Pitfall 6: Drain Step Does Not Wait (Missing WaitForDrainAsync)

**What goes wrong:** Step 4 calls `CompleteAll()` (marks channels done) but does not await drain completion. Buffered varbinds in channels are abandoned when the process exits. This can cause data loss and corrupted counter baselines.

**Why it happens:** `IDeviceChannelManager` in SnmpCollector currently has `CompleteAll()` but not `WaitForDrainAsync()`. If GracefulShutdownService only calls `CompleteAll()`, Step 4 completes immediately without draining.

**How to avoid:** Add `Task WaitForDrainAsync(CancellationToken)` to `IDeviceChannelManager` interface and implement it in `DeviceChannelManager` using `Channel.Reader.Completion`. GracefulShutdownService's Step 4 must call both.

**Warning signs:** Step 4 completes instantly; "All channel consumers completed" log appears before "Drain channels" step completes.

### Pitfall 7: GraceMultiplier Default Too Low

**What goes wrong:** GraceMultiplier = 1.0 means the liveness probe fails if a job is late by even one interval (e.g., job runs every 30s, probe fires at 30.1s — stale). This causes false-positive restarts.

**Why it happens:** Liveness probe has `periodSeconds: 15`, `failureThreshold: 3` in K8s. A stale job at exactly 1x interval generates failures immediately.

**How to avoid:** Default `GraceMultiplier = 2.0` (from Simetra reference). This gives each job 2x its interval before being considered stale. For a 30s poll job, staleness fires at >60s. K8s restarts after 3 consecutive failures (3 × 15s period = 45s after staleness detected).

**Warning signs:** K8s restarting pods for healthy-looking jobs; liveness 503 responses in normal operation.

### Pitfall 8: MeterProvider Not Resolved in FlushTelemetryAsync

**What goes wrong:** `_serviceProvider.GetService<MeterProvider>()` returns `null` even though OTel is registered. This happens if the `MeterProvider` singleton was not registered under the `MeterProvider` type key.

**Why it happens:** `AddOpenTelemetry().WithMetrics(...)` registers `MeterProvider` in DI, but the registration key is `MeterProvider` (the concrete SDK type, not an interface). It is resolvable via `GetService<MeterProvider>()` as confirmed by the Simetra reference. Verified: OTel Extensions.Hosting registers it as `MeterProvider`.

**How to avoid:** `GetService<MeterProvider>()` (not `IServiceProvider.GetRequiredService<MeterProvider>()`) — use nullable null-check to handle local dev where OTel may be partially wired. Same pattern as Simetra.

**Warning signs:** Telemetry flush step logs "Telemetry flush completed" instantly with 0 metrics exported.

---

## Code Examples

Verified patterns from the Simetra reference implementation:

### LivenessVectorService
```csharp
// Source: src/Simetra/Pipeline/LivenessVectorService.cs
public sealed class LivenessVectorService : ILivenessVectorService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stamps = new();

    public void Stamp(string jobKey) => _stamps[jobKey] = DateTimeOffset.UtcNow;

    public DateTimeOffset? GetStamp(string jobKey)
        => _stamps.TryGetValue(jobKey, out var ts) ? ts : null;

    public IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps()
        => _stamps.ToDictionary(kv => kv.Key, kv => kv.Value).AsReadOnly();
}
```

### JobIntervalRegistry
```csharp
// Source: src/Simetra/Pipeline/JobIntervalRegistry.cs
public sealed class JobIntervalRegistry : IJobIntervalRegistry
{
    private readonly Dictionary<string, int> _intervals = new(StringComparer.Ordinal);

    public void Register(string jobKey, int intervalSeconds) => _intervals[jobKey] = intervalSeconds;

    public bool TryGetInterval(string jobKey, out int intervalSeconds)
        => _intervals.TryGetValue(jobKey, out intervalSeconds);
}
```

### LivenessOptions
```csharp
// Source: src/Simetra/Configuration/LivenessOptions.cs
public sealed class LivenessOptions
{
    public const string SectionName = "Liveness";

    [Range(1.0, 100.0)]
    public double GraceMultiplier { get; set; } = 2.0;
}
```

### LivenessHealthCheck (staleness logic)
```csharp
// Source: src/Simetra/HealthChecks/LivenessHealthCheck.cs — directly portable
public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
{
    var stamps = _liveness.GetAllStamps();
    var now = DateTimeOffset.UtcNow;
    var staleEntries = new Dictionary<string, object>();

    foreach (var (jobKey, lastStamp) in stamps)
    {
        if (!_intervals.TryGetInterval(jobKey, out var intervalSeconds))
            continue; // unregistered job key — skip

        var threshold = TimeSpan.FromSeconds(intervalSeconds * _graceMultiplier);
        var age = now - lastStamp;

        if (age > threshold)
        {
            staleEntries[jobKey] = new
            {
                ageSeconds = Math.Round(age.TotalSeconds, 1),
                thresholdSeconds = threshold.TotalSeconds,
                lastStamp = lastStamp.ToString("O")
            };
        }
    }

    if (staleEntries.Count > 0)
    {
        _logger.LogWarning("Liveness check failed: {StaleCount} stale job(s) detected", staleEntries.Count);
        return Task.FromResult(HealthCheckResult.Unhealthy(
            $"{staleEntries.Count} stale job(s)",
            data: staleEntries.ToDictionary(kv => kv.Key, kv => kv.Value,
                StringComparer.Ordinal) as IReadOnlyDictionary<string, object>));
    }

    return Task.FromResult(HealthCheckResult.Healthy()); // No log on healthy — HLTH-07
}
```

### WaitForDrainAsync (add to DeviceChannelManager)
```csharp
// Source: src/Simetra/Pipeline/DeviceChannelManager.cs
public async Task WaitForDrainAsync(CancellationToken cancellationToken)
{
    var completionTasks = _channels.Values
        .Select(channel => channel.Reader.Completion)
        .ToList();
    await Task.WhenAll(completionTasks).WaitAsync(cancellationToken);
    _logger.LogInformation("All device channels drained");
}
```

### HostOptions.ShutdownTimeout (in AddSnmpLifecycle)
```csharp
// Source: src/Simetra/Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddSnmpLifecycle(this IServiceCollection services)
{
    services.Configure<HostOptions>(opts =>
        opts.ShutdownTimeout = TimeSpan.FromSeconds(30));   // SHUT-08

    services.AddHostedService<GracefulShutdownService>();   // SHUT-01: MUST BE LAST

    return services;
}
```

### K8s Deployment Probe Config (reference from Simetra deploy/k8s/deployment.yaml)
```yaml
startupProbe:
  httpGet:
    path: /healthz/startup
    port: health         # containerPort 8080
  initialDelaySeconds: 5
  periodSeconds: 3
  failureThreshold: 10   # 5s + 10*3s = 35s max startup window
readinessProbe:
  httpGet:
    path: /healthz/ready
    port: health
  periodSeconds: 10
  failureThreshold: 3
livenessProbe:
  httpGet:
    path: /healthz/live
    port: health
  periodSeconds: 15
  failureThreshold: 3    # 3 * 15s = 45s before restart
  timeoutSeconds: 5
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `AddOtlpExporter()` in metrics | Manual `OtlpMetricExporter` + `PeriodicExportingMetricReader` | Phase 7 decision [07-03] | Cannot use old pattern here — MetricRoleGatedExporter already uses manual construction |
| `Host.CreateApplicationBuilder` | `WebApplication.CreateBuilder` | Phase 8 | Enables `MapHealthChecks`; `WebApplicationBuilder` is `IHostApplicationBuilder` so existing extension methods compile unchanged |
| `mcr.microsoft.com/dotnet/runtime:9.0` Dockerfile | `mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim` | Phase 8 | Required for ASP.NET Core shared framework at runtime; adds ~50MB to image |
| No HTTP surface | Kestrel on port 8080 (health only) | Phase 8 | Minimal Kestrel surface for K8s probes; not a general API server |

**Deprecated/outdated:**
- `HealthCheck` via `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService` directly (Generic Host without HTTP) — works for code-level checks but cannot serve HTTP probes that K8s requires. Use `MapHealthChecks` instead.

---

## Decisions Recommended (Claude's Discretion)

### Shutdown Step Ordering and Overlap
**Decision:** Sequential execution with per-step CTS budgets. Steps run in this order:
1. Lease release (3s) — enables HA failover immediately
2. Listener stop (3s) — closes UDP socket, no new traps accepted
3. Scheduler standby (3s) — no new job fires, in-flight jobs continue
4. Drain channels (8s) — ConsumerService processes remaining buffered varbinds
5. Telemetry flush (5s, independent CTS) — always runs

**Rationale:** Steps 1-4 must be sequential (dependency chain: lease → listener → scheduler → drain). Step 5 is independent and must always run even if prior steps fail (SHUT-06).

**Total budget:** 3+3+3+8+5 = 22s budget on happy path, 30s ceiling via `HostOptions.ShutdownTimeout`.

### Step Timeout Behavior
**Decision:** Log Warning and continue to next step. Never re-throw. This matches Simetra's `ExecuteWithBudget` — `OperationCanceledException` from budget expiry → Warning log; other exceptions → Error log. Either way, execution moves to the next step.

**Rationale:** Abandoning a step and proceeding is safer than aborting the entire shutdown sequence. The remaining steps (especially telemetry flush) should still run.

### Drain Scope
**Decision:** Channel drain only (BoundedChannel buffers). Quartz jobs under `[DisallowConcurrentExecution]` will complete their current execution naturally — `WaitForJobsToComplete = true` on `QuartzHostedService` already handles this. MediatR handlers are synchronous within `ISender.Send` — they complete before the consumer loop iteration ends. Counter delta engine state is acceptable to lose (CONTEXT.md: "okay to lose last baseline").

**Rationale:** The channel buffer is the only explicit backpressure store. Quartz's own `WaitForJobsToComplete` handles job-level drain. MediatR has no queue — it's synchronous dispatch.

### Transport Mechanism
**Decision:** `FrameworkReference + MapHealthChecks` on port 8080. Not TCP socket, not custom HTTP listener.

**Rationale:** K8s `httpGet` probes are standard; they provide granular per-path health semantics (startup vs readiness vs liveness at separate URLs). TCP probes only verify port is open — no URL-level differentiation. Custom HTTP listener is unnecessary complexity.

### Liveness Grace Multiplier
**Decision:** Default `2.0`, configurable via `Liveness:GraceMultiplier`, `[Range(1.0, 100.0)]`.

**Rationale:** 2x interval gives a reasonable buffer for slow SNMP polls and Quartz scheduling variance. K8s liveness with `periodSeconds: 15, failureThreshold: 3` means 45s of consecutive stale before restart. With grace = 2.0 and a 30s poll, staleness fires at >60s — providing margin before K8s acts.

### Health Endpoint Port
**Decision:** Port 8080 (Kestrel default in .NET 8+ container environments). No custom port configuration unless needed by K8s service definitions.

**Rationale:** Port 8080 is the `.NET 8+` container default for Kestrel (environment variable `ASPNETCORE_HTTP_PORTS=8080` is the container default). No configuration override needed.

### Startup Probe Conditions (HLTH-01)
**Decision:** Healthy when `IJobIntervalRegistry` has at least one entry (confirms `AddSnmpScheduling` completed and Quartz jobs were registered).

**Rationale:** The OID map is loaded at startup via `IOptions<OidMapOptions>` (already bound by `AddSnmpConfiguration`). Poll definitions are registered during `AddSnmpScheduling`. `IJobIntervalRegistry.Count > 0` (or checking for "correlation" job key) is a clean proxy for "scheduler is configured".

### Readiness Probe Conditions (HLTH-02)
**Decision:** Healthy when `IDeviceChannelManager.DeviceNames.Count > 0` (device registry populated, channels created) AND `ISchedulerFactory.GetScheduler().IsStarted` (Quartz running).

**Rationale:** `DeviceNames.Count > 0` confirms device registry is populated and `DeviceChannelManager` initialized — which also implies `SnmpTrapListenerService` can function. The Quartz `IsStarted` check confirms the application is past startup phase. Together these gate readiness without requiring a custom flag on `SnmpTrapListenerService`.

### Failed Step Log Level
**Decision:**
- Step timeout (budget exceeded): `LogWarning`
- Step exception: `LogError`
- Step success: `LogInformation`

**Rationale:** Timeout is operational — step may retry on next shutdown. Exception is unexpected — needs attention. Success is routine — important to have in kubectl logs for post-mortem.

### Shutdown OTel Metrics
**Decision:** No special shutdown metrics. `MeterProvider.ForceFlush()` in Step 5 flushes all pending measurements. No new counters for shutdown steps.

**Rationale:** Shutdown metrics would need to be flushed before recording them — a circular problem. Log messages at appropriate levels provide the observability for shutdown steps. OTel metrics are for steady-state behavior.

### Console Logging During Shutdown
**Decision:** No change to `LoggingOptions.EnableConsole`. All shutdown log messages use the existing `ILogger<GracefulShutdownService>` which routes through the OTel OTLP exporter and console formatter as configured.

**Rationale:** Shutdown logs at Information/Warning/Error level are naturally visible in `kubectl logs`. No special handling needed.

### CorrelationJob Integration with Job Interval Registry
**Decision:** `CorrelationJob` participates in both liveness vector stamping AND job interval registry. `intervalRegistry.Register("correlation", correlationOptions.IntervalSeconds)` in `AddSnmpScheduling`. `_liveness.Stamp("correlation")` in `CorrelationJob.Execute` finally block.

**Rationale:** CorrelationJob is a real Quartz job with a configured interval. Including it in liveness detection catches the case where Quartz has stalled entirely (correlation job would be the first to show stale, since it typically runs every 30s).

---

## Open Questions

1. **IDeviceChannelManager.WaitForDrainAsync — Interface Change**
   - What we know: Simetra has this method; SnmpCollector does not. Must be added.
   - What's unclear: Whether existing tests for `IDeviceChannelManager` need updating (they mock the interface). The planner should flag test update as a required task.
   - Recommendation: Add to interface and implementation in the same plan as GracefulShutdownService (Plan 08-01 or 08-03).

2. **SnmpTrapListenerService Readiness Signal**
   - What we know: There is no `IsRunning` flag on `SnmpTrapListenerService`. The decision above uses `DeviceNames.Count > 0` as a proxy.
   - What's unclear: Whether the UDP socket bind could fail silently and still make `DeviceNames.Count > 0` return true (answer: yes, if `SnmpTrapListenerService.ExecuteAsync` throws on bind, the `DeviceChannelManager` channels still exist).
   - Recommendation: Use `ISchedulerFactory.GetScheduler().IsStarted` AND `DeviceNames.Count > 0` as the readiness condition. If `ExecuteAsync` fails on bind, the host will propagate the exception and the whole pod fails — K8s readiness probe failure is moot at that point.

3. **Dockerfile Not Yet Created for SnmpCollector**
   - What we know: Only one Dockerfile exists (for Simetra). SnmpCollector has no Dockerfile.
   - What's unclear: Whether Phase 8 includes creating a Dockerfile or if that's deferred.
   - Recommendation: Create a Dockerfile for SnmpCollector in Phase 8 (required for K8s deployment with health probes). Base it on Simetra's with `aspnet:9.0-bookworm-slim` as the runtime image.

---

## Sources

### Primary (HIGH confidence)
- `src/Simetra/Lifecycle/GracefulShutdownService.cs` — Reference implementation, directly verified
- `src/Simetra/HealthChecks/StartupHealthCheck.cs`, `ReadinessHealthCheck.cs`, `LivenessHealthCheck.cs` — Reference implementations
- `src/Simetra/Pipeline/ILivenessVectorService.cs`, `LivenessVectorService.cs` — Reference implementations
- `src/Simetra/Pipeline/IJobIntervalRegistry.cs`, `JobIntervalRegistry.cs` — Reference implementations
- `src/Simetra/Configuration/LivenessOptions.cs` — Reference configuration
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` — AddScheduling, AddSimetraHealthChecks, AddSimetraLifecycle patterns
- `src/Simetra/Program.cs` — WebApplication, MapHealthChecks pattern
- `deploy/k8s/deployment.yaml` — K8s probe configuration (initialDelaySeconds, periodSeconds, failureThreshold)
- [Microsoft Learn: Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0) — MapHealthChecks API
- [Microsoft Learn: App health checks in C# - .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostic-health-checks) — Generic Host AddHealthChecks, no HTTP without FrameworkReference

### Secondary (MEDIUM confidence)
- [ASP.NET Core Quartz.NET integration docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/aspnet-core-integration.html) — Standby vs Shutdown semantics; WaitForJobsToComplete
- [Microsoft Learn: Worker Services - .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers) — Generic Host + WebApplication distinction

### Tertiary (LOW confidence)
- None — all critical claims verified via reference implementation or official docs.

---

## Metadata

**Confidence breakdown:**
- Graceful shutdown pattern: HIGH — Simetra reference implementation verified line by line
- Health check implementation: HIGH — Simetra reference + official docs verified
- HTTP transport (FrameworkReference approach): HIGH — official docs + Simetra Dockerfile pattern confirmed
- Drain mechanism (WaitForDrainAsync): HIGH — Simetra reference verified; Channel.Reader.Completion is official API
- Liveness vector and job interval registry: HIGH — Simetra reference verified
- K8s probe configuration values: HIGH — directly from Simetra deployment.yaml

**Research date:** 2026-03-05
**Valid until:** 2026-06-05 (stable APIs — .NET 9, Quartz 3.15.x; no fast-moving dependencies)
