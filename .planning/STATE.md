# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-17)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v2.2 Progressive E2E Snapshot Suite

## Current Position

Phase: Not started (defining roadmap)
Plan: —
Status: Defining roadmap
Last activity: 2026-03-20 — Milestone v2.2 started

Progress: [░░░░░░░░░░] v2.2 roadmap pending

## Performance Metrics

**Velocity:**
- Total plans completed: 130 (v1.0 through v2.1, including quick tasks + 56-01 + 56-02 + 57-01 + 57-02 + 59-01 + 59-02)
- Average duration: ~25 min
- Total execution time: ~39.5 hours

*Updated after each plan completion*

## Accumulated Context

### Key Facts

- Phase 51 is the hard dependency: aiohttp==3.13.3 HTTP server must be registered via `loop.run_until_complete(start_http_server())` BEFORE `snmpEngine.open_dispatcher()` — order cannot be reversed
- Minimum stabilization wait per scenario: 2 × SnapshotJob cycle (30s) + OTel scrape (15s) = ~45s; depth-3 time series scenarios require 75s+
- All Prometheus command counter assertions must use `sum(snmp_command_sent_total{...})` across replicas — per-pod checks will miss leader-only counter increments
- Use distinct tenant names per scenario fixture to prevent suppression cache bleed between scenarios
- Port 8080 for HTTP endpoint — no collision confirmed (collector health port is per-pod, separate Deployment; e2e-sim pod port 8080 is free)
- MTS-01 multi-tenant log polling: use two separate poll_until_log calls (one per tenant) since poll_until_log checks one pattern at a time
- tenant-cfg03-two-diff-prio-mts.yaml has P1 SuppressionWindowSeconds=30; required for MTS-02B gate-pass — after 59-01 fix, gate-pass is triggered by sim_set_scenario default (P1 Healthy), not suppression
- ValidateAndBuildTenants now takes snapshotIntervalSeconds and ICommandMapService parameters
- IntervalSeconds=0 after poll group resolution now SKIPS metric (was: preserved as default)
- Threshold Min>Max now SKIPS metric (was: clear threshold + keep metric)
- Duplicate tenant Name, metric, and command detection: skip duplicate, keep first
- CommandName not in command map: Error + skip (was: pass-through)
- Command IP resolution via AllDevices loop (mirrors metric pattern)
- Phase 57: K8s watcher startup order is OidMap -> Devices -> CommandMap -> Tenants via sequential InitialLoadAsync before host starts
- Phase 57: Local-dev load order fixed — command map loads before tenants (was: tenants before command map)

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
| 56-01 | Threshold Min>Max skips metric (not clear+keep) | Consistent with all other Error-level checks that skip the metric via continue |
| 56-01 | IntervalSeconds=0 skips metric | If MetricName passed TEN-05 but can't resolve interval from any poll group, the metric is genuinely broken |
| 56-01 | Passthrough test helpers return real devices | New IntervalSeconds=0 check requires device with poll group for OID-to-interval resolution in tests |
| 56-02 | CommandName not in map -> Error + skip | Consistent with TEN-05 MetricName check; commands skipped until next reload if map loads late |
| 56-02 | Duplicate metric/command key uses resolved IP | Post-resolution key prevents hostname+IP of same device from falsely surviving as distinct |
| 56-02 | Command IP resolution mirrors metric pattern | Identical AllDevices loop + unresolved skip for consistency |
| 57-02 | CancellationToken.None for pre-host InitialLoadAsync | Host stoppingToken not available before app.RunAsync |
| 57-02 | No try/catch around InitialLoadAsync in K8s block | Crash-the-pod semantics — K8s restarts on failure |
| 58-01 | Scenario 33 uses poll_until 45 5 for counter-increment | Mirrors STS-02 30b; SNMP SET + OTel export + scrape requires polling, not immediate snapshot |
| 58-01 | Old 33c (no suppressions while stale) removed | Stale now dispatches commands; suppression-while-stale is no longer the expected behavior |
| 58-02 | STS-06 38b tier=4 log scoped to e2e-tenant-A prefix | Avoids false positives from tier=4 logs in prior scenarios still in pod log buffer |
| 58-02 | STS-06 baseline captured after priming, before stale switch | Delta measures only post-stale dispatches, not priming phase commands |
| 58-03 | STS-07 primes with agg_breach (not healthy) before stale switch | Synthetic holder needs agg_breach OID values to populate timestamps; null slots never age out |
| 58-03 | STS-07 tier=1 log grep scoped to e2e-tenant-agg | Prior tenant tier=1 logs may still be in pod buffer; tenant-scope prevents false positives |
| 59-01 | Tier=4 always returns TierResult.Unresolved | Command intent (reaching tier=4) = device unresolved; suppression/channel-full are operational, not correctness states |
| 59-01 | Advance gate blocks on TierResult.Unresolved (not Commanded) | Fixes bug where suppressed P1 commands allowed P2 to evaluate, defeating priority starvation |
| 59-02 | MTS-02B gate-pass via sim_set_scenario default (P1 Healthy) | Corrected advance gate blocks on Unresolved; gate-pass must come from P1 transitioning to tier=3 Healthy, not from suppression |
| 59-02 | report.sh range |28|40| unchanged | New scenario 40 is at 0-based index 39, already within existing range; no extension required |
| 60-01 | IsReady short-circuits true when ReadSeries().Length > 0 | CopyFrom with real data makes holder immediately ready despite new ConstructedAt |
| 60-01 | ReadinessGrace = TimeSeriesSize * IntervalSeconds * GraceMultiplier | Pure computed property; fields are immutable so no caching needed |
| 60-01 | ConstructedAt uses property initializer (= DateTimeOffset.UtcNow) | Equivalent to constructor body assignment, syntactically cleaner |
| 60-02 | AreAllReady() is a private static method in SnapshotJob | Mirrors HasStaleness/AreAllResolvedViolated pattern; keeps EvaluateTenant readable |
| 60-02 | HasStaleness null-slot: return true (stale) instead of continue | Only reachable after AreAllReady passes; null slot post-readiness = device never responded |
| 60-02 | EvaluateTenant_ResolvedEmptyHolder_SkippedInGate uses IntervalSeconds=0 | Excluded from staleness; empty series skipped in gate; no async needed |
| 61-01 | Per-OID override dict checked before scenario in DynamicInstance.getValue | Enables arbitrary test control without predefined scenario combinations |
| 61-01 | /oid/{oid}/stale registered before /oid/{oid}/{value} | aiohttp route registration order determines handler for overlapping patterns; literal 'stale' must win |
| 61-01 | All 4 tenants use Min:10 for evaluate threshold (not Max:80) | Symmetric with resolved Min:1; value=0 always violated, value>=10 always healthy |
| 61-01 | reset_scenario calls reset_oid_overrides before sim_set_scenario default | Prevents per-OID state leakage across scenarios |
| 61-01 | T1 reuses existing .999.4.x OIDs, no new OID map entries for T1 | e2e_port_utilization/.channel_state/.bypass_status already in OID map |
| 61-02 | SNS-02 BEFORE_SENT baseline captured before sim_set_oid_stale calls | Delta measures only post-stale dispatches; priming-phase commands excluded |
| 61-02 | Negative counter assertion uses snapshot+sleep 10+snapshot (delta=0) | Absence of increment is a point-in-time check; polling would just time out |
| 61-02 | SNS-03 partial violation sub-assertion calls reset_oid_overrides + re-prime + sleep 3 | Flushes prior tier=2 logs before --since=10s absence check |
| 61-02 | tier=4 log patterns include both em-dash and double-dash variants | Defensive against log format differences across environments |
| 61-03 | Gate-block scripts assert G1 state first, then sleep 10 + check G2 absent | Prevents false pass if G2 had stale logs from prior scenarios in pod buffer |
| 61-03 | SNS-B2 uses 5s poll, no sleep-8 | ReadinessGrace = 6s; must catch "not ready" before grace expires (time-based IsReady becomes true after 6s) |
| 61-03 | Negative G2 assertion uses --since=15s (B1/B3/B4) or --since=10s (B2) | Isolates current observation window from prior scenario log residue |

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 070 | Exclude SnmpSource.Command from staleness check | 2026-03-17 | 119275c | [070-exclude-command-source-from-staleness](./quick/070-exclude-command-source-from-staleness/) |
| 071 | Source-aware threshold check — newest only for Trap/Command | 2026-03-17 | 050c0bb | [071-source-aware-threshold-check](./quick/071-source-aware-threshold-check/) |
| 072 | Fix ADV test script counter timing — poll_until instead of immediate snapshot | 2026-03-18 | ff5a88a | [072-fix-adv-test-script-counter-timing](./quick/072-fix-adv-test-script-counter-timing/) |
| 073 | TimeSeriesSize validation must be >= 1 | 2026-03-18 | 385daf2 | [073-timeseriessize-validation-gt-zero](./quick/073-timeseriessize-validation-gt-zero/) |
| 074 | Fix command registry lookup — preserve config address | 2026-03-18 | 9bc5ab1 | [074-command-registry-lookup-config-address](./quick/074-command-registry-lookup-config-address/) |
| 075 | Add error sentinel filter to CommandWorkerService | 2026-03-18 | 34e67eb | [075-command-error-sentinel-filter](./quick/075-command-error-sentinel-filter/) |
| 076 | Snapshot tier fixes — staleness to commands, rename ConfirmedBad | 2026-03-18 | 911dd08 | [076-snapshot-tier-fixes](./quick/076-snapshot-tier-fixes/) |
| 077 | Direct EvaluateTenant for single-tenant groups — skip ThreadPool | 2026-03-19 | 9410015 | [077-snapshot-direct-eval-single-tenant](./quick/077-snapshot-direct-eval-single-tenant/) |
| 079 | SnapCycle debug logging for advance gate diagnosis | 2026-03-20 | 527c41c | [079-snapshot-gate-debug-logging](./quick/079-snapshot-gate-debug-logging/) |
| 080 | Fix configmap restore double-reload (strip annotations, use replace) | 2026-03-20 | baea8ae | [080-fix-configmap-restore-double-reload](./quick/080-fix-configmap-restore-double-reload/) |

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-19T20:15:58Z
Stopped at: Completed 61-03-PLAN.md (advance gate scenarios 46-52 — all 7 gate combinations)
Resume file: None
