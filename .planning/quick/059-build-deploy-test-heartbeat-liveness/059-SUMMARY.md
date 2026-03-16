---
phase: quick
plan: 059
result: complete
started: 2026-03-16T00:00:00Z
completed: 2026-03-16T00:00:00Z
---

## Summary

Created `tests/e2e/test-heartbeat-liveness.sh` — a standalone E2E script that builds, deploys, and validates heartbeat liveness.

## What Was Done

### Task 1: Create test-heartbeat-liveness.sh

Created a comprehensive bash script with 8 steps:

1. **Build** — `docker build -t snmp-collector:local` (skippable via `SKIP_BUILD=true`)
2. **Deploy** — `kubectl rollout restart` + wait for rollout complete (skippable via `SKIP_DEPLOY=true`)
3. **Pod readiness** — polling loop with timeout
4. **Settle wait** — 45s for heartbeat stamps to populate after startup
5. **Port-forward** — single pod on port 18080
6. **Query /healthz/live** — curl with HTTP status capture
7. **Parse and validate** — 6 named assertions via jq:
   - `overall-liveness-healthy` — top-level status is "Healthy"
   - `pipeline-heartbeat-exists` — key present in liveness data
   - `pipeline-heartbeat-not-stale` — stale=false
   - `no-stale-jobs` — zero entries with stale=true
   - `pipeline-heartbeat-age-reasonable` — age < threshold
   - `prometheus-heartbeat-handled` (optional, gated by `CHECK_PROMETHEUS=true`)
8. **Summary** — pass/fail count with failure details

### Patterns Followed

- Sources `lib/common.sh` and `lib/kubectl.sh` (existing E2E infrastructure)
- Uses `record_pass`/`record_fail`/`print_summary` from common.sh
- Cleanup trap for port-forward shutdown
- Graceful failure handling when endpoint is unreachable

## Files Created

| File | Lines | Purpose |
|------|-------|---------|
| `tests/e2e/test-heartbeat-liveness.sh` | ~200 | Standalone heartbeat liveness E2E test |

## Verification

- `bash -n` syntax check: PASS
- 6 named assertions with pass/fail paths
- SKIP_BUILD/SKIP_DEPLOY/CHECK_PROMETHEUS env var flags present
- Sources shared libs, follows existing E2E patterns
