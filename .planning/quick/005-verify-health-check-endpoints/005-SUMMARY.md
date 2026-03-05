# Quick Task 005: Verify Health Check Endpoints

**Status:** Complete (verification only, no code changes)
**Date:** 2026-03-05

## Claim 1: Location — Health check endpoints (startup, readiness, liveness)

**CONFIRMED**

| Endpoint | Handler | Tag | Registration |
|----------|---------|-----|-------------|
| `/healthz/startup` | `StartupHealthCheck` | `"startup"` | `Program.cs:34-42` |
| `/healthz/ready` | `ReadinessHealthCheck` | `"ready"` | `Program.cs:44-52` |
| `/healthz/live` | `LivenessHealthCheck` | `"live"` | `Program.cs:54-62` |

All three use tag-filtered `HealthCheckOptions.Predicate` and return 200 (Healthy) / 503 (Unhealthy).

Registered in `ServiceCollectionExtensions.cs:459-462`:
```
.AddCheck<StartupHealthCheck>("startup", tags: new[] { "startup" })
.AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "ready" })
.AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" })
```

## Claim 2: Triggers — K8s probe requests

**CONFIRMED** (architecture matches standard K8s probe model)

These are passive HTTP endpoints — K8s kubelet calls them on its configured schedule. The code does not actively push; it responds to GET requests. The checks execute on each request (no caching).

## Claim 3a: Startup probe — return healthy once pipeline fully wired and first correlationId exists

**PARTIALLY CONFIRMED — does NOT check correlationId**

| What it actually checks | Evidence |
|------------------------|----------|
| `IJobIntervalRegistry.TryGetInterval("correlation", out _)` | `StartupHealthCheck.cs:28` |
| Returns Healthy if "correlation" key is registered | `StartupHealthCheck.cs:30-31` |
| Returns Unhealthy with "Poll definitions not yet registered with Quartz" | `StartupHealthCheck.cs:32` |

**Finding:** The startup probe checks that `AddSnmpScheduling` completed (which populates the job interval registry), NOT that a correlationId exists. However, `Program.cs:29-30` seeds the first correlationId *before* `app.Run()` starts hosted services, so by the time the startup probe could be called, a correlationId already exists. The probe's actual check is **Quartz scheduling wired** (which implies OID map loaded), not correlationId existence.

## Claim 3b: Readiness probe — check all device channels open + Quartz scheduler running

**CONFIRMED**

| Check | Evidence |
|-------|----------|
| `_channels.DeviceNames.Count == 0` → Unhealthy | `ReadinessHealthCheck.cs:30-33` — "No device channels registered" |
| `!scheduler.IsStarted \|\| scheduler.IsShutdown` → Unhealthy | `ReadinessHealthCheck.cs:37-40` — "Quartz scheduler is not running" |
| Both pass → Healthy | `ReadinessHealthCheck.cs:42` |

Note: The claim says "all device channels open" — the check verifies `DeviceNames.Count > 0` (at least one channel exists), not that each individual channel is open/writable. This is a reasonable proxy since channels are created for all configured devices during startup.

## Claim 3c: Liveness probe — check liveness vector stamps relative to intervals

**CONFIRMED**

| Check | Evidence |
|-------|----------|
| Reads all stamps | `LivenessHealthCheck.cs:42` — `_liveness.GetAllStamps()` |
| Looks up each job's interval | `LivenessHealthCheck.cs:48` — `_intervals.TryGetInterval(jobKey, out intervalSeconds)` |
| Computes threshold | `LivenessHealthCheck.cs:51` — `intervalSeconds * graceMultiplier` |
| Age > threshold → stale | `LivenessHealthCheck.cs:54` — `age > threshold` |
| Returns Unhealthy with all stale jobs | `LivenessHealthCheck.cs:72-77` — diagnostic data dictionary per stale job |
| Skips unknown keys | `LivenessHealthCheck.cs:49` — `continue` if interval not registered |

Note: The claim says "all tenant stamps" — the codebase uses "job key" not "tenant". Each job key (e.g., `"correlation"`, `"metric-poll-npb-core-01-0"`) is checked independently.

## Summary

| Claim | Verdict |
|-------|---------|
| 3 endpoints: /healthz/startup, /healthz/ready, /healthz/live | **CONFIRMED** |
| K8s probe triggers (passive HTTP) | **CONFIRMED** |
| Startup: pipeline wired + correlationId exists | **PARTIAL** — checks job registry populated, not correlationId (though correlationId is seeded before probes are reachable) |
| Readiness: device channels + Quartz running | **CONFIRMED** |
| Liveness: stamps recent relative to intervals | **CONFIRMED** |
