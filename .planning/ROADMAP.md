# Roadmap: SNMP Monitoring System

## Milestones

- ✅ **v1.0 Foundation** - Phases 1-10 (shipped 2026-03-07)
- ✅ **v1.1 Device Simulation** - Phases 11-14 (shipped 2026-03-08)
- ✅ **v1.2 Operational Enhancements** - Phases 15-16 (shipped 2026-03-08)
- ✅ **v1.3 Grafana Dashboards** - Phases 18-19 (shipped 2026-03-09)
- ✅ **v1.4 E2E System Verification** - Phases 20-24 (shipped 2026-03-09)
- ✅ **v1.5 Priority Vector Data Layer** - Phases 25-29 (shipped 2026-03-10)
- ✅ **v1.6 Organization & Command Map Foundation** - Phases 30-32 (shipped 2026-03-13)
- ✅ **v1.7 Configuration Consistency & Tenant Commands** - Phases 33-36 (shipped 2026-03-15)
- ✅ **v1.8 Combined Metrics** - Phases 37-40 (shipped 2026-03-15)
- ✅ **v1.9 Metric Threshold Structure & Validation** - Phases 41-42 (shipped 2026-03-15)
- ✅ **v1.10 Heartbeat Refactor & Pipeline Liveness** - Phases 43-44 (shipped 2026-03-15)
- ✅ **v2.0 Tenant Evaluation & Control** - Phases 45-50 (shipped 2026-03-17)
- 🚧 **v2.1 E2E Tenant Evaluation Tests** - Phases 51-59 (in progress)

## Phases

<details>
<summary>✅ v1.0 through v1.4 (Phases 1-24) - SHIPPED</summary>

See `.planning/MILESTONES.md` and `.planning/milestones/` for archived details.

</details>

<details>
<summary>✅ v1.5 Priority Vector Data Layer (Phases 25-29) - SHIPPED 2026-03-10</summary>

See `.planning/MILESTONES.md` for details.

</details>

<details>
<summary>✅ v1.6 Organization & Command Map Foundation (Phases 30-32) - SHIPPED 2026-03-13</summary>

#### Phase 30: OID Map Integrity

**Goal**: Operators can detect configuration errors in oidmaps.json at load time, and any code that needs to reverse-resolve a metric name to its OID can do so via a stable interface method
**Depends on**: Nothing (foundation for Phase 31)
**Requirements**: MAP-01, MAP-02, MAP-03, MAP-04
**Success Criteria** (what must be TRUE):
  1. Loading an oidmaps.json with a duplicate OID key produces a structured log warning per duplicate that names the OID, both conflicting metric names, and which name was retained — no silent last-write-wins clobber
  2. Loading an oidmaps.json with a duplicate metric name value produces a structured log warning per duplicate that names the conflicting OIDs — both map entries remain visible in logs
  3. `IOidMapService.ResolveToOid("obp_channel_L1")` returns the correct OID string; `ResolveToOid("no-such-name")` returns null
  4. The reverse index is rebuilt atomically alongside the forward map on every hot-reload — a caller reading immediately after reload sees the new reverse map, never a partial state
  5. Validation runs before `OidMapService.UpdateMap` is called so that duplicate-warning log entries are never followed by contradictory "added" diff entries for the same load event

**Plans:** 2 plans

Plans:
- [x] 30-01-PLAN.md — Reverse index and ResolveToOid (OidMapService + IOidMapService + tests)
- [x] 30-02-PLAN.md — Duplicate OID/name validation in OidMapWatcherService + tests

---

#### Phase 31: Human-Name Device Config

**Goal**: Operators can reference metric names like "obp_channel_L1" instead of raw OID strings in devices.json poll entries, with full replacement of the Oids field by MetricNames and graceful handling of unresolvable names. Restructure oidmaps.json from flat dictionary to array of objects with explicit Oid/MetricName fields.
**Depends on**: Phase 30 (`IOidMapService.ResolveToOid` must exist before DeviceRegistry can call it)
**Requirements**: DEV-01, DEV-02, DEV-03, DEV-04, DEV-05, DEV-06, DEV-07
**Success Criteria** (what must be TRUE):
  1. A devices.json poll entry with `MetricNames: ["obp_channel_L1", "obp_r1_power_L1"]` starts polling both OIDs after device config load — MetricPollJob never receives metric name strings as OID arguments
  2. A MetricNames[] entry that has no match in the current OID map logs a structured warning with device name and metric name, and that entry is silently skipped — the device's other poll entries still register normally
  3. When device config reloads, names are resolved against the current OID map state at that moment (point-in-time resolution)
  4. Reload diff logging includes per-name resolution detail (resolved count, unresolved names listed)

**Plans:** 3 plans

Plans:
- [x] 31-01-PLAN.md — OidMap array restructure + C# model rename (PollOptions, Polls, MetricNames)
- [x] 31-02-PLAN.md — Name resolution in DeviceRegistry via IOidMapService + unit tests
- [x] 31-03-PLAN.md — Config file rewrite (devices.json, K8s ConfigMaps, E2E fixtures, E2E scenarios)

---

#### Phase 32: Command Map Infrastructure

**Goal**: A command map lookup table is operational — operators can load commandmaps.json via ConfigMap hot-reload or local file, and any in-process code can resolve a command name to its SET OID or vice versa
**Depends on**: Nothing (fully independent of Phases 30 and 31)
**Requirements**: CMD-01, CMD-02, CMD-03, CMD-04, CMD-05, CMD-06
**Success Criteria** (what must be TRUE):
  1. `CommandMapService.ResolveCommandOid("set-power-threshold")` returns the correct OID string; an unknown name returns null — both forward (OID → name) and reverse (name → OID) lookups work without throwing
  2. Updating the simetra-commandmaps ConfigMap in K8s triggers a hot-reload within seconds — structured diff log entries appear in pod logs showing added, removed, and changed command entries, plus total entry count
  3. In local dev mode (no K8s cluster), `CommandMapService` is populated from `config/commandmaps.json` on startup — no empty-map silent failure
  4. Loading a commandmaps.json with a duplicate OID key or a duplicate command name produces a structured warning per duplicate — same validation behavior as OID map integrity (Phase 30)
  5. The simetra-commandmaps ConfigMap manifest exists in the deploy directory and is ready to apply to the cluster

**Plans:** 3 plans

Plans:
- [x] 32-01-PLAN.md — ICommandMapService + CommandMapService + commandmaps.json + 12 unit tests
- [x] 32-02-PLAN.md — simetra-commandmaps K8s ConfigMap manifests (standalone + production)
- [x] 32-03-PLAN.md — CommandMapWatcherService + DI wiring + local dev fallback + 10 validation tests

</details>

---

<details>
<summary>✅ v1.7 Configuration Consistency & Tenant Commands (Phases 33-36) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.7-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v1.8 Combined Metrics (Phases 37-40) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.8-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v1.9 Metric Threshold Structure & Validation (Phases 41-42) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.9-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v1.10 Heartbeat Refactor & Pipeline Liveness (Phases 43-44) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.10-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v2.0 Tenant Evaluation & Control (Phases 45-50) - SHIPPED 2026-03-17</summary>

See `.planning/milestones/v2.0-ROADMAP.md` for details.

</details>

---

### 🚧 v2.1 E2E Tenant Evaluation Tests (In Progress)

**Milestone Goal:** Validate the SnapshotJob 4-tier tenant evaluation flow end-to-end via a purpose-built HTTP-controlled simulator and bash scenario scripts, proving every evaluation path observable in Prometheus metrics and pod logs.

#### Phase 51: Simulator HTTP Control Endpoint

**Goal**: The E2E simulator exposes an HTTP control endpoint so test scripts can switch OID return values mid-test without restarting the pod, while all existing E2E scenarios continue to pass unchanged
**Depends on**: Nothing (foundation that unblocks all remaining phases)
**Requirements**: SIM-01, SIM-02, SIM-03, SIM-04, SIM-05, SIM-06
**Success Criteria** (what must be TRUE):
  1. `POST /scenario/{name}` switches the active scenario and all subsequent SNMP GET responses for registered OIDs return the new scenario's values — the switch takes effect within the next poll cycle
  2. `GET /scenario` returns a JSON object with the current active scenario name — verifiable with `curl` from a test script
  3. The simulator starts in the "default" scenario and emits a startup log line naming the active scenario — no unknown initial state
  4. The "default" scenario produces the identical OID values as the pre-HTTP simulator — existing scenario 11 (gauge-labels-e2e-sim) passes without modification
  5. The K8s Deployment and Service expose TCP/8080 and the simulator Dockerfile declares `EXPOSE 8080` — `kubectl port-forward` to 8080 succeeds
**Plans**: 2 plans

- [x] 51-01-PLAN.md — Scenario registry, DynamicInstance refactor, HTTP control endpoint, 6 new OIDs
- [x] 51-02-PLAN.md — Infrastructure: requirements.txt, Dockerfile, K8s deployment/service ports

---

#### Phase 52: Test Library and Config Artifacts

**Goal**: All bash library helpers, port-forward orchestration, tenant fixture YAML files, and OID/command/device config entries required by the scenario scripts exist and are wired into the test runner
**Depends on**: Phase 51 (port-forward to 8080 must be available before simulator.sh helpers are useful)
**Requirements**: CFG-01, CFG-02, CFG-03, CFG-04, CFG-05, CFG-06, CFG-07, INF-01, INF-02, INF-03
**Success Criteria** (what must be TRUE):
  1. `set_scenario <name>`, `reset_scenario`, and `get_active_scenario` bash functions are available to any scenario script that sources `lib/simulator.sh` — each function produces a non-zero exit code on HTTP failure
  2. `poll_until_log <pod> <pattern> <timeout>` iterates all replica pods via `kubectl get pods -o jsonpath` and returns success when any pod log matches the pattern within the timeout — single-pod checks are not used anywhere in the new scripts
  3. All 28 existing E2E scenarios pass without modification after `run-all.sh` sources `simulator.sh` and adds the 8080 port-forward
  4. Tenant fixture YAML files (`tenant-eval-single.yaml`, `tenant-eval-two-same-priority.yaml`, `tenant-eval-two-diff-priority.yaml`, `tenant-eval-aggregate.yaml`) each use distinct tenant IDs and `GraceMultiplier >= 2.0`
  5. OID metric map, OID command map, and device config entries for the evaluate OID, two resolved OIDs, and command response OID are applied to the cluster and visible in the collector's loaded config logs
**Plans**: 3 plans

Plans:
- [x] 52-01-PLAN.md — OID metric map, command map, device config entries, command_trigger simulator scenario
- [x] 52-02-PLAN.md — sim.sh bash library and run-all.sh port-forward integration
- [x] 52-03-PLAN.md — Tenant fixture YAML files (4 topologies: single, two-same-prio, two-diff-prio, aggregate)

---

#### Phase 53: Single-Tenant Scenarios

**Goal**: Five single-tenant scenario scripts validate every branch of the 4-tier evaluation tree — healthy no-action, evaluate violated with command dispatch, resolved gate block, suppression window, and staleness detection — each producing observable evidence in pod logs and Prometheus counters
**Depends on**: Phase 52 (library helpers and fixtures must exist before scenario scripts run)
**Requirements**: STS-01, STS-02, STS-03, STS-04, STS-05
**Success Criteria** (what must be TRUE):
  1. STS-01 (healthy baseline): after stabilization with all OIDs in-range, pod logs contain a tier-3 no-action log line and `sum(snmp_command_sent_total)` delta is zero across the observation window
  2. STS-02 (evaluate violated): after switching to the violated scenario and waiting at least one full poll+OTel cycle (45s), `sum(snmp_command_sent_total)` delta is >= 1 and pod logs contain a tier-4 command dispatch log line
  3. STS-03 (resolved gate): with all resolved metrics out-of-range, pod logs contain a ConfirmedBad tier-2 log line and `sum(snmp_command_sent_total)` delta remains zero — the evaluate tier is never reached
  4. STS-04 (suppression): the first cycle within the suppression window increments `snmp_command_sent_total`; the second cycle within the window increments `snmp_command_suppressed_total` instead; a cycle after the window expires increments `snmp_command_sent_total` again
  5. STS-05 (staleness): after the simulator switches to a stale scenario and one grace period elapses, pod logs contain a tier-1 Stale log line and no command counters increment
**Plans**: 3 plans

Plans:
- [x] 53-01-PLAN.md — Simulator "healthy" scenario, suppression fixture, report category
- [x] 53-02-PLAN.md — STS-01 healthy, STS-02 evaluate violated, STS-03 resolved gate scripts
- [x] 53-03-PLAN.md — STS-04 suppression window, STS-05 staleness detection scripts

---

#### Phase 54: Multi-Tenant Scenarios

**Goal**: Two multi-tenant scenario scripts validate that same-priority tenants are evaluated independently in parallel and that different-priority groups enforce the advance gate — a single non-Healthy result in group 1 blocks group 2
**Depends on**: Phase 53 (single-tenant infrastructure must be stable before multi-tenant complexity is added)
**Requirements**: MTS-01, MTS-02
**Success Criteria** (what must be TRUE):
  1. MTS-01 (same priority): with two tenants at priority 1 each producing distinct tier results, pod logs contain independent tier log lines per tenant name and each tenant's counter deltas match its own scenario outcome — neither tenant's result is attributed to the other
  2. MTS-02 sub-scenario A (advance gate blocked): when tenant-A at priority 1 is Healthy and tenant-B at priority 1 is Commanded, pod logs show group-2 tenants are not evaluated — `sum(snmp_command_sent_total)` delta for group-2 tenants is zero
  3. MTS-02 sub-scenario B (advance gate passed): when all priority-1 tenants are Commanded, pod logs show group-2 tenants are evaluated and their tier results appear in logs — the advance gate allows progression
  4. All counter assertions use `sum(snmp_command_sent_total{...})` without a pod label filter — per-pod counter checks are not used
**Plans**: 2 plans

Plans:
- [x] 54-01-PLAN.md — MTS fixture (P1 SuppressionWindowSeconds=30), report.sh extension, MTS-01 same-priority script
- [x] 54-02-PLAN.md — MTS-02 advance gate script (gate blocked + gate passed in one script)

---

#### Phase 55: Advanced Scenarios

**Goal**: Two advanced scenario scripts validate aggregate metric evaluation (synthetic pipeline feeds threshold check) and time-series depth enforcement (all samples in a depth-3 series must be violated before tier-4 fires), producing the complete v2.1 scenario coverage
**Depends on**: Phase 54 (advanced scenarios are last due to 75s+ timing requirements; multi-tenant must be stable first)
**Requirements**: ADV-01, ADV-02
**Success Criteria** (what must be TRUE):
  1. ADV-01 (aggregate as evaluate): with an AggregatedMetricDefinition configured as the evaluate holder, breaching the aggregate threshold produces a tier-4 command dispatch log line and `sum(snmp_command_sent_total)` delta >= 1 — the Synthetic source reaches the threshold check without being blocked by OID resolution
  2. ADV-02 (time series depth > 1): with `TimeSeriesSize: 3`, a single out-of-range sample does not fire — the tier-4 log line and counter increment appear only after all 3 time series slots contain violated values, requiring at least 3 full poll cycles (minimum 75s wait) before the assertion passes
  3. ADV-02 recovery: switching one sample back in-range while depth-3 series is partially filled causes the tier result to return to Healthy — the all-samples check rejects the partial violation
  4. The complete scenario suite (scenarios 29 and above) produces a categorized pass/fail report consistent with the existing run-all.sh Markdown output format
**Plans**: 2 plans

Plans:
- [x] 55-01-PLAN.md — Simulator agg_breach scenario, report.sh extension, ADV-01 aggregate evaluate script
- [x] 55-02-PLAN.md — ADV-02 depth-3 all-samples time-series script (breach + recovery)

---

#### Phase 56: Tenant Validation Hardening

**Goal**: Fix all validation audit findings — silent failures get logs, inconsistent behaviors get normalized, missing checks get added — so every tenant config problem is observable in pod logs at load time
**Depends on**: Phase 55 (validation gaps discovered during v2.1 E2E testing)
**Requirements**: VAL-01 through VAL-08
**Success Criteria** (what must be TRUE):
  1. IP resolution failure logs a Warning with the unresolved hostname and the metric continues with hostname (existing behavior, now observable)
  2. IntervalSeconds=0 (unresolved from poll group) logs a Warning per metric — operator knows the metric is excluded from staleness
  3. Duplicate tenant Names across the config produce a Warning per duplicate — suppression key collision risk is surfaced
  4. Duplicate metric (same Ip+Port+MetricName) within one tenant produces a Warning — double-write risk is surfaced
  5. SuppressionWindowSeconds <= 0 logs a Warning and clamps to default 60 — prevents every-cycle firing
  6. Threshold Min>Max log uses consistent parameter names (`TenantId`/`Index`) matching all other checks
  7. Comment step numbers are sequential (no duplicate step 6)
  8. Command Ip resolved to device IP (same as metric Ip resolution) — eliminates asymmetry
**Plans**: 2 plans

Plans:
- [x] 56-01-PLAN.md — Point fixes: comment renumber, threshold skip, IP skip, TimeSeriesSize cap, IntervalSeconds skip, suppression -1/0/interval validation + 7 tests
- [x] 56-02-PLAN.md — Structural additions: duplicate tenant/metric/command detection, command IP resolution + skip, CommandName skip + 8 tests

---

#### Phase 57: Deterministic Watcher Startup Order

**Goal**: Enforce sequential initial load order for ConfigMap watchers — OID metric map → devices → command map → tenants — so the tenant watcher always validates against fully populated registries, eliminating false-positive skips from startup race conditions
**Depends on**: Phase 56 (validation hardening added CommandName skip that requires command map to be loaded first)
**Requirements**: WSO-01, WSO-02, WSO-03, WSO-04
**Success Criteria** (what must be TRUE):
  1. OidMapWatcherService completes initial load before DeviceWatcherService starts — OID map has all entries when device config resolves MetricNames
  2. DeviceWatcherService completes initial load before CommandMapWatcherService starts — device registry is populated when command map loads
  3. CommandMapWatcherService completes initial load before TenantVectorWatcherService starts — command map has all entries when tenant config validates CommandNames
  4. TenantVectorWatcherService initial load runs last — all 3 dependency registries are populated, zero false-positive skips from empty registries
  5. After initial load completes, all 4 watchers independently watch for K8s ConfigMap changes — hot-reload is unaffected by startup ordering
  6. Pod startup logs show sequential load order with clear "initial load complete" markers per watcher
  7. All existing unit tests pass — no behavioral change after initial load phase
**Plans**: 2 plans

Plans:
- [x] 57-01-PLAN.md — Extract InitialLoadAsync from all 4 watcher services
- [x] 57-02-PLAN.md — Wire sequential startup in Program.cs (K8s + local-dev paths)

---

#### Phase 58: SnapshotJob Tier Simulation Tests

**Goal**: E2E scenario scripts that validate every SnapshotJob tier path by source type (Poll, Synthetic, Trap, Command) — proving staleness-to-commands, resolved gate, evaluate gate, suppression, and source-aware threshold checks are observable in pod logs and Prometheus counters
**Depends on**: Phase 57 (deterministic startup ensures clean tenant config), Quick tasks 074-076 (command registry fix, staleness fix)
**Success Criteria** (what must be TRUE):
  1. Stale poll data triggers command dispatch (tier=1 → tier=4) — `snmp_command_sent_total` increments, pod logs show "tier=1 stale — skipping to commands"
  2. All resolved violated produces Violated result with no commands — pod logs show tier=2 stop, `snmp_command_sent_total` delta is zero
  3. Resolved not all violated + all evaluate violated produces command dispatch — tier=4 log and counter increment
  4. Suppression window blocks duplicate commands within window, allows after expiry
  5. Source-aware threshold: Poll/Synthetic check all time series samples, Trap/Command check newest only
  6. Advance gate: Commanded tenant blocks lower-priority group evaluation
  7. All existing E2E scenarios (1-37) continue to pass unchanged
**Plans**: 3 plans

Plans:
- [x] 58-01-PLAN.md — Fix scenarios 31/33 log patterns + staleness behavior + report.sh range
- [x] 58-02-PLAN.md — New STS-06 staleness-to-commands scenario (Poll source)
- [x] 58-03-PLAN.md — New STS-07 synthetic staleness-to-commands scenario + report.sh range update

---

#### Phase 59: Advance Gate Fix & Priority Starvation Simulation

**Goal**: Fix the advance gate to block lower-priority groups whenever a tenant reaches tier=4 command intent (regardless of suppression), and prove via E2E simulation that P2 is starved when P1 is in an active command cycle
**Depends on**: Phase 58 (tier simulation tests), Quick task 076 (staleness fix)
**Success Criteria** (what must be TRUE):
  1. When P1 reaches tier=4 and all commands are suppressed (count=0), the advance gate still blocks — P2 is NOT evaluated
  2. P2 is only evaluated when P1's resolved metrics are all violated (tier=2 stop) — meaning the device condition is confirmed
  3. E2E simulation with 1s snapshot interval and 10s suppression window proves P2 starvation pattern: P2 never fires commands while P1 is in active command cycle
  4. Command sent and suppressed counters are observable per tenant in Prometheus
  5. All existing E2E scenarios (1-39) continue to pass unchanged
**Plans**: 0 plans

Plans:
- [ ] TBD (run /gsd:plan-phase 59 to break down)

---

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 25. Config Models | v1.5 | 1/1 | Complete | 2026-03-10 |
| 26. Core Data Types | v1.5 | 2/2 | Complete | 2026-03-10 |
| 27. Pipeline Integration | v1.5 | 2/2 | Complete | 2026-03-10 |
| 28. ConfigMap Watcher | v1.5 | 2/2 | Complete | 2026-03-10 |
| 29. K8s Deployment | v1.5 | 2/2 | Complete | 2026-03-10 |
| 30. OID Map Integrity | v1.6 | 2/2 | Complete | 2026-03-13 |
| 31. Human-Name Device Config | v1.6 | 3/3 | Complete | 2026-03-13 |
| 32. Command Map Infrastructure | v1.6 | 3/3 | Complete | 2026-03-13 |
| 33. Config Model Additions | v1.7 | 2/2 | Complete | 2026-03-14 |
| 34. CommunityString Validation | v1.7 | 2/2 | Complete | 2026-03-14 |
| 35. Watcher-Registry Refactor | v1.7 | 2/2 | Complete | 2026-03-15 |
| 36. Config File Renames | v1.7 | 2/2 | Complete | 2026-03-15 |
| 37. Config and Runtime Models | v1.8 | 1/1 | Complete | 2026-03-15 |
| 38. DeviceWatcherService Validation | v1.8 | 1/1 | Complete | 2026-03-15 |
| 39. Pipeline Bypass Guards | v1.8 | 1/1 | Complete | 2026-03-15 |
| 40. MetricPollJob Aggregate Dispatch | v1.8 | 1/1 | Complete | 2026-03-15 |
| 41. Threshold Model & Holder Storage | v1.9 | 1/1 | Complete | 2026-03-15 |
| 42. Threshold Validation & Config Files | v1.9 | 2/2 | Complete | 2026-03-15 |
| 43. Heartbeat Cleanup | v1.10 | 1/1 | Complete | 2026-03-15 |
| 44. Pipeline Liveness | v1.10 | 2/2 | Complete | 2026-03-15 |
| 45. Structural Prerequisites | v2.0 | 2/2 | Complete | 2026-03-16 |
| 46. Infrastructure Components | v2.0 | 3/3 | Complete | 2026-03-16 |
| 47. CommandWorkerService | v2.0 | 2/2 | Complete | 2026-03-16 |
| 48. SnapshotJob 4-Tier Evaluation | v2.0 | 4/4 | Complete | 2026-03-16 |
| 49. Observability & Dashboard | v2.0 | 1/1 | Complete | 2026-03-16 |
| 50. Label Rename | v2.0 | 1/1 | Complete | 2026-03-16 |
| 51. Simulator HTTP Control Endpoint | v2.1 | 2/2 | Complete | 2026-03-17 |
| 52. Test Library and Config Artifacts | v2.1 | 3/3 | Complete | 2026-03-17 |
| 53. Single-Tenant Scenarios | v2.1 | 3/3 | Complete | 2026-03-17 |
| 54. Multi-Tenant Scenarios | v2.1 | 2/2 | Complete | 2026-03-17 |
| 55. Advanced Scenarios | v2.1 | 2/2 | Complete | 2026-03-17 |
| 56. Tenant Validation Hardening | v2.1 | 2/2 | Complete | 2026-03-18 |
| 57. Deterministic Watcher Startup Order | v2.1 | 2/2 | Complete | 2026-03-18 |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-18 after Phase 58 added*
