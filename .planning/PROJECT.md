# SNMP Monitoring System

## What This Is

A K8s-native SNMP monitoring agent that receives traps and polls devices, resolves OIDs to human-readable metric names via a flat OID map, and pushes metrics through OpenTelemetry to Prometheus/Grafana. Built on C# .NET 9 with MediatR for event routing and Quartz for poll scheduling. Runs as a 3-replica Kubernetes deployment with leader-gated metric export and near-instant failover. Includes OBP and NPB device simulators for development and testing.

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

### Active

(No active requirements — planning next milestone)

### Out of Scope

- Device management / configuration writes — monitor only, no SNMP SET
- Custom middleware pipeline — using MediatR
- Device modules (`IDeviceModule`) — device-agnostic, flat OID map only
- Traces / distributed tracing — no TracerProvider, no ActivitySource
- OID prefix/pattern matching — flat exact-match dictionary only
- Per-OID metric names — using two shared instruments (snmp_gauge, snmp_info)
- `raw_value` label on gauge metrics — only snmp_info carries string `value` label
- SNMPv3 auth / USM security — target devices use v2c

## Context

**Current state:** v1.4 shipped. 4,937 LOC source + 4,318 LOC tests across 76 C# files + 783 LOC Python simulators + ~1,848 LOC bash/python E2E test infrastructure. 138 unit tests passing. Running in Docker Desktop K8s cluster (3 replicas) with OTel Collector + Prometheus + Grafana. Full E2E test harness with 27 scenarios covering pipeline counters, business metrics, OID mutations, device lifecycle, and watcher resilience. Two Grafana dashboards shipped. Single Prometheus instance serves multiple sites via service_instance_id label.

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
*Last updated: 2026-03-09 after v1.4 milestone completion*
