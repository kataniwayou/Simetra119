# Project Milestones: SNMP Monitoring System

## v2.5 Tenant Metrics Approach Modification (Shipped: 2026-03-23)

**Delivered:** Per-tenant metrics refactored from counters to percentage gauges, EvaluateTenant redesigned with gather-then-decide flow, resolved metric flipped to measure violations, dashboard and 7 E2E scenarios updated.

**Phases completed:** 76-81 (8 plans total)

**Key accomplishments:**
- 6 percentage gauges replacing 6 counters with 0-100% values per cycle
- Gather-then-decide EvaluateTenant flow — dispatch only when state = Unresolved
- Resolved metric direction flipped (higher = worse, consistent with evaluate)
- Dashboard columns with (%) suffix, direct gauge PromQL queries
- 7 E2E scenarios (107-113) including 50% partial percentage verification
- 479 unit tests, 113 E2E scenarios total

**Stats:**
- 6 phases, 8 plans
- Timeline: 1 day (2026-03-23)

**Git range:** `feat(76-01)` → `feat(81-01)`

**What's next:** To be determined — `/gsd:new-milestone`

---

## v2.4 Tenant Vector Metrics (Shipped: 2026-03-23)

**Delivered:** Per-tenant internal evaluation state exposed as 8 OTel instruments on a third meter exporting on all instances, surfaced in a 13-column operations dashboard table with state color mapping, per-cycle integer counters, P99 histogram, and trend arrows — verified end-to-end by 6 new E2E scenarios.

**Phases completed:** 72-75 (8 plans total)

**Key accomplishments:**
- TenantMetricService — 8 OTel instruments (6 counters, 1 gauge, 1 histogram) on SnmpCollector.Tenant meter
- EvaluateTenant instrumented with RecordAndReturn pattern, counting helpers, per-cycle batched metrics at exit
- Operations dashboard Tenant Status table — 13 columns, state color mapping, trend arrows, increase()[30s] counters
- 6 E2E scenarios (107-112) proving instrument→Prometheus pipeline across all 4 evaluation paths
- All-instances export verified — followers export tenant metrics while snmp_gauge remains leader-gated

**Stats:**
- 17 files changed (1,846 insertions, 451 deletions)
- 4 phases, 8 plans
- 6 new E2E scenarios (107-112), 112 total
- 475 unit tests passing
- Timeline: 1 day (2026-03-23)

**Git range:** `feat(72-01)` → `feat(75-03)`

**What's next:** To be determined — `/gsd:new-milestone`

---

## v2.3 Metric Validity & Correctness (Shipped: 2026-03-22)

**Delivered:** Every pipeline counter, command counter, business metric value, metric label, and negative-path assertion validated by E2E scenarios against the live SNMP simulator and Prometheus — proving every metric means what it claims to measure.

**Phases completed:** 66-71 (14 plans total, including 2 gap closure)

**Key accomplishments:**
- 38 new E2E scenarios (69-106) validating all 37 metric instruments
- Pipeline counter validity: published/handled/rejected/errors proven accurate with exact delta assertions
- Command counter semantics clarified: dispatched = decision, suppressed = prevention, failed = execution error
- Business metric values proven zero-transformation: raw SNMP values arrive exactly in Prometheus
- All 7 label dimensions verified: source (poll/trap/command/synthetic), snmp_type, resolved_name, device_name
- Negative proofs: heartbeat suppression, unmapped OID absence, bad-community rejection, follower export gating
- Infrastructure: assert_delta_eq/ge helpers, sort -V for 100+ scenarios, simulator scale-down for reachability testing

**Stats:**
- 88 files changed (8,641 insertions, 71 deletions)
- 6 phases, 14 plans (including 2 gap closure for CCV-03/04)
- 1 day (2026-03-22)
- 38 requirements, 38 satisfied, 0 dropped

**Git range:** `v2.2` → `v2.3`

**What's next:** TBD — next milestone planning

---

## v2.2 Progressive E2E Snapshot Suite (Shipped: 2026-03-22)

**Delivered:** Progressive 3-stage E2E test suite validating every SnapshotJob evaluation state (single tenant), proving two-tenant independence, and exercising all 7 advance gate combinations (4-tenant 2-group fixture) -- each stage gated on the previous passing.

**Phases completed:** 62-65 (8 plans total, 3 quick tasks)

**Key accomplishments:**
- 16 new PSS scenario scripts (53-68) producing 52 test results across 3 progressive stages
- Single-tenant fixture proving all 6 evaluation states: Not Ready, Stale, Resolved, Unresolved, Healthy, Suppressed
- Two-tenant fixture proving per-tenant evaluation independence (T1 state does not affect T2)
- Four-tenant 2-group fixture testing all 7 advance gate combinations (3 pass + 4 block)
- Stage gating infrastructure: FAIL_COUNT gates prevent cascading execution of later stages
- Runner stabilization: stale filename fixes, cleanup traps, --since alignment, standalone report categories

**Stats:**
- 99 files changed (8,922 insertions, 1,167 deletions)
- 8,478 LOC C# source + 12,166 LOC C# tests + 7,532 LOC bash E2E + 1,338 LOC Python simulators
- 4 phases, 8 plans, 3 quick tasks (082-084)
- 3 days (2026-03-20 → 2026-03-22)
- 462 unit tests passing, 68 PSS E2E scenario scripts

**Git range:** `v2.1` → `v2.2`

**What's next:** TBD — next milestone planning

---

## v2.1 E2E Tenant Evaluation Tests (Shipped: 2026-03-20)

**Delivered:** Comprehensive E2E test suite validating every path through the SnapshotJob 4-tier evaluation tree and priority group advance gate, using an HTTP-controlled SNMP simulator with per-OID runtime control and a 4-tenant 2-group fixture.

**Phases completed:** 51-61 (26 plans total, 4 quick tasks)

**Key accomplishments:**
- HTTP-controlled E2E simulator with 24 OIDs, per-OID override endpoints, and named scenario switching
- 52 E2E scenario scripts across 6 categories producing 113 test results
- 4-tenant 2-group fixture testing all advance gate combinations (3 pass + 4 block)
- Tenant validation hardening: 8 new validation checks with per-entry skip semantics
- Deterministic watcher startup order: OidMap -> Devices -> CommandMap -> Tenants
- Advance gate bug fix: tier=4 always returns Unresolved (suppressed commands no longer pass gate)
- Readiness window replacing sentinel samples: grace = TimeSeriesSize x IntervalSeconds x GraceMultiplier

**Stats:**
- 114 commits in milestone range
- 8,471 LOC C# source + 12,166 LOC C# tests + 1,338 LOC Python simulators + 5,088 LOC bash E2E
- 11 phases, 26 plans, 4 quick tasks (077-080)
- 3 days (2026-03-17 -> 2026-03-20)
- 462 unit tests passing, 113 E2E tests (101 passing)

**Git range:** `v2.0` -> `v2.1`

**What's next:** TBD — next milestone planning

---

## v2.0 Tenant Evaluation & Control (Shipped: 2026-03-17)

**Delivered:** SnapshotJob evaluates tenants by priority with 4-tier logic (staleness, resolved thresholds, evaluate thresholds, command dispatch) and issues SNMP SET commands through a Channel-backed worker with leader gate and suppression cache. Label rename: metric_name -> resolved_name across all instruments and dashboards.

**Phases completed:** 45-50 (13 plans total, 9 quick tasks)

**Key accomplishments:**
- SnapshotJob 4-tier evaluation with priority group traversal (parallel within group, sequential across)
- CommandWorkerService: Channel-backed SNMP SET execution with leader gate, Stopwatch logging, response dispatch through full MediatR pipeline
- ISuppressionCache: per-tenant suppression window with lazy TTL expiry
- 3 command pipeline counters (sent/failed/suppressed) + snapshot cycle duration histogram
- MetricSlotHolder sentinel timestamp + Range validation (GraceMultiplier 2-5, TimeoutMultiplier 0.1-0.9)
- Label rename: metric_name -> resolved_name across all instruments and dashboards

**Stats:**
- 60 files changed (3,959 insertions, 372 deletions)
- 8,163 LOC C# source + 11,244 LOC C# tests
- 6 phases, 13 plans, 9 quick tasks
- 2 days (2026-03-16 -> 2026-03-17)
- 424 tests passing

**Git range:** `v1.10` -> `v2.0`

**What's next:** TBD — next milestone planning

---

## v1.10 Heartbeat Refactor & Pipeline Liveness (Shipped: 2026-03-15)

**Delivered:** Removed hardcoded heartbeat special cases (-115 lines) and added pipeline-arrival liveness detection proving the full MediatR chain is working, with two independent liveness layers (job completion + pipeline arrival).

**Phases completed:** 43-44 (3 plans total)

**Key accomplishments:**
- Removed hardcoded heartbeat tenant from TenantVectorRegistry.Reload
- Removed heartbeat bypass from TenantVectorFanOutBehavior (zero special cases)
- IHeartbeatLivenessService with volatile long lock-free stamping
- LivenessHealthCheck staleness: IntervalSeconds × GraceMultiplier (30s)
- 6 new unit tests

**Stats:**
- 2 phases, 3 plans
- 1 day (2026-03-15)
- 338 unit tests passing

**What's next:** TBD — runtime threshold evaluation, metric staleness detection

---

## v1.9 Metric Threshold Structure & Validation (Shipped: 2026-03-15)

**Delivered:** Tenant metric entries can carry an optional Threshold (Min/Max) validated at load time, with GraceMultiplier on device poll groups resolved to tenant holders for future staleness detection.

**Phases completed:** 41-42 (3 plans total, 1 quick task)

**Key accomplishments:**
- ThresholdOptions sealed class with Min (double?) and Max (double?)
- Min > Max validation in ValidateAndBuildTenants (check 7)
- GraceMultiplier on PollOptions, resolved from device poll group alongside IntervalSeconds
- Example thresholds in all 3 tenant config files
- 6 new unit tests + 4 quick task tests

**Stats:**
- 16 files changed (416 insertions, 18 deletions)
- 2 phases, 3 plans, 1 quick task
- 1 day (2026-03-15)
- 336 unit tests passing

**Git range:** `e8fa603` → `d5cc6e7`

**What's next:** TBD — runtime threshold evaluation, heartbeat refactor

---

## v1.8 Combined Metrics (Shipped: 2026-03-15)

**Delivered:** Poll groups can compute aggregate metrics (sum/subtract/absDiff/mean) from individual SNMP GET responses and dispatch them as synthetic gauges through the full MediatR pipeline to Prometheus with `source="synthetic"`.

**Phases completed:** 37-40 (4 plans total, 2 quick tasks)

**Key accomplishments:**
- AggregatedMetricName + Aggregator on PollOptions with 5-rule load-time validation
- SnmpSource.Synthetic enum + OidResolutionBehavior bypass guard (3 lines)
- DispatchAggregatedMetricAsync with 4 aggregation functions and all-or-nothing guard
- snmp.aggregated.computed pipeline counter + operations dashboard panel
- 37 new unit tests across 4 phases

**Stats:**
- 33 files changed (1,462 insertions, 472 deletions)
- 4 phases, 4 plans, 2 quick tasks
- 1 day (2026-03-15)
- 326 unit tests passing

**Git range:** `81ec507` → `e20305d`

**What's next:** TBD — next milestone planning

---

## v1.7 Configuration Consistency & Tenant Commands (Shipped: 2026-03-15)

**Delivered:** CommunityString as explicit device identifier, self-describing tenant entries with Role and Commands, per-entry validation with TEN-13 completeness gate, architectural consistency refactor (watcher-validates-registry-stores pattern for all 4 watchers), and config file naming convention alignment.

**Phases completed:** 33-36 (8 plans total, 1 quick task)

**Key accomplishments:**
- DeviceOptions.Name replaced by CommunityString; DeviceInfo.Name derived via TryExtractDeviceName at load time
- CommandSlotOptions data model (Ip, Port, CommandName, Value, ValueType) for future SNMP SET
- MetricSlotOptions.Role (Evaluate/Resolved) with TEN-13 tenant completeness gate
- Per-entry skip validation: CommunityString format, Role, ValueType, MetricName resolution, IP+Port existence, zero-OID poll groups
- Watcher-validates-registry-stores architecture: all 4 watchers validate, all 4 registries are pure FrozenDictionary stores
- Config file renames: tenants.json, oid_metric_map.json, oid_command_map.json

**Stats:**
- 56 files changed (2,175 insertions, 1,017 deletions)
- 6,920 LOC C# source + 7,785 LOC tests
- 4 phases, 8 plans, 1 quick task
- 2 days (2026-03-14 → 2026-03-15)
- 286 unit tests passing

**Git range:** `95e28b5` → `7e6a53e`

**What's next:** TBD — next milestone planning

---

## v1.4 E2E System Verification (Shipped: 2026-03-09)

**Delivered:** Full E2E test harness proving the SNMP-to-Prometheus pipeline works correctly under normal operation, configuration mutations, watcher resilience, and edge cases -- 27 scenarios producing 33 test results with a categorized Markdown report.

**Phases completed:** 20-24 (11 plans total)

**Key accomplishments:**
- Dedicated pysnmp E2E test simulator with 9 OIDs (7 mapped, 2 unmapped) and dual trap loops
- Bash test runner with poll-until-satisfied, delta assertions, and ConfigMap snapshot/restore
- All 10 pipeline counters verified via Prometheus delta queries
- Business metrics, unknown OID classification, and trap-originated metrics verified
- OID rename/remove/add and device add/remove/modify mutations verified at runtime
- ConfigMap watcher resilience: invalid JSON handling, log verification, reconnection observation

**Stats:**
- 52 files created/modified (~3,255 insertions)
- ~1,848 LOC bash + python E2E test infrastructure
- 5 phases, 11 plans
- 1 day (2026-03-09)
- 24/24 requirements satisfied, 8/8 integration checks, 2/2 E2E flows

**Git range:** `v1.3` → `v1.4`

**What's next:** TBD — next milestone planning

---

## v1.3 Grafana Dashboards (Shipped: 2026-03-09)

**Delivered:** Two purpose-built Grafana dashboard JSON files — an operations dashboard for pipeline health and pod observability, and a business dashboard with device-agnostic gauge and info metric tables with cascading filters, trend arrows, and copyable PromQL columns.

**Phases completed:** 18-19 (2 plans total, 9 quick tasks)

**Key accomplishments:**
- Operations dashboard: pod identity table, 11 pipeline counter panels, 6 .NET runtime panels, all filtered by host
- Business dashboard: gauge and info metric tables with 3 cascading filters (Host->Pod->Device)
- Trend column with delta-driven colored arrows showing value changes
- PromQL column with copyable query strings including host/pod labels
- Cell inspect enabled for full content viewing
- 9 quick tasks for iterative dashboard refinements

**Stats:**
- 53 files changed (7,379 insertions, 355 deletions)
- 2 phases, 2 plans, 9 quick tasks
- 2 days (2026-03-08 → 2026-03-09)
- 10/10 requirements satisfied
- 5/5 E2E flows verified

**Git range:** `v1.2` → `v1.3`

**What's next:** TBD — next milestone planning

---

## v1.2 Operational Enhancements (Shipped: 2026-03-08)

**Delivered:** K8s API watch for ConfigMap hot-reload with sub-second event delivery, DynamicPollScheduler for live device/poll reconfiguration, and full live UAT of 13 ConfigMap scenarios + watch reconnection against 3-replica cluster.

**Phases completed:** 15-16 (8 plans total, 5 quick tasks)

**Key accomplishments:**
- K8s API watch replaces file-based hot-reload — sub-second ConfigMap change detection
- Split OidMapWatcherService + DeviceWatcherService with independent reload locks
- DynamicPollScheduler reconciles Quartz jobs on device config changes (add/remove/reschedule)
- Full live UAT: 13 ConfigMap scenarios + watch reconnection verified against 3-replica cluster
- Operational cleanup: SiteOptions→PodIdentityOptions, removed redundant host/pod tags, IsHeartbeat flag

**Stats:**
- 30 files modified (2,207 insertions, 131 deletions)
- 4,937 LOC C# source + 4,318 LOC tests + 783 LOC Python simulators
- 2 phases, 8 plans, 5 quick tasks
- 1 day (2026-03-08)
- 138 tests passing
- 4/4 requirements satisfied

**Git range:** `v1.1` → `v1.2`

**What's next:** TBD — v2.0 planning (Grafana dashboards, SNMP table walk, alerting rules)

---

## v1.1 Device Simulation (Shipped: 2026-03-08)

**Delivered:** OID maps for OBP (24 OIDs) and NPB (68 OIDs) with JSONC documentation, realistic SNMP simulators with trap generation, and full K8s E2E integration with devices.json poll configuration.

**Phases completed:** 11-14 (10 plans total)

**Key accomplishments:**
- OBP OID map (24 entries, 4 links) and NPB OID map (68 entries, 8 ports) with inline documentation
- OBP simulator with power random walk and StateChange traps for all 4 links
- NPB simulator with Counter64 traffic profiles and portLinkChange traps for 6 active ports
- DNS resolution in DeviceRegistry for K8s Service names + optional CommunityString override
- devices.json with 92 poll OIDs across both device types (10s interval)
- E2E verification script validating poll + trap metrics in Prometheus

**Stats:**
- 53 files created/modified
- 4,937 LOC C# source + 4,318 LOC tests + 783 LOC Python simulators
- 4 phases, 10 plans
- 1 day (2026-03-07)
- 138 tests passing
- 14/14 requirements satisfied

**Git range:** `18a0c9d` → `67e046b`

**What's next:** v1.2 Operational Enhancements — K8s API watch, dynamic config reload

---

## v1.0 Foundation (Shipped: 2026-03-07)

**Delivered:** K8s-native SNMP monitoring agent that receives traps, polls devices, resolves OIDs, and pushes metrics through OpenTelemetry to Prometheus with leader-gated export and graceful HA failover.

**Phases completed:** 1-10 (48 plans total, 16 quick tasks)

**Key accomplishments:**
- Full MediatR pipeline with 4-behavior chain dispatching to snmp_gauge and snmp_info instruments
- SNMP trap + poll ingestion with community string convention and Quartz scheduling
- Leader-gated metric export via K8s Lease API with near-instant failover
- Graceful 5-step shutdown and startup/readiness/liveness health probes
- Heartbeat loopback proving pipeline liveness without metric pollution
- Production 3-replica K8s deployment with OTel Collector push pipeline to Prometheus

**Stats:**
- 94 files (70 source + 24 test)
- 7,819 lines of C# (4,077 source + 3,742 test)
- 10 phases, 48 plans, 16 quick tasks
- 3 days from start to ship (Mar 4-7, 2026)
- 121 tests passing, 0 warnings
- 33 K8s manifests

**Git range:** `5163696 docs: initialize project` → `a02ab42 feat: suppress heartbeat metric export`

**What's next:** TBD — production deployment with real NPB/OBP devices, OID map population, Grafana dashboards

---
