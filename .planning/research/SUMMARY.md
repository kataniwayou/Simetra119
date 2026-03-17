# Project Research Summary

**Project:** Simetra119 SNMP Collector — v2.1 E2E Tenant Evaluation Tests
**Domain:** E2E test infrastructure — HTTP-controlled simulator + bash evaluation test scripts
**Researched:** 2026-03-17
**Confidence:** HIGH

## Executive Summary

The v2.1 milestone adds end-to-end validation of the SnapshotJob 4-tier tenant evaluation flow built in v2.0. The system under test is already deployed and functional; this milestone is exclusively test infrastructure. The approach is: (1) extend the existing Python SNMP simulator with an HTTP control endpoint so bash test scripts can switch OID return values mid-test without restarting the pod, and (2) add bash E2E scenario scripts that exercise every evaluation path — healthy, tier-2 resolved gate, tier-3 evaluate violated, tier-4 command dispatch, suppression, staleness, multi-tenant priority groups, time series depth, and aggregate metrics.

The recommended implementation adds one new dependency: `aiohttp==3.13.3`. It embeds a non-blocking HTTP server inside the simulator's existing asyncio event loop using `aiohttp.web.AppRunner` + `TCPSite`. This is the only viable approach because `snmpEngine.open_dispatcher()` is a blocking `loop.run_forever()` call — any HTTP server must be registered as an asyncio task before that call executes. Threading (Flask, stdlib http.server) introduces data-race risk for OID state mutation and was evaluated and rejected. On the bash test layer, zero new tools are needed: `curl`, `kubectl`, and `jq` are already present. A new `lib/simulator.sh` library and a `poll_until_log` function are the only structural additions.

The critical risks are all timing-related: tests must wait long enough for poll cycles to fill the time series, for OTel export to flush to Prometheus, and for the suppression cache to clear between scenarios. Every timing hazard has a known, specific prevention strategy documented in PITFALLS.md. None require changes to the system under test — they are purely test-script discipline issues that can be enforced via code review and documented wait constants.

---

## Key Findings

### Recommended Stack

One new Python package: `aiohttp==3.13.3`. The simulator (`python:3.12-slim`) gains an HTTP control endpoint via `aiohttp.web.AppRunner` + `TCPSite`, started as a coroutine before `snmpEngine.open_dispatcher()`. The `AppRunner`/`TCPSite` startup API completes without blocking the event loop and leaves the HTTP server live inside pysnmp's run loop. The HTTP handler and `DynamicInstance.getValue()` callbacks both execute on the same single-threaded asyncio event loop — a plain Python `dict` for scenario state requires no locks.

For the bash test layer, zero new tools are needed. The existing patterns in `lib/prometheus.sh` (`poll_until`) and `lib/common.sh` (`record_pass/fail`, `assert_delta_gt`, `snapshot_counter`) cover all counter-based assertions. A new `lib/simulator.sh` provides `set_scenario`/`reset_scenario`/`get_active_scenario` wrappers. A `poll_until_log` function (added to `lib/kubectl.sh`) covers log-based tier assertions using `kubectl logs --since` + `grep -F`.

**Core technologies:**
- `aiohttp 3.13.3`: asyncio HTTP server for simulator scenario control — only option that integrates with pysnmp's blocking `open_dispatcher` without threading
- `pysnmp 7.1.22`: unchanged; confirmed current as of 2026-03-17
- `curl` + `kubectl` + `jq`: bash test tooling already present; no additions needed
- `python:3.12-slim`: base image unchanged; aiohttp installs via pre-built wheel with no compiler required

**K8s changes required:**
- Add `EXPOSE 8080` to simulator Dockerfile
- Add TCP/8080 `containerPort` to the Deployment and `port` to the Service
- Add `start_port_forward e2e-simulator 8080 8080` to `run-all.sh`

See `STACK.md` for the complete `requirements.txt` change, Dockerfile diff, K8s Service YAML, and the full `poll_until_log` and `sim_set_scenario` implementation with rationale.

### Expected Features

Nine table-stakes scenarios and three differentiators are fully specified in FEATURES.md, with exact log patterns, counter names, and assertion directions for every scenario.

**Must have (9 table-stakes scenarios):**
- TS-SC-01: Single tenant healthy — tier-3 no-action path, zero commands
- TS-SC-02: Single tenant evaluate violated — tier-4 first command dispatch, `sent_total` delta >= 1
- TS-SC-03: Resolved gate (ConfirmedBad) — tier-2 stops evaluation when all Resolved violated, zero commands
- TS-SC-04: Suppression window — cache blocks re-fire within window, then allows re-fire after expiry
- TS-SC-05: Staleness detection — tier-1 blocks all evaluation when holders exceed age limit
- TS-SC-06: Two tenants same priority — parallel independent evaluation; advance gate fails when one is Healthy
- TS-SC-07: Two tenants different priority — advance gate sub-scenario A (Healthy blocks group-2) and B (all Commanded passes gate)
- TS-SC-08: Time series all-samples check — depth-3 series; one in-range sample recovers to Healthy
- TS-SC-09: Aggregate metric as evaluate holder — Synthetic source staleness and threshold fire path

**Should have (3 differentiators):**
- D-SC-01: Cycle duration histogram sanity check — `histogram_quantile(0.99, ...)` < 1000ms
- D-SC-02: Partial suppression — two commands per tenant, one suppressed mid-window
- D-SC-03: Liveness not broken across full suite — pod remains Running after all scenarios; health endpoint returns 200

**Explicitly do not build:**
- Fixed-sleep assertions (AF-SC-01) — use `poll_until` loop instead
- SET side-effect verification on simulator (AF-SC-02) — covered by CommandWorkerService unit tests
- Production tenant config in E2E scenarios (AF-SC-03) — isolation risk; use `tenant-eval-*.yaml` fixtures only
- ConfirmedBad cascade multi-group scenario (AF-SC-04) — ConfirmedBad does not advance the gate; covered by unit tests

### Architecture Approach

The architecture is additive: the existing single-process Python simulator gains scenario state and an HTTP server; the existing bash test runner sources one new library file and opens one new port-forward. No restructuring of the collector, no new K8s deployments, and no changes to existing test scenarios 01–28.

**Major components:**
1. `SCENARIOS` dict + `_active_scenario` variable (inline in `e2e_simulator.py`) — scenario registry mapping name to per-OID-label values; every scenario defines all 7 mapped OIDs; "default" entry reproduces current static values exactly
2. `make_getter(label)` factory replacing frozen `lambda v=static_value: v` closures — reads `_active_scenario` at GET time; safe due to asyncio single-threaded guarantee
3. `start_http_server()` coroutine (aiohttp AppRunner/TCPSite) — exposes `GET /scenario`, `POST /scenario`, `POST /scenario/reset`; registered via `loop.run_until_complete()` before `open_dispatcher()`
4. `tests/e2e/lib/simulator.sh` — bash wrappers (`set_scenario`, `reset_scenario`, `get_active_scenario`) following the same sourced-library pattern as `kubectl.sh` and `prometheus.sh`
5. `tests/e2e/fixtures/tenant-eval-*.yaml` — complete `simetra-tenants` ConfigMaps per test topology; applied and restored using the save/restore pattern from scenario 28
6. `tests/e2e/scenarios/29-*.sh` and higher — five-phase scripts (SETUP / STABILIZE / ASSERT / TEARDOWN / VERIFY-CLEAN); each script that calls `set_scenario` includes a `trap _cleanup EXIT`

**Build order constraint:** `loop.run_until_complete(start_http_server())` must precede `snmpEngine.open_dispatcher()`. Once `open_dispatcher()` runs, the loop is owned by pysnmp and cannot accept new coroutine registrations.

See `ARCHITECTURE.md` for the complete startup sequence code, full HTTP API spec (request/response shapes, 404 error format), five-phase scenario pattern, and `lib/simulator.sh` implementation.

### Critical Pitfalls

Full pitfall set is in `PITFALLS.md` (14 pitfalls, 6 critical, 5 moderate, 3 minor). The top items for implementation:

1. **asyncio event loop blocking** (P1 — Critical) — A synchronous HTTP handler stalls SNMP GET responses during the handler call, producing tier-1 stale results in the very next SnapshotJob cycle. Prevention: use `aiohttp.web` with async handlers only; no `time.sleep()`, no blocking I/O inside any handler.

2. **Time series fill requirement — asserting too early** (P3 — Critical) — `AreAllEvaluateViolated()` requires all samples in the series to be violated. Minimum wait before asserting: `(TimeSeriesSize * IntervalSeconds) + OTel_export_interval`. For the default depth-1 tenant this is 45s; for depth-3 (TS-SC-08) this is 75s. Using a 30s `poll_until` timeout for depth > 1 produces reliable false failures.

3. **Suppression cache bleeds between scenarios** (P4 — Critical) — `SuppressionCache` is not cleared between test runs. Two scenarios testing the same tenant's command within 60s of each other will see the second command suppressed. Prevention: use distinct tenant IDs per scenario (preferred), or space scenarios 75s+ apart.

4. **OTel cumulative export delay inflates/deflates delta baseline** (P6 — Critical) — The baseline counter snapshot must be taken after at least `2 * OTel_export_interval` (30s) settle time following any ConfigMap apply or pod restart. The correct model is already in scenario 28.

5. **Sentinel value (0) triggers equality thresholds at startup** (P2 — Critical) — `MetricSlotHolder` initializes with `Value=0`. Thresholds where `Min = Max = 0` fire immediately before any real poll data arrives. Prevention: avoid equality-zero thresholds in test configs; use range thresholds where `0` is within the safe range.

6. **Single-pod log check misses tier logs on other replicas** (P9 — Moderate) — SnapshotJob runs on all 3 pods; tier-4 log appears on all 3. Log assertions must iterate all pods via `kubectl get pods -o jsonpath='{.items[*].metadata.name}'`. For command-sent assertions, prefer `sum(snmp_command_sent_total)` over pod log inspection.

---

## Implications for Roadmap

All four research files converge on the same build order. Infrastructure must precede scenarios; single-tenant scenarios must precede multi-tenant; simpler scenarios before timing-sensitive ones. No phase requires external research beyond what is in these files.

### Phase 1: Simulator HTTP Control Endpoint

**Rationale:** Every scenario except TS-SC-01 and TS-SC-02 depends on the ability to switch OID values mid-test. The HTTP endpoint is the hard dependency that unblocks all remaining 7 table-stakes scenarios.

**Delivers:** Modified `e2e_simulator.py` with `SCENARIOS` dict, `_active_scenario`, `make_getter` factory, and aiohttp HTTP server. Updated `requirements.txt` (add `aiohttp==3.13.3`), Dockerfile (`EXPOSE 8080`), and K8s deployment manifest (TCP/8080 on Deployment and Service). Verification gate: existing scenario 11 (`gauge-labels-e2e-sim`) still passes with default OID values unchanged.

**Addresses:** HTTP control endpoint requirement; pre-condition for TS-SC-04, 05, 07, 08, 09

**Avoids:**
- P1 (event loop blocking): must use `AppRunner`/`TCPSite`, async handlers, no threading
- P13 (unknown default state): hardcode `"default"` scenario name at startup; emit startup log line
- P12 (port conflicts): use 8080; confirm no collision with health endpoint before committing

**Research flag:** NONE. Full implementation pattern specified in STACK.md and ARCHITECTURE.md.

---

### Phase 2: Test Library and Runner Setup

**Rationale:** The scenario scripts depend on `simulator.sh` wrappers and the port-forward addition to `run-all.sh`. This phase can run in parallel with Phase 1 or immediately after. All 28 existing scenarios must pass unchanged after this phase.

**Delivers:** `tests/e2e/lib/simulator.sh` with `set_scenario`, `reset_scenario`, `get_active_scenario`. Updated `run-all.sh` (source `simulator.sh`, add port-forward for 8080). `poll_until_log` function added to `lib/kubectl.sh`. Tenant fixture YAML files: `tenant-eval-single.yaml`, `tenant-eval-two-priority.yaml`.

**Addresses:** Test orchestration infrastructure for all TS-SC-* scenarios

**Avoids:**
- P9 (single-pod log check): `poll_until_log` must iterate all pods
- P4 (suppression bleed): fixture files must use distinct tenant IDs per scenario topology
- P14 (GraceMultiplier validation): all fixture YAML must use `GraceMultiplier >= 2.0`

**Research flag:** NONE. Follows established patterns from `lib/prometheus.sh`, `kubectl.sh`, and scenario 28.

---

### Phase 3: Single-Tenant Table-Stakes Scenarios (TS-SC-01 through TS-SC-05)

**Rationale:** These five scenarios cover the complete single-tenant evaluation tree. TS-SC-01 validates the full scaffold before any positive assertion. Each subsequent scenario builds on the prior. Multi-tenant scenarios in Phase 4 require single-tenant scenarios to be stable first.

**Delivers:** Scenario scripts for TS-SC-01 through TS-SC-05. Each uses the five-phase pattern with `trap _cleanup EXIT`. Assertion evidence confirmed for: healthy no-action, command fire, resolved gate block, suppression window (both phases), staleness onset and recovery.

**Addresses:** TS-SC-01 through TS-SC-05 from FEATURES.md

**Avoids:**
- P2 (sentinel equality threshold): all thresholds in fixtures must use range values where `0` is safe
- P3 (time series fill): `poll_until` timeouts must be at least `TimeSeriesSize * 15 + 30` seconds
- P6 (OTel export delay): mandatory 30s settle wait after `kubectl apply` before baseline snapshot
- P10 (scenario switch timing): mandatory 15s sleep after `set_scenario` before `poll_until` loop
- P11 (resolved gate misconfig): Resolved threshold in fixture must be in range for the "healthy" scenario
- P14 (GraceMultiplier): keep `>= 2.0` in all fixture configs

**Research flag:** NONE. All tier-log patterns and counter names verified from `SnapshotJob.cs` and `PipelineMetricService.cs`.

---

### Phase 4: Multi-Tenant Scenarios (TS-SC-06, TS-SC-07)

**Rationale:** Priority group and advance gate behavior requires two tenants at controlled priorities. Build after Phase 3 is stable to avoid confounding multi-tenant failures with single-tenant infrastructure issues.

**Delivers:** Scenario scripts for TS-SC-06 (same priority, independent evaluation) and TS-SC-07 (different priority, advance gate sub-scenarios A and B). Fixture: `tenant-eval-two-priority.yaml`.

**Addresses:** TS-SC-06, TS-SC-07 from FEATURES.md

**Avoids:**
- P5 (per-pod vs cluster-total command counter): use `sum(snmp_command_sent_total)` without pod filter
- P7 (priority gate blocks test tenant): ensure no higher-priority stale tenants during the test window; assign same priority for parallel-evaluation tests
- P4 (suppression bleed between sub-scenarios): TS-SC-07 sub-scenario B reuses same tenant; use distinct suppression keys or space 75s+ between sub-scenarios

**Research flag:** NONE. Advance gate logic (`shouldAdvance` boolean) and group traversal behavior specified in PITFALLS.md P7 from source code.

---

### Phase 5: Time Series and Aggregate Scenarios (TS-SC-08, TS-SC-09)

**Rationale:** TS-SC-08 requires `TimeSeriesSize: 3` and minimum 75s per phase. TS-SC-09 requires the `AggregatedMetricDefinition` pipeline (verified working in scenario 28). Both are slower than earlier scenarios; scheduling them last reduces risk of slow tests blocking earlier scenario development.

**Delivers:** Scenario scripts for TS-SC-08 (three phases: all-violated fires, one in-range recovers, series refills and fires again) and TS-SC-09 (aggregate metric threshold breach via Synthetic source).

**Addresses:** TS-SC-08, TS-SC-09 from FEATURES.md

**Avoids:**
- P3 (time series fill): TS-SC-08 phases A and C each require `3 * 15 + 30 = 75s` minimum `poll_until` timeout; this must be commented in the script
- P8 (ConfigMap reload series truncation): `TimeSeriesSize: 3` must remain constant across all TS-SC-08 fixture variants — do not change it between scenario phases
- P2 (sentinel): with `TimeSeriesSize: 3`, first fire cannot occur until all 3 slots are overwritten; test must wait at least 3 poll cycles before asserting phase A fires

**Research flag:** NONE. `AreAllEvaluateViolated()` and `MetricSlotHolder.CopyFrom()` truncation behavior verified from source.

---

### Phase 6: Differentiators (D-SC-01, D-SC-02, D-SC-03)

**Rationale:** Low-risk additions that strengthen coverage without blocking table-stakes validation. All three reuse Phase 3–5 scaffolding with minimal new test code.

**Delivers:** D-SC-01 Prometheus histogram sanity assertion (single query, low effort). D-SC-02 two-command partial suppression script extending Phase 3 scaffolding. D-SC-03 liveness health check appended to `run-all.sh` summary output.

**Research flag:** NONE.

---

### Phase Ordering Rationale

- Phase 1 before all others: the HTTP endpoint is the blocking dependency for 7 of 9 table-stakes scenarios.
- Phase 2 in parallel with or immediately after Phase 1: library functions are needed before any scenario script runs; fixture YAML can be drafted while Phase 1 is in progress.
- Phase 3 before Phase 4: multi-tenant scenarios build on the same library primitives; timing issues discovered in single-tenant scenarios must be fixed once, before multi-tenant complexity is added.
- Phase 4 before Phase 5: multi-tenant introduces priority group logic; time series depth adds an orthogonal timing dimension; keeping them separate keeps failure diagnosis clean.
- Phase 6 last: differentiators; never on the critical path.
- Within Phase 3, SC-01 must come first (validates scaffold before positive assertions), then SC-02 (first command fire), SC-03, SC-04, SC-05 in dependency order as specified in FEATURES.md.

### Research Flags

No phases require deeper research during planning. All integration points were derived from direct source reads:

- **Phases 1–2:** aiohttp `AppRunner`/`TCPSite` pattern fully specified in STACK.md with code examples; bash library patterns derived directly from `lib/prometheus.sh`, `lib/kubectl.sh`, and `run-all.sh`.
- **Phases 3–6:** All log line patterns, counter names, suppression key format (`{tenantId}:{Ip}:{Port}:{CommandName}`), timing constants, and threshold semantics verified from `SnapshotJob.cs`, `MetricSlotHolder.cs`, `SuppressionCache.cs`, and `PipelineMetricService.cs`.

The one MEDIUM-confidence item (pysnmp `open_dispatcher` calling `loop.run_forever()`) carries no implementation risk: the behavior is directly observable in the existing simulator — trap loop tasks execute inside the `open_dispatcher()` call — and the aiohttp integration pattern handles it correctly by design.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | aiohttp 3.13.3 verified on PyPI 2026-03-17; AppRunner/TCPSite pattern verified against aiohttp docs; pysnmp open_dispatcher loop ownership is MEDIUM (observed behavior, not pysnmp source read) but low implementation risk |
| Features | HIGH | Derived from full source read of SnapshotJob.cs, MetricSlotHolder.cs, SuppressionCache.cs, PipelineMetricService.cs, SnapshotJobTests.cs (40+ unit tests), and all 28 existing E2E scenario scripts |
| Architecture | HIGH | All claims derived from direct code inspection of simulator, K8s manifests, and test library files; no inference |
| Pitfalls | HIGH | All 14 pitfalls sourced from direct code reads; each tied to specific file and line behavior |

**Overall confidence:** HIGH

### Gaps to Address

- **Port selection (8080):** PITFALLS.md P12 notes that 8080 may conflict with other cluster services. STACK.md and ARCHITECTURE.md both specify 8080. Confirm no collision with the collector's health endpoint port in the actual cluster before implementation. If conflict exists, use 9191.

- **Staleness scenario mechanics (TS-SC-05):** The staleness test requires the simulator to stop returning valid SNMP GET responses. FEATURES.md specifies a `stale-start`/`stale-end` scenario pair. The exact mechanism (delayed response via `asyncio.sleep()` in the OID getter to exceed `IntervalSeconds * GraceMultiplier`, or explicit timeout behavior) is not yet designed. The simplest correct approach is a scenario getter that sleeps longer than the grace window so the collector's poll job times out and stops writing to the slot. This is a Phase 1 design decision.

- **OID count sufficiency for multi-metric scenarios:** TS-SC-06 and TS-SC-07 need two tenants with independent Evaluate and Resolved metrics. The 7 currently mapped OIDs may be sufficient (one Evaluate and one Resolved OID per tenant, using existing labels). If not, `simetra-oid-metric-map.yaml` must be extended. Confirm during Phase 2 fixture authoring. Extension pattern is documented in ARCHITECTURE.md.

---

## Sources

### Primary (HIGH confidence — direct source reads)

- `src/SnmpCollector/Jobs/SnapshotJob.cs` — 4-tier evaluation logic, advance gate, tier log lines, liveness stamp
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `ReadSeries()`, sentinel construction, `CopyFrom()` truncation at `TimeSeriesSize`
- `src/SnmpCollector/Pipeline/SuppressionCache.cs` — key format `{tenantId}:{Ip}:{Port}:{CommandName}`, lazy TTL behavior
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — `snmp.command.sent/failed/suppressed`, `snmp.snapshot.cycle_duration_ms`
- `src/SnmpCollector/Services/CommandWorkerService.cs` — leader gate, command sent counter placement on leader only
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — `GraceMultiplier` field, range constraint `[2.0, 5.0]`
- `tests/SnmpCollector.Tests/Jobs/SnapshotJobTests.cs` — 40+ unit tests documenting exact edge case behavior
- `simulators/e2e-sim/e2e_simulator.py` — asyncio event loop structure, 9-OID surface, `DynamicInstance` pattern, no HTTP today
- `simulators/e2e-sim/requirements.txt`, `Dockerfile` — current dependency and base image state
- `deploy/k8s/simulators/e2e-sim-deployment.yaml` — current port structure and probe patterns
- `tests/e2e/run-all.sh`, `lib/common.sh`, `lib/prometheus.sh`, `lib/kubectl.sh` — existing test library
- `tests/e2e/scenarios/28-tenantvector-routing.sh` — save/restore ConfigMap pattern, multi-pod log iteration
- `src/SnmpCollector/config/tenants.json` — tenant config structure and OID name conventions

### Primary (HIGH confidence — official library docs)

- [aiohttp PyPI — v3.13.3](https://pypi.org/project/aiohttp/) — version confirmed, Python 3.12 compatibility confirmed
- [aiohttp Web Server Advanced docs](https://docs.aiohttp.org/en/stable/web_advanced.html) — AppRunner/TCPSite non-blocking startup pattern
- [Python asyncio event loop docs](https://docs.python.org/3/library/asyncio-eventloop.html) — single-threaded execution guarantee

### Secondary (MEDIUM confidence)

- [pysnmp PyPI — v7.1.22](https://pypi.org/project/pysnmp/) — version confirmed current; `open_dispatcher` loop ownership is observed behavior, not pysnmp source read

---

*Research completed: 2026-03-17*
*Ready for roadmap: yes*
