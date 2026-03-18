# Phase 57: Deterministic Watcher Startup Order - Research

**Researched:** 2026-03-18
**Domain:** .NET BackgroundService startup sequencing, C# async patterns
**Confidence:** HIGH

## Summary

All four ConfigMap watchers (OidMapWatcherService, DeviceWatcherService, CommandMapWatcherService, TenantVectorWatcherService) follow an identical pattern: their `ExecuteAsync` method performs an initial load via `LoadFromConfigMapAsync` then enters a K8s watch loop. The initial load code is already cleanly separated into a private `LoadFromConfigMapAsync` method in each watcher, making extraction to a public `InitialLoadAsync` straightforward.

The current problem is twofold: (1) In K8s mode, all four watchers start as BackgroundServices with no guaranteed order -- `IHostedService.StartAsync` is called in registration order, but `ExecuteAsync` runs concurrently after the first `await`. (2) In local-dev mode (Program.cs), the command map is loaded AFTER tenants, meaning tenant validation runs against an empty command map. Phase 57 fixes both paths.

**Primary recommendation:** Extract `LoadFromConfigMapAsync` into a public `InitialLoadAsync` on each watcher, call them sequentially in Program.cs before `app.RunAsync()`, and strip the initial-load block from `ExecuteAsync` so it only runs the watch loop.

## Standard Stack

No new libraries needed. This phase is purely a refactoring of existing code using standard .NET patterns.

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Hosting | existing | BackgroundService base class | Already in use |
| System.Diagnostics.Stopwatch | BCL | Startup timing | Zero-allocation, high-resolution timer |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Stopwatch per-watcher | DateTimeOffset.UtcNow | Stopwatch is higher resolution and standard for elapsed timing |
| Sequential calls in Program.cs | IHostedLifecycleService.StartingAsync | StartingAsync runs before all StartAsync calls but still has no inter-service ordering; sequential explicit calls are simpler and deterministic |
| Common base class | Keep each watcher independent | Base class adds coupling; all 4 watchers are similar but not identical (different dependencies, different HandleConfigMapChanged logic). Keep independent. |

## Architecture Patterns

### Current ExecuteAsync Pattern (identical in all 4 watchers)
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // BLOCK 1: Initial load (lines ~57-70 in each watcher)
    try
    {
        await LoadFromConfigMapAsync(stoppingToken).ConfigureAwait(false);
        _logger.LogInformation("...initial load complete...");
    }
    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
    {
        _logger.LogError(ex, "...initial load failed -- will retry via watch loop");
    }

    // BLOCK 2: Watch loop (lines ~72+ in each watcher)
    while (!stoppingToken.IsCancellationRequested) { ... }
}
```

### Target Pattern After Refactor
```csharp
/// <summary>
/// Performs initial ConfigMap load. Called by Program.cs during startup sequencing.
/// Throws on failure (crash-the-pod semantics).
/// </summary>
public async Task InitialLoadAsync(CancellationToken ct)
{
    await LoadFromConfigMapAsync(ct).ConfigureAwait(false);
    _logger.LogInformation("OidMapWatcher initial load complete ({EntryCount} entries)", _count);
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // Watch loop only -- initial load already done by Program.cs
    while (!stoppingToken.IsCancellationRequested) { ... }
}
```

### Startup Sequence in Program.cs (K8s path)
```csharp
// After builder.Build(), before app.RunAsync()
if (k8s.KubernetesClientConfiguration.IsInCluster())
{
    var totalSw = Stopwatch.StartNew();

    var sw = Stopwatch.StartNew();
    var oidWatcher = app.Services.GetRequiredService<OidMapWatcherService>();
    await oidWatcher.InitialLoadAsync(CancellationToken.None);
    var oidTime = sw.Elapsed;
    // ... repeat for each watcher in order ...
}
```

### Startup Sequence in Program.cs (local-dev path)
The local-dev path already does sequential loads in Program.cs but in the WRONG order:
1. OID map (correct)
2. Devices (correct)
3. Tenants (WRONG -- loads before command map)
4. Command map (WRONG -- should be before tenants)

Fix: reorder to OidMap -> Devices -> CommandMap -> Tenants.

### Anti-Patterns to Avoid
- **Resolving watcher from DI then calling LoadFromConfigMapAsync directly:** The private method should stay private. Add a new public `InitialLoadAsync` that wraps it with logging/timing.
- **Catching exceptions in InitialLoadAsync:** The CONTEXT.md says crash immediately on failure, so InitialLoadAsync should let exceptions propagate. The caller in Program.cs should NOT catch them.
- **Adding retry logic:** Per CONTEXT.md decisions, no retries, no backoff. Let K8s restart the pod.
- **Using CancellationToken from host:** Use `CancellationToken.None` for initial load since the host hasn't started yet (no stoppingToken available). Or use a short timeout if desired, but the decision says crash immediately.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Elapsed time measurement | DateTime arithmetic | System.Diagnostics.Stopwatch | Higher resolution, not affected by clock drift |
| Service ordering framework | Custom IHostedService ordering | Explicit sequential calls before RunAsync | Simpler, more explicit, easier to debug |
| Retry logic | Polly/custom retry | Nothing -- crash and let K8s restart | Per CONTEXT.md decision |

## Common Pitfalls

### Pitfall 1: BackgroundService.ExecuteAsync Concurrent Start
**What goes wrong:** BackgroundService.StartAsync queues ExecuteAsync but does NOT await it to completion. It returns after the first `await` inside ExecuteAsync, allowing the host to call StartAsync on the next service. All watchers start concurrently.
**Why it happens:** This is by design in .NET -- BackgroundService is for long-running background work.
**How to avoid:** Move initial load OUT of ExecuteAsync into a separate method called explicitly before RunAsync.
**Warning signs:** Tenant validation logs showing "MetricName not found in OID map" on fresh pod start.

### Pitfall 2: Local-Dev Command Map Load Order
**What goes wrong:** In the current Program.cs local-dev block, command map loads AFTER tenants (lines 129-141 vs 107-127). This means tenant validation runs with an empty command map, causing all command entries to be skipped.
**Why it happens:** Command map was added later (Phase 32) and appended to the end of the local-dev block.
**How to avoid:** Reorder local-dev block to match the K8s startup sequence: OidMap -> Devices -> CommandMap -> Tenants.
**Warning signs:** All tenant commands showing "CommandName not found in command map" errors in local dev.

### Pitfall 3: Entry Count for Log Summary
**What goes wrong:** The CONTEXT.md requires per-watcher INFO log with entry counts (e.g., "OidMap=112"). But the count is held inside the service (OidMapService, DeviceRegistry, etc.), not returned by LoadFromConfigMapAsync.
**Why it happens:** LoadFromConfigMapAsync currently returns void (Task).
**How to avoid:** Either (a) have InitialLoadAsync return the count, (b) query the service after load for its count, or (c) have InitialLoadAsync log the count itself. Option (c) is cleanest -- each watcher already logs entry counts after reload.
**Warning signs:** Missing entry counts in the summary log line.

### Pitfall 4: DI Registration Must Stay as Singleton + AddHostedService
**What goes wrong:** If watchers are registered only as `AddHostedService<T>()` (not as singletons first), Program.cs cannot resolve them via `GetRequiredService<T>()` to call InitialLoadAsync.
**Why it happens:** `AddHostedService<T>()` registers as IHostedService, not as the concrete type.
**How to avoid:** The current code already uses the correct pattern: `AddSingleton<T>()` then `AddHostedService(sp => sp.GetRequiredService<T>())`. This must be preserved.
**Warning signs:** "No service for type 'OidMapWatcherService'" at startup.

### Pitfall 5: ExecuteAsync Must Not Duplicate Initial Load
**What goes wrong:** If the initial-load try/catch block is left in ExecuteAsync after adding InitialLoadAsync, the watcher loads twice on startup.
**Why it happens:** Incomplete refactoring -- forgetting to remove the old code.
**How to avoid:** Delete the entire initial-load try/catch block from ExecuteAsync, leaving only the watch loop.
**Warning signs:** Double "initial load complete" log lines.

## Code Examples

### InitialLoadAsync Method (OidMapWatcherService pattern)
```csharp
// Source: refactored from existing ExecuteAsync initial-load block
/// <summary>
/// Performs the initial ConfigMap read and applies configuration.
/// Called by Program.cs during deterministic startup sequencing.
/// Throws on failure — caller should let exception propagate to crash the pod.
/// </summary>
public async Task InitialLoadAsync(CancellationToken ct)
{
    await LoadFromConfigMapAsync(ct).ConfigureAwait(false);
    _logger.LogInformation(
        "OidMapWatcher initial load complete ({EntryCount} entries)",
        _oidMapService.Count);
}
```

### Startup Sequence in Program.cs (K8s path)
```csharp
// Source: new code for Program.cs, after app = builder.Build() and before app.RunAsync()
if (k8s.KubernetesClientConfiguration.IsInCluster())
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var totalSw = Stopwatch.StartNew();

    var sw = Stopwatch.StartNew();
    var oidWatcher = app.Services.GetRequiredService<OidMapWatcherService>();
    await oidWatcher.InitialLoadAsync(CancellationToken.None);
    var oidTime = sw.Elapsed; var oidCount = /* get count */;

    sw.Restart();
    var deviceWatcher = app.Services.GetRequiredService<DeviceWatcherService>();
    await deviceWatcher.InitialLoadAsync(CancellationToken.None);
    var deviceTime = sw.Elapsed; var deviceCount = /* get count */;

    sw.Restart();
    var commandWatcher = app.Services.GetRequiredService<CommandMapWatcherService>();
    await commandWatcher.InitialLoadAsync(CancellationToken.None);
    var commandTime = sw.Elapsed; var commandCount = /* get count */;

    sw.Restart();
    var tenantWatcher = app.Services.GetRequiredService<TenantVectorWatcherService>();
    await tenantWatcher.InitialLoadAsync(CancellationToken.None);
    var tenantTime = sw.Elapsed; var tenantCount = /* get count */;

    totalSw.Stop();
    logger.LogInformation(
        "Startup sequence: OidMap={OidCount} ({OidTime:F1}s) -> Devices={DeviceCount} ({DeviceTime:F1}s) -> CommandMap={CommandCount} ({CommandTime:F1}s) -> Tenants={TenantCount} ({TenantTime:F1}s) -- total {TotalTime:F1}s",
        oidCount, oidTime.TotalSeconds,
        deviceCount, deviceTime.TotalSeconds,
        commandCount, commandTime.TotalSeconds,
        tenantCount, tenantTime.TotalSeconds,
        totalSw.Elapsed.TotalSeconds);
}
```

### Entry Count Access
Each service needs a way to report its count for the summary log:
- `IOidMapService` -- needs a `Count` property (check if exists)
- `IDeviceRegistry.AllDevices.Count` -- already available
- `ICommandMapService` -- needs a `Count` property (check if exists)
- `TenantVectorRegistry` -- needs a count accessor (check if exists)

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Initial load inside ExecuteAsync | Explicit InitialLoadAsync before host start | Phase 57 | Deterministic ordering, crash-on-failure |
| Swallow initial-load errors, retry via watch | Crash immediately, let K8s restart | Phase 57 | Faster failure detection, cleaner startup |
| Local-dev: wrong load order (tenants before commands) | Correct order: OidMap -> Devices -> CommandMap -> Tenants | Phase 57 | Tenant command validation works in local dev |

## Open Questions

1. **Entry count accessors on services**
   - What we know: DeviceRegistry has `AllDevices` (count available). OidMapService, CommandMapService, TenantVectorRegistry may or may not expose a count property.
   - What's unclear: Need to check if Count/EntryCount properties exist on IOidMapService, ICommandMapService, and TenantVectorRegistry.
   - Recommendation: If missing, add a simple `int Count { get; }` property to each. Alternatively, have InitialLoadAsync return the count.

2. **Whether InitialLoadAsync should return an int (entry count)**
   - What we know: The summary log needs counts from each watcher.
   - Recommendation: Have InitialLoadAsync return `Task<int>` with the entry count. This is cleaner than querying each service separately and avoids needing to add Count properties.

3. **CancellationToken for initial load**
   - What we know: InitialLoadAsync is called before RunAsync, so there's no host-provided stoppingToken.
   - Recommendation: Pass `CancellationToken.None`. If the K8s API call hangs, the pod's liveness probe will eventually kill it. No need for a manual timeout.

## Sources

### Primary (HIGH confidence)
- Direct code inspection of all 4 watcher services, Program.cs, and ServiceCollectionExtensions.cs in the repository
- .NET BackgroundService behavior (ExecuteAsync runs after StartAsync returns at first await) -- well-established framework behavior

### Secondary (MEDIUM confidence)
- None needed -- this is purely an internal refactoring with no external library dependencies

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - no new libraries, pure refactoring of existing code
- Architecture: HIGH - pattern is clear from direct code inspection, all 4 watchers are structurally identical
- Pitfalls: HIGH - identified from actual code analysis (e.g., local-dev wrong order is visible in Program.cs lines 107-141)

**Research date:** 2026-03-18
**Valid until:** 2026-06-18 (stable -- internal refactoring, no external dependency changes)
