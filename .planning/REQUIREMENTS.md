# Requirements: SNMP Monitoring System

**Defined:** 2026-03-04
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v1 Requirements

### SNMP Collection

- [ ] **COLL-01**: Trap listener receives SNMPv2c traps on UDP 162 with community string authentication
- [ ] **COLL-02**: Quartz-based poller executes SNMP GET (v2c) for configured OIDs per device
- [ ] **COLL-03**: Each device has its own IP, OID list, and configurable poll intervals in appsettings
- [ ] **COLL-04**: Quartz creates a MetricPollJob per device/poll combination
- [ ] **COLL-05**: Quartz thread pool auto-scales to total job count
- [ ] **COLL-06**: Poll timeout set to 80% of interval to leave response window
- [x] **COLL-07**: Both traps and polls publish the same `SnmpOidReceived` notification to MediatR

### OID Map

- [x] **MAP-01**: Flat `Dictionary<string, string>` in appsettings under `OidMap` section
- [x] **MAP-02**: Maps OID string to metric_name (e.g., `"1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad"`)
- [x] **MAP-03**: OID in map resolves to metric_name; OID not in map resolves to "Unknown"
- [x] **MAP-04**: Shared by traps and polls — no device distinction
- [x] **MAP-05**: Hot-reloadable without app restart (file change detection or config reload)

### MediatR Pipeline

- [x] **PIPE-01**: MediatR v12.5.0 (MIT license) for event routing
- [x] **PIPE-02**: `SnmpOidReceived` notification carries OID, agent IP, value, source (poll/trap), SNMP TypeCode
- [x] **PIPE-03**: `LoggingBehavior` logs every OID received with agent and source
- [x] **PIPE-04**: `ExceptionBehavior` catches all failures, never crashes the pipeline
- [x] **PIPE-05**: `ValidationBehavior` rejects malformed OID or IP format
- [x] **PIPE-06**: `OidResolutionBehavior` resolves metric_name from OID map, sets "Unknown" if not found
- [x] **PIPE-07**: `OtelMetricHandler` records to correct instrument based on SNMP TypeCode
- [x] **PIPE-08**: Behavior registration order: Logging → Exception → Validation → OidResolution
- [x] **PIPE-09**: Configure `TaskWhenAllPublisher` for parallel handler dispatch with per-handler error isolation

### Business Metrics

- [x] **METR-01**: `snmp_gauge` instrument for Integer32, Gauge32, TimeTicks values
- [x] **METR-02**: `snmp_counter` instrument for Counter32, Counter64 values
- [x] **METR-03**: `snmp_info` instrument (gauge=1) for OctetString, IpAddress, OID with string in `value` label
- [x] **METR-04**: Common labels on all instruments: `site_name`, `metric_name`, `oid`, `agent`, `source`
- [x] **METR-05**: `site_name` loaded from appsettings `Site:Name`
- [x] **METR-06**: Runtime metric type detection from SNMP TypeCode — no type field in OID map

### Counter Delta Engine

- [x] **DELT-01**: Cache stores last cumulative value per OID+agent combination
- [x] **DELT-02**: Delta = current - previous for normal increments
- [x] **DELT-03**: Counter reset detection: current < previous → treat current as delta
- [x] **DELT-04**: Counter32 wrap-around handling at 2^32 boundary
- [x] **DELT-05**: First poll after startup: skip (no previous baseline)
- [x] **DELT-06**: Poll sysUpTime to distinguish device reboot from counter wrap

### Pipeline Metrics

- [x] **PMET-01**: `snmp.event.published` — events published to MediatR
- [x] **PMET-02**: `snmp.event.handled` — events successfully handled by OtelMetricHandler
- [x] **PMET-03**: `snmp.event.errors` — exceptions caught by ExceptionBehavior
- [x] **PMET-04**: `snmp.event.rejected` — events rejected by ValidationBehavior
- [x] **PMET-05**: `snmp.poll.executed` — Quartz poll jobs completed
- [x] **PMET-06**: `snmp.trap.received` — traps received on UDP 162
- [x] **PMET-07**: All pipeline metrics include `site_name` label
- [x] **PMET-08**: Pipeline metrics exported by all instances (not leader-gated)

### Logging & Telemetry

- [ ] **LOG-01**: Structured logging with `ILogger` and named placeholders
- [ ] **LOG-02**: Custom console formatter: `[timestamp] [level] [site|role|correlationId] category message`
- [ ] **LOG-03**: Console output toggleable via `Logging:EnableConsole` in appsettings
- [ ] **LOG-04**: OTLP log export with enrichment processor (site_name, role, correlationId)
- [ ] **LOG-05**: Correlation IDs: rotating global via CorrelationJob + per-operation AsyncLocal scoping
- [ ] **LOG-06**: OTLP gRPC export for metrics and logs to OTel Collector
- [ ] **LOG-07**: No traces — metrics and logs only

### Leader Election & HA

- [ ] **HA-01**: `ILeaderElection` interface with `IsLeader` and `CurrentRole` properties
- [ ] **HA-02**: `K8sLeaseElection` using Kubernetes Lease API (auto-detected via `IsInCluster()`)
- [ ] **HA-03**: `AlwaysLeaderElection` for local development (non-K8s environments)
- [ ] **HA-04**: All instances poll devices and receive traps (not leader-only)
- [ ] **HA-05**: Only leader exports business metrics (snmp_gauge, snmp_counter, snmp_info)
- [ ] **HA-06**: Pipeline metrics + System.Runtime exported by all instances
- [ ] **HA-07**: `MetricRoleGatedExporter` filters business meter at export time
- [ ] **HA-08**: Near-instant failover via explicit lease deletion on graceful shutdown

### Graceful Shutdown

- [ ] **SHUT-01**: GracefulShutdownService registered last in DI (stops first)
- [ ] **SHUT-02**: Step 1: Release K8s lease (3s budget)
- [ ] **SHUT-03**: Step 2: Stop SNMP trap listener (3s budget)
- [ ] **SHUT-04**: Step 3: Scheduler standby — no new jobs fire (3s budget)
- [ ] **SHUT-05**: Step 4: Drain in-flight operations (8s budget)
- [ ] **SHUT-06**: Step 5: Flush telemetry with independent CTS — always runs (5s budget)
- [ ] **SHUT-07**: Each step has its own CancellationTokenSource budget
- [ ] **SHUT-08**: Total shutdown timeout: 30 seconds

### Health Probes

- [ ] **HLTH-01**: Startup probe: verify OID map loaded and poll definitions registered
- [ ] **HLTH-02**: Readiness probe: verify trap listener running and registry populated
- [ ] **HLTH-03**: Liveness probe: per-job staleness detection (age vs interval * grace multiplier)
- [ ] **HLTH-04**: Job interval registry built at startup for staleness threshold calculation
- [ ] **HLTH-05**: Liveness vector stamped by every job in finally block

### Hardening

- [ ] **HARD-01**: Trap storm protection — rate limiting / backpressure when devices flood traps
- [ ] **HARD-02**: Device unreachability handling — timeout detection, stale metric awareness
- [ ] **HARD-03**: SNMP poll timeout logged at Warning level, device marked unreachable after N consecutive failures
- [ ] **HARD-04**: Configuration validation at startup with fail-fast (ValidateOnStart)

### Push Pipeline

- [ ] **PUSH-01**: Full push: App → OTLP gRPC → OTel Collector (:4317) → remote_write → Prometheus
- [ ] **PUSH-02**: OTel Collector configuration for prometheusremotewriteexporter
- [ ] **PUSH-03**: No scrape endpoint in the application — pure push

### Device Configuration

- [x] **DEVC-01**: Per-device configuration in appsettings with Name, IpAddress, and MetricPolls
- [x] **DEVC-02**: Each MetricPoll entry has an OID list and IntervalSeconds
- [x] **DEVC-03**: Device registry with O(1) lookup by IP (for traps) and by name (for polls)
- [x] **DEVC-04**: Quartz job identity: `metric-poll-{deviceName}-{pollIndex}`

## v2 Requirements

### Advanced Collection

- **ADV-01**: SNMP table walk (GETBULK) for dynamic OID discovery
- **ADV-02**: SNMPv3 support if future devices require it

### Operational Enhancements

- **OPS-01**: Hot-reloadable device configuration (add/remove devices without restart)
- **OPS-02**: Heartbeat loopback job proving full pipeline liveness
- **OPS-03**: Grafana dashboard templates for NPB and OBP devices
- **OPS-04**: Prometheus alerting rules for common failure conditions

## Out of Scope

| Feature | Reason |
|---------|--------|
| Device management / SNMP SET | Monitor only — configuration push is a different product |
| MIB browser / MIB compilation | Use external tools (snmpwalk, MIB Browser). Not core to monitoring. |
| Network topology discovery | Duplicates specialized tools (LibreNMS, NetBox) |
| Embedded alerting engine | Prometheus Alertmanager handles this better |
| Embedded TSDB | Prometheus is the TSDB — no need to duplicate |
| Custom middleware pipeline | Using MediatR — no need for Simetra-style custom pipeline |
| Device modules (IDeviceModule) | Device-agnostic flat OID map — no per-type code |
| Channel queuing | MediatR direct dispatch — no System.Threading.Channels |
| SNMPv3 auth / USM security | Target devices (NPB, OBP) use v2c — unnecessary complexity |
| SNMP Inform acknowledgment | Not needed for v2c trap reception |
| Traces / distributed tracing | Metrics and logs sufficient for SNMP monitoring |
| ObservableCounter / ObservableGauge | Using regular Counter.Add(delta) and Gauge.Record(value) |
| raw_value label on gauge/counter | Prevents cardinality explosion — only snmp_info has value label |
| Per-OID metric names | Three shared instruments (snmp_gauge, snmp_counter, snmp_info) |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| COLL-01 | Phase 5 | Pending |
| COLL-02 | Phase 6 | Pending |
| COLL-03 | Phase 6 | Pending |
| COLL-04 | Phase 6 | Pending |
| COLL-05 | Phase 6 | Pending |
| COLL-06 | Phase 6 | Pending |
| COLL-07 | Phase 3 | Complete |
| MAP-01 | Phase 2 | Complete |
| MAP-02 | Phase 2 | Complete |
| MAP-03 | Phase 2 | Complete |
| MAP-04 | Phase 2 | Complete |
| MAP-05 | Phase 2 | Complete |
| PIPE-01 | Phase 3 | Complete |
| PIPE-02 | Phase 3 | Complete |
| PIPE-03 | Phase 3 | Complete |
| PIPE-04 | Phase 3 | Complete |
| PIPE-05 | Phase 3 | Complete |
| PIPE-06 | Phase 3 | Complete |
| PIPE-07 | Phase 3 | Complete |
| PIPE-08 | Phase 3 | Complete |
| PIPE-09 | Phase 3 | Complete |
| METR-01 | Phase 3 | Complete |
| METR-02 | Phase 3 | Complete |
| METR-03 | Phase 3 | Complete |
| METR-04 | Phase 3 | Complete |
| METR-05 | Phase 3 | Complete |
| METR-06 | Phase 3 | Complete |
| DELT-01 | Phase 4 | Complete |
| DELT-02 | Phase 4 | Complete |
| DELT-03 | Phase 4 | Complete |
| DELT-04 | Phase 4 | Complete |
| DELT-05 | Phase 4 | Complete |
| DELT-06 | Phase 4 | Complete |
| PMET-01 | Phase 3 | Complete |
| PMET-02 | Phase 3 | Complete |
| PMET-03 | Phase 3 | Complete |
| PMET-04 | Phase 3 | Complete |
| PMET-05 | Phase 3 | Complete |
| PMET-06 | Phase 3 | Complete |
| PMET-07 | Phase 3 | Complete |
| PMET-08 | Phase 3 | Complete |
| LOG-01 | Phase 1 | Complete |
| LOG-02 | Phase 1 | Complete |
| LOG-03 | Phase 1 | Complete |
| LOG-04 | Phase 1 | Complete |
| LOG-05 | Phase 1 | Complete |
| LOG-06 | Phase 1 | Complete |
| LOG-07 | Phase 1 | Complete |
| HA-01 | Phase 7 | Pending |
| HA-02 | Phase 7 | Pending |
| HA-03 | Phase 7 | Pending |
| HA-04 | Phase 7 | Pending |
| HA-05 | Phase 7 | Pending |
| HA-06 | Phase 7 | Pending |
| HA-07 | Phase 7 | Pending |
| HA-08 | Phase 7 | Pending |
| SHUT-01 | Phase 8 | Pending |
| SHUT-02 | Phase 8 | Pending |
| SHUT-03 | Phase 8 | Pending |
| SHUT-04 | Phase 8 | Pending |
| SHUT-05 | Phase 8 | Pending |
| SHUT-06 | Phase 8 | Pending |
| SHUT-07 | Phase 8 | Pending |
| SHUT-08 | Phase 8 | Pending |
| HLTH-01 | Phase 8 | Pending |
| HLTH-02 | Phase 8 | Pending |
| HLTH-03 | Phase 8 | Pending |
| HLTH-04 | Phase 8 | Pending |
| HLTH-05 | Phase 8 | Pending |
| HARD-01 | Phase 5 | Pending |
| HARD-02 | Phase 6 | Pending |
| HARD-03 | Phase 6 | Pending |
| HARD-04 | Phase 1 | Complete |
| PUSH-01 | Phase 1 | Complete |
| PUSH-02 | Phase 1 | Complete |
| PUSH-03 | Phase 1 | Complete |
| DEVC-01 | Phase 2 | Complete |
| DEVC-02 | Phase 2 | Complete |
| DEVC-03 | Phase 2 | Complete |
| DEVC-04 | Phase 2 | Complete |

**Coverage:**
- v1 requirements: 80 total
- Mapped to phases: 80
- Unmapped: 0

---
*Requirements defined: 2026-03-04*
*Last updated: 2026-03-05 after Phase 4 completion*
