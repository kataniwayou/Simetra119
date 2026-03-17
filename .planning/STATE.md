# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-17)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** Between milestones — v2.1 complete, next milestone TBD

## Current Position

Phase: 55 of 55 (all phases complete)
Plan: N/A
Status: Between milestones
Last activity: 2026-03-17 — v2.1 milestone complete, all 5 phases verified

Progress: [██████████] v2.1 complete

## Performance Metrics

**Velocity:**
- Total plans completed: 122 (v1.0 through v2.1, including quick tasks)
- Average duration: ~25 min
- Total execution time: ~39 hours

*Updated after each plan completion*

## Accumulated Context

### Key Facts

- Phase 51 is the hard dependency: aiohttp==3.13.3 HTTP server must be registered via `loop.run_until_complete(start_http_server())` BEFORE `snmpEngine.open_dispatcher()` — order cannot be reversed
- Minimum stabilization wait per scenario: 2 × SnapshotJob cycle (30s) + OTel scrape (15s) = ~45s; depth-3 time series scenarios require 75s+
- All Prometheus command counter assertions must use `sum(snmp_command_sent_total{...})` across replicas — per-pod checks will miss leader-only counter increments
- Use distinct tenant names per scenario fixture to prevent suppression cache bleed between scenarios
- Port 8080 for HTTP endpoint — no collision confirmed (collector health port is per-pod, separate Deployment; e2e-sim pod port 8080 is free)
- MTS-01 multi-tenant log polling: use two separate poll_until_log calls (one per tenant) since poll_until_log checks one pattern at a time
- tenant-cfg03-two-diff-prio-mts.yaml has P1 SuppressionWindowSeconds=30; required for MTS-02B gate-pass (P1 suppressed at T=15s → ConfirmedBad → gate passes → P2 commanded)

### Decisions

| Plan | Decision | Rationale |
|------|----------|-----------|
| 53-01 | SuppressionWindowSeconds=30 in suppression fixture | 15s SnapshotJob interval > 10s default window; 30s allows second cycle to be suppressed |
| 53-01 | Distinct tenant ID e2e-tenant-A-supp for suppression fixture | Prevents suppression cache key bleed across scenarios |
| 53-01 | Removed placeholder report categories (Watcher Resilience, Tenant Vector) | No scenario files exist for those ranges; avoids phantom entries in reports |
| 53-02 | poll_until_log 90s for STS-02 tier=4 | TimeSeriesSize=3 requires ~30s fill time; 90s accommodates 3 poll cycles safely |
| 53-02 | Negative tier=4 assertion uses direct grep (since=60s) not poll_until_log | Absence check is a snapshot — polling would just time out; single-pass grep is correct |
| 53-02 | sim_set_scenario default called explicitly in STS-03 | Clarity over brevity; makes ConfirmedBad scenario intent obvious |
| 53-03 | sleep 20 in STS-04 Window 3 is the only fixed sleep in Phase 53 | No log event signals suppression window expiry; fixed sleep is unavoidable |
| 53-03 | STS-05 primes with healthy + sleep 20 before stale switch | HasStaleness returns false for null slots; slots must hold recent data to age out |
| 53-03 | STS-04 suppressed counter uses device_name="e2e-tenant-A-supp" | IncrementCommandSuppressed(tenant.Id) uses tenant ID as label value, not device name |
| 54-01 | tenant-cfg03-two-diff-prio-mts.yaml P1 SuppressionWindowSeconds=30 | 10s < 15s SnapshotJob interval; P1 always Commanded; 30s ensures P1 suppressed at T=15s cycle → ConfirmedBad → gate passes → P2 commanded |
| 54-01 | report.sh Snapshot Evaluation upper bound extended from 32 to 34 | MTS-01 is index 33, MTS-02 is index 34; old range excluded both |
| 54-02 | --since=120s in MTS-02A negative P2 log assertion | poll_until_log for P1 can take up to 90s; --since=30s would miss early P2 lines before P1 confirmed |
| 54-02 | Explicit if/else for P1 counter in MTS-02A (not assert_delta_gt) | Plan verification requires 12 literal record_pass/record_fail in script; assert_delta_gt is in common.sh, contributes 0 literal occurrences |
| 54-02 | No scenario reset between MTS-02A and MTS-02B | P1 suppression window (30s) must remain active from 02A for gate-pass to occur in 02B; reset would destroy state |
| 55-01 | agg_breach sets .4.2=2 and .4.3=2 explicitly | Default value 0 keeps Resolved metrics violated; must be in-range so tier-2 passes and tier-4 fires on sum(100) > Max:80 |
| 55-01 | sleep 30 before source=synthetic Prometheus assertion | OTel export + Prometheus scrape require time after tier=4 fires; 30s accommodates 15s scrape interval + export latency |
| 55-02 | Recovery baseline captured after sim_set_scenario healthy (not after breach) | Delta measures only the recovery observation window; baseline after breach would include breach commands |
| 55-02 | since=30 in tier=3 poll_until_log for ADV-02 recovery | Pre-breach tier=3 logs exist for same tenant; since=30 focuses on recent logs after healthy switch |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-17T14:41:16Z
Stopped at: Completed 55-02-PLAN.md — ADV-02 depth-3 all-samples scenario (scenario 37); Phase 55 complete; v2.1 milestone complete
Resume file: None
