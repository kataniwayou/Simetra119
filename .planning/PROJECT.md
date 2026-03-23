# SNMP Monitoring System

## What This Is

A K8s-native SNMP monitoring agent that receives traps and polls devices, resolves OIDs to human-readable metric names via a flat OID map, and pushes metrics through OpenTelemetry to Prometheus/Grafana. Evaluates tenant health on a 15s cycle through a 4-tier logic tree (staleness, resolved thresholds, evaluate thresholds, command dispatch) and issues SNMP SET commands through a Channel-backed worker with leader gate and suppression cache. Built on C# .NET 9 with MediatR for event routing and Quartz for poll scheduling. Runs as a 3-replica Kubernetes deployment with leader-gated metric export and near-instant failover. Includes OBP and NPB device simulators for development and testing.

## Core Value

Every SNMP OID — from a trap or a poll — gets resolved, typed correctly (gauge/info), and pushed to Prometheus where it's queryable in Grafana within seconds.

## Requirements

### Validated

**v1.0 Foundation (shipped 2026-03-07)**

- MediatR pipeline: 4-behavior chain (Logging -> Exception -> Validation -> OidResolution) with OtelMetricHandler
- SNMP trap listener with community string convention (Simetra.*) and backpressure channel
- Quartz-based SNMP GET polling with per-device configuration and unreachability tracking
- Two metric instruments: snmp_gauge (Integer32, Gauge32, TimeTicks, Counter32, Counter64) and snmp_info (OctetString, IpAddress, OID)
- Flat OID map with hot-reload, "Unknown" fallback for unmapped OIDs
- Pipeline metrics (11 counters) exported by all instances with device_name label
- K8s Lease API leader election with MetricRoleGatedExporter
- Graceful 5-step shutdown (30s budget) with lease release
- Startup/readiness/liveness health probes with per-job staleness detection
- HeartbeatJob loopback proving pipeline liveness (IsHeartbeat flag, suppressed from metric export)
- Label taxonomy: device_name, metric_name, oid, ip, source, snmp_type (host/pod identity via OTel resource attributes)

See `.planning/milestones/v1.0-REQUIREMENTS.md` for full requirement details.

**v1.1 Device Simulation (shipped 2026-03-08)**

- OID map naming convention: `obp_{metric}_L{n}` for OBP, `npb_{metric}` / `npb_port_{metric}_P{n}` for NPB
- OBP OID map: 24 entries (4 links x 6 metrics) with JSONC documentation
- NPB OID map: 68 entries (4 system + 8 ports x 8 metrics) with JSONC documentation
- OBP simulator: 24 OIDs, power random walk, StateChange traps, Simetra.OBP-01 community
- NPB simulator: 68 OIDs, Counter64 traffic profiles, portLinkChange traps, Simetra.NPB-01 community
- K8s simulator deployments with pysnmp health probes and DEVICE_NAME env vars
- devices.json with 92 poll OIDs across OBP-01 and NPB-01 (10s interval, K8s DNS addresses)
- DNS resolution in DeviceRegistry for K8s Service names + optional CommunityString override

See `.planning/milestones/v1.1-REQUIREMENTS.md` for full requirement details.

**v1.2 Operational Enhancements (shipped 2026-03-08)**

- K8s API watch for ConfigMap changes with sub-second event delivery (replaces file-based hot-reload)
- Split ConfigMap architecture: simetra-oidmaps + simetra-devices + snmp-collector-config
- Dynamic device/poll schedule reloading without pod restart (DynamicPollScheduler reconciles Quartz jobs)
- Local development fallback with file-based loading
- Live UAT verified: 13 ConfigMap scenarios + watch reconnection against 3-replica cluster

See `.planning/milestones/v1.2-REQUIREMENTS.md` for full requirement details.

**v1.3 Grafana Dashboards (shipped 2026-03-09)**

- Operations dashboard JSON: pod identity/role table, 11 pipeline counter time series, 6 .NET runtime time series, per-pod host filter, auto-refresh 5s
- Business dashboard JSON: gauge and info metric tables with cascading Host/Pod/Device filters, Trend column with delta arrows, PromQL column with copyable queries
- Dashboard JSON files created by Claude, imported manually by user via Grafana UI

**v1.4 E2E System Verification (shipped 2026-03-09)**

- Dedicated E2E test simulator (pysnmp) with 9 OIDs (7 mapped, 2 unmapped) and dual trap loops
- Bash E2E test runner with poll-until-satisfied, delta-based counter assertions, ConfigMap snapshot/restore
- 27 scenario scripts producing 33 test results across 5 categories (pipeline counters, business metrics, OID mutations, device lifecycle, watcher resilience)
- All 10 pipeline counters verified via Prometheus delta queries
- snmp_gauge/snmp_info label correctness, unknown OID classification, trap-originated metrics verified
- OID rename/remove/add and device add/remove/modify ConfigMap mutations verified at runtime
- ConfigMap watcher resilience: invalid JSON handling, log verification, reconnection observation
- 5-category categorized Markdown report with pass/fail evidence

See `.planning/milestones/v1.4-REQUIREMENTS.md` for full requirement details.

**v1.7 Configuration Consistency & Tenant Commands (shipped 2026-03-15)**

- CommunityString as explicit device identifier — DeviceOptions.CommunityString holds full value, DeviceInfo.Name derived at load time
- Tenant Commands data model — CommandSlotOptions (Ip, Port, CommandName, Value, ValueType) for future SNMP SET
- MetricSlotOptions.Role (Evaluate/Resolved) with TEN-13 tenant completeness gate
- Per-entry skip validation — CommunityString, Role, ValueType, MetricName, IP+Port, zero-OID poll groups
- Watcher-validates-registry-stores architecture — all 4 watchers validate, all 4 registries pure stores
- Config renames — tenants.json, oid_metric_map.json, oid_command_map.json

See `.planning/milestones/v1.7-REQUIREMENTS.md` for full requirement details.

**v1.8 Combined Metrics (shipped 2026-03-15)**

- Aggregate metrics — AggregatedMetricName + Aggregator (sum/subtract/absDiff/mean) on poll groups
- Synthetic pipeline — dispatches as snmp_gauge with oid="0.0", source="synthetic"; OidResolution bypassed for SnmpSource.Synthetic
- All-or-nothing computation — all numeric, all responded; exception isolated from individual metrics
- snmp.aggregated.computed pipeline counter + operations dashboard panel

See `.planning/milestones/v1.8-REQUIREMENTS.md` for full requirement details.

**v1.9 Metric Threshold Structure & Validation (shipped 2026-03-15)**

- ThresholdOptions (Min double?, Max double?) on tenant metric entries with load-time validation
- Min > Max = Error log, threshold cleared, metric still loads
- GraceMultiplier on PollOptions (default 2.0), resolved from device poll group
- IntervalSeconds resolved from device poll group (not operator-set on tenants)

See `.planning/milestones/v1.9-REQUIREMENTS.md` for full requirement details.

**v1.10 Heartbeat Refactor & Pipeline Liveness (shipped 2026-03-15)**

- Removed hardcoded heartbeat tenant + bypass (-115 lines, zero special cases)
- IHeartbeatLivenessService: pipeline-arrival liveness stamp in OtelMetricHandler
- LivenessHealthCheck: two layers — job completion (all jobs) + pipeline arrival (heartbeat)
- Staleness: IntervalSeconds × GraceMultiplier (30s default)

See `.planning/milestones/v1.10-REQUIREMENTS.md` for full requirement details.

**v2.0 Tenant Evaluation & Control (shipped 2026-03-17)**

- SnapshotJob: 4-tier tenant evaluation (staleness, resolved thresholds, evaluate thresholds, command dispatch) on 15s Quartz cycle
- Priority group traversal: parallel within group, sequential across groups, advance only if all violated
- CommandWorkerService: Channel-backed SNMP SET execution with leader gate and Stopwatch logging
- SET response dispatched through full MediatR pipeline with source=Command
- ISuppressionCache: per-tenant suppression window with lazy TTL expiry (ConcurrentDictionary, Ip:Port:CommandName key)
- 3 command pipeline counters (snmp.command.sent/failed/suppressed) + snmp.snapshot.cycle_duration_ms histogram
- MetricSlotHolder sentinel timestamp at construction + Range validation (GraceMultiplier 2-5, TimeoutMultiplier 0.1-0.9)
- Label rename: metric_name -> resolved_name across all instruments and dashboards
- SnapshotJob liveness stamp via ILivenessVectorService

See `.planning/milestones/v2.0-REQUIREMENTS.md` for full requirement details.

**v2.1 E2E Tenant Evaluation Tests (shipped 2026-03-20)**

- HTTP-controlled E2E simulator with 24 OIDs, per-OID override endpoints, and named scenario switching
- 52 E2E scenario scripts across 6 categories producing 113 test results
- 4-tenant 2-group fixture testing all advance gate combinations (3 pass + 4 block)
- Tenant validation hardening: 8 new checks with per-entry skip semantics
- Deterministic watcher startup order: OidMap -> Devices -> CommandMap -> Tenants
- Advance gate fix: tier=4 always returns Unresolved (suppressed commands block gate)
- Readiness window: grace = TimeSeriesSize x IntervalSeconds x GraceMultiplier

See `.planning/milestones/v2.1-REQUIREMENTS.md` for full requirement details.

**v2.2 Progressive E2E Snapshot Suite (shipped 2026-03-22)**

- Progressive 3-stage E2E test suite: single tenant → two tenants → four tenants with advance gate
- 16 new PSS scenario scripts (53-68) with stage gating (FAIL_COUNT gates)
- All evaluation states verified: Not Ready, Stale, Resolved, Unresolved, Healthy, Suppressed
- Two-tenant independence proven: per-tenant results don't interfere
- All 7 advance gate combinations verified (3 pass + 4 block) with 4-tenant 2-group fixture
- Runner stabilization: stale filename fixes, cleanup traps, --since alignment, standalone report categories

See `.planning/milestones/v2.2-REQUIREMENTS.md` for full requirement details.

**v2.3 Metric Validity & Correctness (shipped 2026-03-22)**

- 38 E2E scenarios (69-106) validating every metric instrument
- Pipeline counters: published/handled/rejected/errors proven accurate with exact deltas
- Command counters: dispatched (decision) + suppressed (prevention) + failed (execution) semantics clarified
- Business values: zero-transformation proven for all 7 SNMP types + value change propagation
- Labels: source, snmp_type, resolved_name, device_name all verified correct
- Negative proofs: heartbeat, unmapped, bad-community, dropped, follower export all proven

See `.planning/milestones/v2.3-REQUIREMENTS.md` for full requirement details.

**v2.4 Tenant Vector Metrics (shipped 2026-03-23)**

- TenantMetricService: 8 OTel instruments (6 counters, 1 gauge, 1 histogram) on SnmpCollector.Tenant meter
- All instances export tenant metrics (not leader-gated) — follower pods verified
- EvaluateTenant instrumented: RecordAndReturn at all 4 exit points, counting helpers, per-cycle batched metrics
- Operations dashboard Tenant Status table: 13 columns with state color mapping, trend arrows, increase()[30s] counters
- 6 E2E scenarios (107-112) proving full instrument→Prometheus pipeline for all evaluation paths

See `.planning/milestones/v2.4-REQUIREMENTS.md` for full requirement details.

**v2.5 Tenant Metrics Approach Modification (shipped 2026-03-23)**

- 6 percentage gauges replacing 6 counters: tenant.metric.stale.percent, tenant.metric.resolved.percent, tenant.metric.evaluate.percent, tenant.command.dispatched.percent, tenant.command.failed.percent, tenant.command.suppressed.percent
- Gather-then-decide EvaluateTenant flow: gather → decide → dispatch (only Unresolved) → compute percentages → record all at exit
- Resolved metric direction flipped: violated holders (higher = worse)
- State renamed: tenant.state → tenant.evaluation.state
- Dashboard: percentage columns with (%) suffix, direct gauge PromQL
- 7 E2E scenarios (107-113) including 50% partial percentage verification

See `.planning/milestones/v2.5-REQUIREMENTS.md` for full requirement details.

### Active

(None — planning next milestone)

### Out of Scope

- Custom middleware pipeline — using MediatR
- Device modules (`IDeviceModule`) — device-agnostic, flat OID map only
- Traces / distributed tracing — no TracerProvider, no ActivitySource
- OID prefix/pattern matching — flat exact-match dictionary only
- Per-OID metric names — using two shared instruments (snmp_gauge, snmp_info)
- `raw_value` label on gauge metrics — only snmp_info carries string `value` label
- SNMPv3 auth / USM security — target devices use v2c
- Cross-watcher cascade reloads — operator triggers reload manually after config changes

## Context

**Current state:** v2.4 shipped. 475 unit tests passing, 112 E2E scenario scripts (01-112) across 7 categories. Running in Docker Desktop K8s cluster (3 replicas) with OTel Collector + Prometheus + Grafana. Two Grafana dashboards (business + operations) with per-tenant status table. All 4 watchers follow watcher-validates-registry-stores pattern. Closed-loop tenant evaluation with SNMP SET command execution and full per-tenant observability.

**Reference project:** `src/Simetra/` is an existing SNMP monitoring system used as architectural reference. Key patterns adopted: structured logging, OTel setup, console formatter, correlation IDs, leader election, role-gated export. Key patterns replaced: custom middleware -> MediatR, device modules -> flat OID map, channels -> single shared trap channel.

**Target devices:** NPB (Network Packet Broker, CGS enterprise 47477.100) and OBP (Optical Bypass, CGS enterprise 47477.10.21). Both share a single oidmaps.json (92 entries).

**Known tech debt:** None (cleaned up in quick-036)

## Constraints

- **Runtime**: C# .NET 9
- **Event routing**: MediatR 12.5.0 (MIT) — v13+ is RPL-1.5, do not upgrade
- **Scheduling**: Quartz.NET — in-memory store, dynamic job registration
- **SNMP library**: SharpSnmpLib (Lextm) — SNMPv2c only
- **Telemetry**: OpenTelemetry SDK with OTLP gRPC exporter — metrics and logs only
- **HA**: Kubernetes Lease API for leader election — all instances active, export gated
- **Metric design**: Two instruments (snmp_gauge, snmp_info) — type determined at runtime from SNMP TypeCode
- **OID map**: Flat dictionary, no pattern matching — exact OID string lookup
- **Community string**: Simetra.{DeviceName} convention for both auth and device identity

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| MediatR over custom middleware | Simpler extension model, familiar CQRS pattern, testable with in-memory bus | Good |
| Two instruments (gauge + info) | Correct Prometheus types, counters as raw gauges let Prometheus rate() handle delta | Good |
| Flat OID map over device modules | Device-agnostic, config-only OID changes, no code deployment for new OIDs | Good |
| Counter delta removed | Prometheus rate() handles natively; in-app delta was unnecessary complexity | Good |
| No raw_value label on gauge | Prevents cardinality explosion from changing numeric values as labels | Good |
| All instances poll and receive | No warm-up delay on failover, leader only controls metric export | Good |
| No traces | Metrics and logs sufficient for SNMP monitoring, reduces complexity | Good |
| Community string convention | Simetra.{DeviceName} replaces IP-based device lookup; works for any IP | Good |
| host_name/pod_name removed from TagLists | Redundant with OTel resource attributes service_instance_id and k8s_pod_name | Good |
| Heartbeat as internal infra | Pipeline metrics prove liveness; no snmp_gauge pollution for heartbeat | Good |
| IRequest<Unit> over INotification | MediatR behaviors only fire for IRequest; INotification bypasses pipeline entirely | Good |
| IsHeartbeat flag at ingestion boundary | Single point of truth in ChannelConsumerService; avoids string comparison in handlers | Good |
| Single shared oidmaps.json | Both device types in one file; simpler K8s ConfigMap management | Good |
| DNS resolution in DeviceRegistry | K8s Service DNS names resolved at startup; MetricPollJob uses pre-resolved IPs | Good |
| Split ConfigMap watchers | OidMapWatcherService and DeviceWatcherService independent; no cascading reloads | Good |
| K8s API watch over projected volume | Sub-second event delivery vs 60-120s kubelet sync; direct ConfigMap read | Good |
| DynamicPollScheduler in both modes | Symmetric ReconcileAsync for K8s and local dev; avoids code path divergence | Good |
| PodIdentityOptions rename | Clearer than SiteOptions; section name "PodIdentity" matches single property | Good |
| Dashboard JSON manual import | No K8s provisioning; user imports via Grafana UI for simplicity | Good |
| Two dashboards (ops + business) | Separation of concerns: ops for pipeline health, business for SNMP metrics | Good |
| Trend column over per-row coloring | Grafana configFromData is field-level not per-row; delta arrows are the viable approach | Good |
| PromQL column with label_replace | Copyable query strings per row via label_join+label_replace with backtick raw strings | Good |
| Cascading Host/Pod/Device filters | Three-level filter for multi-pod multi-device environments | Good |
| Bash E2E runner over pytest | Sufficient for sequential SNMP scenarios, no extra dependencies | Good |
| Poll-until-satisfied over fixed sleeps | Handles OTel 15s export interval variability; 30s timeout, 3s interval | Good |
| Delta-based counter assertions | Correct for cumulative temporality OTel metrics; before/after snapshots | Good |
| ConfigMap snapshot/restore isolation | Safe mutation testing without manual cleanup; per-scenario isolation | Good |
| Sourced-script pattern for scenarios | No shebang in scenarios; inherit lib functions from run-all.sh | Good |
| Pass-with-caveat for WATCH-04 | Watcher reconnection rarely observable in short test windows; code review suffices | Good |

---
*Last updated: 2026-03-23 after v2.5 milestone shipped*
