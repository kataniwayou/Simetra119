# Plan 08-04 Summary: DI Wiring & Integration

**Status:** Complete
**Duration:** ~4 min
**Commits:**
- `ac35983` feat(08-04): wire DI, WebApplication, health endpoints, and lifecycle
- `eda4952` feat(08-04): add liveness stamps to jobs and create Dockerfile

## What Was Built

### Task 1: DI Wiring and Program.cs
- **Program.cs** — Switched from `Host.CreateApplicationBuilder` to `WebApplication.CreateBuilder`
- **Health endpoints** — `/healthz/startup`, `/healthz/ready`, `/healthz/live` with tag-filtered checks
- **AddSnmpHealthChecks** — Registers StartupHealthCheck (tag: startup), ReadinessHealthCheck (tag: ready), LivenessHealthCheck (tag: live)
- **AddSnmpLifecycle** — Sets HostOptions.ShutdownTimeout=30s, registers GracefulShutdownService LAST
- **AddSnmpPipeline** — Registers ILivenessVectorService as singleton
- **AddSnmpScheduling** — Populates JobIntervalRegistry for correlation + all poll jobs
- **AddSnmpConfiguration** — Binds LivenessOptions with ValidateDataAnnotations + ValidateOnStart

### Task 2: Job Stamps and Dockerfile
- **MetricPollJob** — `_liveness.Stamp(jobKey)` in finally block
- **CorrelationJob** — `_liveness.Stamp(jobKey)` in finally block
- **Dockerfile** — Multi-stage build with aspnet:9.0-bookworm-slim, ports 10162/udp + 8080/tcp
- **MetricPollJobTests** — Updated constructor call with LivenessVectorService parameter

## Deviations

- **FrameworkReference already added:** Plan 08-02 pulled the FrameworkReference forward. No duplicate needed.
- **Test stub WaitForDrainAsync already done:** Plan 08-01 already added WaitForDrainAsync to test stubs. No duplicate needed.
- **Missing usings in Program.cs (auto-fix):** `Microsoft.AspNetCore.Builder` and `Microsoft.AspNetCore.Http` required for `WebApplication` and `StatusCodes` — not available via implicit usings with `Microsoft.NET.Sdk`.
- **MetricPollJobTests constructor update (auto-fix):** New `ILivenessVectorService` parameter required in MetricPollJob constructor.

## Verification

- `dotnet build src/SnmpCollector/SnmpCollector.csproj` — 0 errors, 0 warnings
- `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` — 114 passed, 0 failed

## Files Changed

| File | Action |
|------|--------|
| src/SnmpCollector/Program.cs | Rewritten (WebApplication + health endpoints) |
| src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs | Modified (AddSnmpHealthChecks, AddSnmpLifecycle, registry, liveness) |
| src/SnmpCollector/Jobs/MetricPollJob.cs | Modified (ILivenessVectorService + stamp) |
| src/SnmpCollector/Jobs/CorrelationJob.cs | Modified (ILivenessVectorService + stamp) |
| src/SnmpCollector/Dockerfile | Created |
| tests/SnmpCollector.Tests/Jobs/MetricPollJobTests.cs | Modified (constructor parameter) |
