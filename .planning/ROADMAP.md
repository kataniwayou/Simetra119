# Roadmap: SNMP Monitoring System

## Overview

This roadmap builds a K8s-native SNMP monitoring agent from infrastructure up to production-ready operation. The build order is dictated by two hard dependency constraints — nothing can resolve OIDs without the registry, and nothing can push metrics without the pipeline — and two correctness constraints — OTel cardinality must be locked before any instrument is created, and counter delta logic must be correct before any counter metrics reach Prometheus. Eight phases deliver one coherent, verifiable capability each: foundation, registries, pipeline, delta engine, trap ingestion, poll scheduling, leader-gated export, and production hardening.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Infrastructure Foundation** - Running host with OTel SDK, structured logging, and push pipeline wired
- [ ] **Phase 2: Device Registry and OID Map** - All lookup structures built, cardinality locked, config validated
- [ ] **Phase 3: MediatR Pipeline and Instruments** - Full behavior chain and all metric instruments verified in isolation
- [ ] **Phase 4: Counter Delta Engine** - Correct delta computation including wrap-around and reboot detection
- [ ] **Phase 5: Trap Ingestion** - UDP 162 listener receiving traps end-to-end through MediatR
- [ ] **Phase 6: Poll Scheduling** - Quartz-driven SNMP GET publishing to MediatR with unreachability handling
- [ ] **Phase 7: Leader Election and Role-Gated Export** - Exactly one pod exports business metrics in multi-instance deployment
- [ ] **Phase 8: Graceful Shutdown and Health Probes** - Clean SIGTERM handling and K8s health probe coverage

## Phase Details

### Phase 1: Infrastructure Foundation
**Goal**: A running .NET 9 Generic Host exists with OTel SDK registered, structured logging active, OTLP push pipeline configured, and startup configuration validated — so every subsequent phase has a testable host to build into.
**Depends on**: Nothing (first phase)
**Requirements**: LOG-01, LOG-02, LOG-03, LOG-04, LOG-05, LOG-06, LOG-07, PUSH-01, PUSH-02, PUSH-03, HARD-04
**Success Criteria** (what must be TRUE):
  1. The application starts, logs a structured startup line to console with the custom formatter (`[timestamp] [level] [site|role|correlationId] category message`), and exits cleanly
  2. OTLP gRPC export is configured to the OTel Collector endpoint and the OTel Collector forwards metrics via `prometheusremotewriteexporter` to Prometheus (pipeline wired, no business metrics yet)
  3. A missing or malformed required config value (e.g., missing `Site:Name`) causes the application to fail-fast at startup with a clear error before accepting any network traffic
  4. Console output can be suppressed by setting `Logging:EnableConsole: false` in appsettings without code changes
  5. Correlation IDs appear on every log line — rotating global ID from CorrelationJob and per-operation AsyncLocal ID visible in the log formatter
**Plans**: 5 plans in 4 waves

Plans:
- [ ] 01-01-PLAN.md — Project scaffold: .NET 9 csproj, config options classes, appsettings files (Wave 1)
- [ ] 01-02-PLAN.md — Docker Compose deploy stack: OTel Collector + Prometheus + Grafana (Wave 1)
- [ ] 01-03-PLAN.md — Telemetry classes: correlation service, console formatter, enrichment processor (Wave 2)
- [ ] 01-04-PLAN.md — DI wiring: ServiceCollectionExtensions + Program.cs entry point (Wave 3)
- [ ] 01-05-PLAN.md — Config validators: SiteOptionsValidator, OtlpOptionsValidator, fail-fast verification (Wave 4)

### Phase 2: Device Registry and OID Map
**Goal**: All lookup structures (device registry and OID map) are populated from configuration, O(1) lookups work correctly, cardinality is explicitly counted and bounded before any metric instruments are created, and hot-reload of the OID map functions without restart.
**Depends on**: Phase 1
**Requirements**: MAP-01, MAP-02, MAP-03, MAP-04, MAP-05, DEVC-01, DEVC-02, DEVC-03, DEVC-04
**Success Criteria** (what must be TRUE):
  1. A device can be looked up by IP address in O(1) (trap path) and by name in O(1) (poll path) from the device registry populated at startup
  2. An OID present in `appsettings OidMap` resolves to its configured `metric_name`; an OID absent from the map resolves to the string `"Unknown"` — both verifiable in a unit test without a running host
  3. Modifying the OID map in appsettings (adding or changing an entry) takes effect without application restart
  4. The label taxonomy (`site_name`, `metric_name`, `oid`, `agent`, `source`) is documented with a cardinality estimate against the target device fleet, and all values are bounded (no raw IP strings or OID strings as label values outside the `oid` label)
  5. Quartz job identities (`metric-poll-{deviceName}-{pollIndex}`) are derivable from device config at startup
**Plans**: TBD

Plans:
- [ ] 02-01: Device options model and device registry with dual-keyed lookup (IP + name)
- [ ] 02-02: OID map loading from appsettings with hot-reload via IOptionsMonitor
- [ ] 02-03: OID resolution logic with "Unknown" fallback and unit tests
- [ ] 02-04: Label taxonomy design — cardinality audit against NPB/OBP device fleet

### Phase 3: MediatR Pipeline and Instruments
**Goal**: The complete MediatR behavior chain and all three OTel metric instruments are built, wired, and unit-testable with synthetic `SnmpOidReceived` notifications — so the pipeline is fully verified before any real network traffic arrives.
**Depends on**: Phase 2
**Requirements**: PIPE-01, PIPE-02, PIPE-03, PIPE-04, PIPE-05, PIPE-06, PIPE-07, PIPE-08, PIPE-09, METR-01, METR-02, METR-03, METR-04, METR-05, METR-06, PMET-01, PMET-02, PMET-03, PMET-04, PMET-05, PMET-06, PMET-07, PMET-08, COLL-07
**Success Criteria** (what must be TRUE):
  1. Publishing a synthetic `SnmpOidReceived` notification with SNMP TypeCode Integer32 causes `snmp_gauge` to be recorded with the correct labels (`site_name`, `metric_name`, `oid`, `agent`, `source`) and no other instruments are touched
  2. Publishing a notification with a malformed OID or invalid agent IP causes the notification to be rejected by `ValidationBehavior` and `snmp.event.rejected` to increment — no exception propagates to the caller
  3. Publishing a notification that causes any behavior to throw causes `snmp.event.errors` to increment and the next notification processes normally — the pipeline never crashes
  4. All pipeline metrics (`snmp.event.published`, `snmp.event.handled`, `snmp.event.errors`, `snmp.event.rejected`, `snmp.poll.executed`, `snmp.trap.received`) are visible in Prometheus without requiring leader election
  5. Behavior execution order is verifiable: Logging fires first, then Exception, then Validation, then OidResolution, then OtelMetricHandler — confirmable via log output on a test notification
**Plans**: TBD

Plans:
- [ ] 03-01: MediatR v12.5.0 registration and SnmpOidReceived notification model
- [ ] 03-02: LoggingBehavior and ExceptionBehavior with pipeline metric counters
- [ ] 03-03: ValidationBehavior (OID format and IP format checks) with rejection counter
- [ ] 03-04: OidResolutionBehavior wired to Phase 2 OID map
- [ ] 03-05: MetricFactory instrument cache — snmp_gauge, snmp_counter, snmp_info with label assembly
- [ ] 03-06: OtelMetricHandler — TypeCode-to-instrument dispatch and snmp.event.handled counter
- [ ] 03-07: TaskWhenAllPublisher configuration and per-handler error isolation verification

### Phase 4: Counter Delta Engine
**Goal**: The counter delta engine correctly computes deltas for all counter scenarios — normal increment, Counter32 wrap-around at 2^32, Counter64 wrap-around, device reboot detection via sysUpTime, and first-poll skip — before any counter metrics reach Prometheus.
**Depends on**: Phase 3
**Requirements**: DELT-01, DELT-02, DELT-03, DELT-04, DELT-05, DELT-06
**Success Criteria** (what must be TRUE):
  1. A counter that increments from 1000 to 1500 produces a delta of 500 recorded to `snmp_counter` — verifiable in a unit test against the delta engine in isolation
  2. A Counter32 that wraps from 4,294,967,200 to 100 is correctly identified as wrap-around (not a reset) and produces the correct delta (~196) — verifiable with a synthetic test case
  3. A counter value lower than the previous value that coincides with a sysUpTime reset (device reboot detected) causes the current value to be treated as the delta — verifiable with a unit test providing synthetic uptime values
  4. The first poll for a given OID+agent combination after startup produces no `snmp_counter` recording — the baseline is stored but not emitted
  5. The delta cache is keyed by OID+agent combination so that two different agents reporting the same OID maintain independent delta state
**Plans**: TBD

Plans:
- [ ] 04-01: Counter delta cache (ConcurrentDictionary keyed by OID+agent) with first-poll skip logic
- [ ] 04-02: Delta computation — normal increment, Counter32 wrap at 2^32, Counter64 wrap
- [ ] 04-03: sysUpTime polling integration and reboot-vs-wrap disambiguation
- [ ] 04-04: Unit test suite — synthetic counter scenarios covering all edge cases

### Phase 5: Trap Ingestion
**Goal**: The application receives SNMPv2c traps on UDP 162 and routes each varbind through the MediatR pipeline to the correct metric instrument — with backpressure under trap storms and community string authentication.
**Depends on**: Phase 3
**Requirements**: COLL-01, HARD-01
**Success Criteria** (what must be TRUE):
  1. An SNMPv2c trap sent to the host on UDP 162 with the configured community string causes `snmp.trap.received` to increment and the trap's varbinds to be recorded to the correct instrument within the MediatR pipeline
  2. A trap received with an incorrect community string is dropped and logged at Warning level — no metric is recorded
  3. A burst of traps exceeding the per-device channel capacity causes excess traps to be dropped (DropOldest) and `snmp.trap.received` reflects received-not-dropped — the listener continues processing without blocking
  4. The trap listener never publishes directly to MediatR — all trap varbinds route through per-device BoundedChannel to ChannelConsumerService before MediatR publish (verifiable by code structure and log sequence)
**Plans**: TBD

Plans:
- [ ] 05-01: SharpSnmpLib trap listener BackgroundService bound to UDP 162 with community string auth
- [ ] 05-02: DeviceChannelManager — per-device BoundedChannel with DropOldest policy
- [ ] 05-03: ChannelConsumerService — one Task per device, reads envelopes, publishes SnmpOidReceived per varbind
- [ ] 05-04: Trap storm rate limiting and snmp.trap.received counter integration

### Phase 6: Poll Scheduling
**Goal**: Quartz executes SNMP GET polls on configured intervals per device, publishes results to MediatR, handles device unreachability gracefully, and the thread pool scales to the total job count without starvation.
**Depends on**: Phase 3, Phase 4
**Requirements**: COLL-02, COLL-03, COLL-04, COLL-05, COLL-06, HARD-02, HARD-03
**Success Criteria** (what must be TRUE):
  1. A configured device at a configured interval receives a Quartz-triggered SNMP GET and the polled OID values appear in Prometheus within the configured interval plus a small tolerance
  2. A device that does not respond within 80% of its configured poll interval causes the poll to time out, logs a Warning, and does not block subsequent polls for other devices or other intervals of the same device
  3. A device that fails N consecutive polls is marked unreachable and logged — subsequent polls continue on schedule rather than accumulating backlog or flooding the log
  4. `snmp.poll.executed` increments after each completed poll job regardless of whether the device responded — success and failure are distinguishable via log level
  5. The Quartz thread pool size is calculated from device count and poll frequency so no job waits for a thread under the expected configuration
**Plans**: TBD

Plans:
- [ ] 06-01: MetricPollJob (Quartz IJob, DisallowConcurrentExecution) with SNMP GET and direct MediatR publish
- [ ] 06-02: Per-device Quartz job registration from device registry (job identity metric-poll-{deviceName}-{pollIndex})
- [ ] 06-03: Poll timeout at 80% of interval via CancellationToken
- [ ] 06-04: Thread pool auto-scaling to job count
- [ ] 06-05: Device unreachability detection — consecutive failure tracking, Warning logging, backoff

### Phase 7: Leader Election and Role-Gated Export
**Goal**: In a multi-instance Kubernetes deployment, exactly one pod exports business metrics (snmp_gauge, snmp_counter, snmp_info) while all pods export pipeline and runtime metrics — with near-instant failover when the leader pod is terminated.
**Depends on**: Phase 5, Phase 6
**Requirements**: HA-01, HA-02, HA-03, HA-04, HA-05, HA-06, HA-07, HA-08
**Success Criteria** (what must be TRUE):
  1. With two application instances running in K8s, exactly one set of `snmp_gauge`, `snmp_counter`, and `snmp_info` series appears in Prometheus — not two sets
  2. Both instances' pipeline metrics (`snmp.event.published`, `snmp.event.handled`, etc.) and `System.Runtime` metrics appear in Prometheus from both pods simultaneously
  3. Terminating the leader pod (SIGTERM) causes another pod to acquire the lease and resume business metric export within the Kubernetes lease renewal interval
  4. In a local (non-K8s) environment, `AlwaysLeaderElection` is selected automatically and business metrics are exported without any Kubernetes dependency
  5. The K8s lease election instance is registered as a single DI singleton that satisfies both `ILeaderElection` and `IHostedService` — verifiable by confirming both interfaces resolve to the same object
**Plans**: TBD

Plans:
- [ ] 07-01: ILeaderElection interface and AlwaysLeaderElection (local dev fallback)
- [ ] 07-02: K8sLeaseElection BackgroundService with IsInCluster auto-detection
- [ ] 07-03: MetricRoleGatedExporter — business meter gated on IsLeader, pipeline/runtime meters always pass
- [ ] 07-04: DI singleton registration pattern — single instance satisfying both ILeaderElection and IHostedService
- [ ] 07-05: Two-instance integration test confirming single business metric series in Prometheus

### Phase 8: Graceful Shutdown and Health Probes
**Goal**: The application shuts down cleanly within 30 seconds under SIGTERM — releasing the K8s lease, draining in-flight work, and flushing telemetry — and K8s health probes correctly reflect application readiness and liveness.
**Depends on**: Phase 7
**Requirements**: SHUT-01, SHUT-02, SHUT-03, SHUT-04, SHUT-05, SHUT-06, SHUT-07, SHUT-08, HLTH-01, HLTH-02, HLTH-03, HLTH-04, HLTH-05
**Success Criteria** (what must be TRUE):
  1. Sending SIGTERM to the application causes it to exit within 30 seconds, having logged each shutdown step (lease release, listener stop, scheduler standby, drain, flush) with its outcome
  2. The K8s lease is released within 3 seconds of SIGTERM, allowing another pod to acquire it and resume metric export before the shutting-down pod's last telemetry flush completes
  3. The startup probe returns healthy only after the OID map is loaded and poll definitions are registered with Quartz — the application does not begin receiving traffic before it is ready
  4. The readiness probe returns healthy only when the trap listener is bound on UDP 162 and the device registry is populated
  5. The liveness probe returns unhealthy for a specific job when its last-execution timestamp is older than its configured interval multiplied by the grace multiplier — allowing K8s to restart the pod rather than leaving a silently stalled job running
**Plans**: TBD

Plans:
- [ ] 08-01: GracefulShutdownService registered last — 5-step shutdown with per-step CTS budgets
- [ ] 08-02: Shutdown step verification — lease release (3s), listener stop (3s), scheduler standby (3s)
- [ ] 08-03: Shutdown step verification — drain in-flight (8s), telemetry flush (5s, independent CTS)
- [ ] 08-04: Job liveness vector — ILivenessVectorService stamped in every job finally block
- [ ] 08-05: StartupHealthCheck, ReadinessHealthCheck, LivenessHealthCheck wired to ASP.NET health endpoints
- [ ] 08-06: CorrelationJob (rotating global correlationId) and job interval registry for staleness thresholds

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Infrastructure Foundation | 0/5 | Planned (3 waves) | - |
| 2. Device Registry and OID Map | 0/4 | Not started | - |
| 3. MediatR Pipeline and Instruments | 0/7 | Not started | - |
| 4. Counter Delta Engine | 0/4 | Not started | - |
| 5. Trap Ingestion | 0/4 | Not started | - |
| 6. Poll Scheduling | 0/5 | Not started | - |
| 7. Leader Election and Role-Gated Export | 0/5 | Not started | - |
| 8. Graceful Shutdown and Health Probes | 0/6 | Not started | - |
