# Plan 09-03 Summary: Deployment Guide & Human Verification

**Status:** Complete
**Date:** 2026-03-05

## What Was Done

### Task 1: Create DEPLOY.md
- Created `deploy/k8s/snmp-collector/DEPLOY.md` with 10-step deployment and validation guide
- Covers: prerequisites, monitoring stack, Docker build, K8s deploy, pod verification, log checks, lease verification, Prometheus metrics, leader failover test, teardown
- Commit: `c001558`

### Task 2: Human Verification (Checkpoint)
All 10 DEPLOY.md steps executed and verified:

1. **Simetra removal**: Deleted old Simetra deployment
2. **Monitoring stack**: All manifests applied and restarted successfully
3. **Docker build**: Required 3 fixes before successful build:
   - Added `.dockerignore` to prevent Windows `obj/` leaking into Linux build (commit `9ba79bc`)
   - Added `<Content>` items to .csproj for appsettings.json publish (commit `6c94a20`)
   - Removed broken sed command from Dockerfile (Devices already empty) (commit `0737366`)
4. **K8s deploy**: configmap, deployment, service all created
5. **Pod health**: 3/3 pods READY 1/1, Running, zero restarts
6. **Health probes**: All endpoints returning 200 (startup, ready, live)
7. **Logs**: Structured `[site-lab-k8s|role|correlationId]` format, correlation ID rotation confirmed
8. **Leader election**: Lease `snmp-collector-leader` held by one pod, two followers observing
9. **Prometheus metrics**: `dotnet_*` runtime metrics from 3 distinct `service_instance_id` values (service_name: snmp-collector). Pipeline metrics (snmp_*) not yet visible (expected — no SNMP device traffic in this phase)
10. **Leader failover**: Deleted leader pod, new leader acquired within ~15 seconds, replacement pod came up Running 1/1

## Deviations

Three Docker build issues discovered and fixed during verification:
- `.dockerignore` missing: Windows `obj/project.assets.json` leaked into Linux container, causing NuGet fallback path error
- `appsettings.json` not published: `Microsoft.NET.Sdk` (non-Web) doesn't auto-publish content files
- Broken sed in Dockerfile: Pattern failed on already-empty Devices array, corrupting JSON

## Commits
- `c001558` — feat(09-03): create DEPLOY.md deployment guide
- `9ba79bc` — fix(09-03): add .dockerignore
- `6c94a20` — fix(09-03): include appsettings.json in publish output
- `0737366` — fix(09-03): remove broken sed command from Dockerfile
