# Project Milestones: SNMP Monitoring System

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
