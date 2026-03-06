# Roadmap: SNMP Monitoring System

## Overview

This roadmap builds a K8s-native SNMP monitoring agent from infrastructure up to production-ready operation. The build order is dictated by two hard dependency constraints — nothing can resolve OIDs without the registry, and nothing can push metrics without the pipeline — and two correctness constraints — OTel cardinality must be locked before any instrument is created, and counter delta logic must be correct before any counter metrics reach Prometheus. Eight phases deliver one coherent, verifiable capability each: foundation, registries, pipeline, delta engine, trap ingestion, poll scheduling, leader-gated export, and production hardening.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Infrastructure Foundation** - Running host with OTel SDK, structured logging, and push pipeline wired
- [x] **Phase 2: Device Registry and OID Map** - All lookup structures built, cardinality locked, config validated
- [x] **Phase 3: MediatR Pipeline and Instruments** - Full behavior chain and all metric instruments verified in isolation
- [x] **Phase 4: Counter Delta Engine** - Correct delta computation including wrap-around and reboot detection
- [x] **Phase 5: Trap Ingestion** - UDP 162 listener receiving traps end-to-end through MediatR
- [x] **Phase 6: Poll Scheduling** - Quartz-driven SNMP GET publishing to MediatR with unreachability handling
- [x] **Phase 7: Leader Election and Role-Gated Export** - Exactly one pod exports business metrics in multi-instance deployment
- [x] **Phase 8: Graceful Shutdown and Health Probes** - Clean SIGTERM handling and K8s health probe coverage
- [x] **Phase 9: Containerized Integration Testing** - K8s integration tests using Docker Desktop stack, observability verification, namespace isolation
- [x] **Phase 10: Metrics Redesign** - Community string convention, open collector model, consistent label taxonomy

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
- [x] 01-01-PLAN.md — Project scaffold: .NET 9 csproj, config options classes, appsettings files (Wave 1)
- [x] 01-02-PLAN.md — Docker Compose deploy stack: OTel Collector + Prometheus + Grafana (Wave 1)
- [x] 01-03-PLAN.md — Telemetry classes: correlation service, console formatter, enrichment processor (Wave 2)
- [x] 01-04-PLAN.md — DI wiring: ServiceCollectionExtensions + Program.cs entry point (Wave 3)
- [x] 01-05-PLAN.md — Config validators: SiteOptionsValidator, OtlpOptionsValidator, fail-fast verification (Wave 4)

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
**Plans**: 4 plans in 3 waves

Plans:
- [x] 02-01-PLAN.md — Config options classes + validators: DeviceOptions, MetricPollOptions, DevicesOptions, OidMapOptions, SnmpListenerOptions (Wave 1)
- [x] 02-02-PLAN.md — Pipeline classes + DI wiring: DeviceRegistry, OidMapService, DeviceInfo, MetricPollInfo (Wave 2)
- [x] 02-03-PLAN.md — TDD: OID resolution and device registry unit tests with test project setup (Wave 3)
- [x] 02-04-PLAN.md — CardinalityAuditService: startup cardinality estimate and label taxonomy logging (Wave 3)

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
**Plans**: 6 plans in 4 waves

Plans:
- [x] 03-01-PLAN.md — NuGet packages (MediatR 12.5.0, SharpSnmpLib 12.5.7), SnmpOidReceived, SnmpSource, PipelineMetricService (Wave 1)
- [x] 03-02-PLAN.md — LoggingBehavior and ExceptionBehavior (Wave 2)
- [x] 03-03-PLAN.md — ValidationBehavior and OidResolutionBehavior (Wave 2)
- [x] 03-04-PLAN.md — SnmpMetricFactory instrument cache and OtelMetricHandler TypeCode dispatch (Wave 2)
- [x] 03-05-PLAN.md — AddSnmpPipeline DI wiring and Program.cs integration (Wave 3)
- [x] 03-06-PLAN.md — TDD: Unit tests for all behaviors, handler, and pipeline integration (Wave 4)

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
**Plans**: 4 plans in 3 waves

Plans:
- [x] 04-01-PLAN.md — Interface extensions: SysUpTimeCentiseconds, RecordCounter, TestSnmpMetricFactory update (Wave 1)
- [x] 04-02-PLAN.md — CounterDeltaEngine: ICounterDeltaEngine + all 5 delta computation paths (Wave 2)
- [x] 04-03-PLAN.md — Handler wiring: OtelMetricHandler integration, DI registration, integration test updates (Wave 3)
- [x] 04-04-PLAN.md — TDD: CounterDeltaEngine unit tests covering all 5 SC edge cases (Wave 3)

### Phase 5: Trap Ingestion
**Goal**: The application receives SNMPv2c traps on UDP 162 and routes each varbind through the MediatR pipeline to the correct metric instrument — with backpressure under trap storms and community string authentication.
**Depends on**: Phase 3
**Requirements**: COLL-01, HARD-01
**Success Criteria** (what must be TRUE):
  1. An SNMPv2c trap sent to the host on UDP 162 with the configured community string causes `snmp.trap.received` to increment and the trap's varbinds to be recorded to the correct instrument within the MediatR pipeline
  2. A trap received with an incorrect community string is dropped and logged at Warning level — no metric is recorded
  3. A burst of traps exceeding the per-device channel capacity causes excess traps to be dropped (DropOldest) and `snmp.trap.received` reflects received-not-dropped — the listener continues processing without blocking
  4. The trap listener never publishes directly to MediatR — all trap varbinds route through per-device BoundedChannel to ChannelConsumerService before MediatR publish (verifiable by code structure and log sequence)
**Plans**: 4 plans in 3 waves

Plans:
- [x] 05-01-PLAN.md — Foundation types: ChannelsOptions, VarbindEnvelope, IDeviceChannelManager/DeviceChannelManager, PipelineMetricService trap counters (Wave 1)
- [x] 05-02-PLAN.md — SnmpTrapListenerService: UDP receive loop, community auth, channel write (Wave 2)
- [x] 05-03-PLAN.md — ChannelConsumerService: per-device ReadAllAsync, ISender.Send dispatch (Wave 2)
- [x] 05-04-PLAN.md — DI wiring + TDD: ServiceCollectionExtensions registration, unit tests for all Phase 5 SC (Wave 3)

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
**Plans**: 4 plans in 4 waves

Plans:
- [x] 06-01-PLAN.md — Foundation types: IDeviceUnreachabilityTracker, DeviceUnreachabilityTracker, PipelineMetricService poll counters (Wave 1)
- [x] 06-02-PLAN.md — MetricPollJob: Quartz IJob with SNMP GET, ISender.Send dispatch, timeout, unreachability (Wave 2)
- [x] 06-03-PLAN.md — DI wiring: AddSnmpScheduling thread pool sizing, job registration, PollSchedulerStartupService (Wave 3)
- [x] 06-04-PLAN.md — Unit tests: DeviceUnreachabilityTracker transitions, MetricPollJob dispatch and failure handling (Wave 4)

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
**Plans**: 5 plans in 4 waves

Plans:
- [x] 07-01-PLAN.md — ILeaderElection interface, AlwaysLeaderElection, TelemetryConstants.LeaderMeterName, LeaseOptions (Wave 1)
- [x] 07-02-PLAN.md — K8sLeaseElection BackgroundService with Lease API and IsInCluster auto-detection (Wave 2)
- [x] 07-03-PLAN.md — MetricRoleGatedExporter with ParentProvider reflection, SnmpMetricFactory LeaderMeterName (Wave 2)
- [x] 07-04-PLAN.md — DI wiring: concrete-first singleton, dynamic role, AddReader replacing AddOtlpExporter (Wave 3)
- [x] 07-05-PLAN.md — Unit tests: MetricRoleGatedExporter gating, DI singleton verification, SnmpMetricFactoryTests fix (Wave 4)

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
**Plans**: 5 plans in 4 waves

Plans:
- [x] 08-01-PLAN.md — Foundation types: ILivenessVectorService, IJobIntervalRegistry, LivenessOptions, WaitForDrainAsync (Wave 1)
- [x] 08-02-PLAN.md — Health checks: StartupHealthCheck, ReadinessHealthCheck, LivenessHealthCheck (Wave 2)
- [x] 08-03-PLAN.md — GracefulShutdownService: 5-step ordered shutdown with per-step CTS budgets (Wave 2)
- [x] 08-04-PLAN.md — DI wiring: WebApplication, health endpoints, job stamps, Dockerfile (Wave 3)
- [x] 08-05-PLAN.md — Unit tests: 23 new tests for all Phase 8 components (Wave 4)

### Phase 9: Containerized Integration Testing
**Goal:** Verify the full SnmpCollector pipeline runs correctly in a containerized K8s environment using the Docker Desktop observability stack — remove simulators and Simetra pods from the simetra namespace, deploy only SnmpCollector with the existing OTel Collector + Prometheus + Grafana stack, and validate end-to-end metric flow.
**Depends on:** Phase 8
**Plans:** 3 plans in 2 waves

Plans:
- [x] 09-01-PLAN.md — Fix monitoring stack: OTel Collector prometheusremotewrite + Prometheus remote_write receiver (Wave 1)
- [x] 09-02-PLAN.md — Create SnmpCollector K8s manifests: ConfigMap, Deployment (3 replicas), Service (Wave 1)
- [x] 09-03-PLAN.md — Deployment guide + human verification: build, deploy, validate metrics and leader failover (Wave 2)

### Phase 10: Metrics Redesign
**Goal:** Redesign the SNMP trap and poll paths to use a `Simetra.{DeviceName}` community string convention for both authentication and device identity, replace `site_name` with `host_name` from the machine hostname, simplify channel architecture from per-device to single shared channel, update readiness checks, and ensure consistent metric labeling across traps and polls.
**Depends on:** Phase 9
**Success Criteria** (what must be TRUE):
  1. Traps from any IP with a valid `Simetra.*` community string are accepted and produce metrics with correct `device_name` label extracted from the community string
  2. Traps with invalid community string (no `Simetra.` prefix) are dropped with Debug-level log
  3. Polls use per-device configured CommunityString (validated as Simetra.* at startup) and Port
  4. All metric labels use `host_name` (from NODE_NAME env var / MachineName) instead of `site_name`, and `device_name` + `ip` instead of `agent`
  5. Empty `Devices[]` config is valid -- pod starts and accepts traps without poll configuration
  6. All tests pass with new label taxonomy, community string convention, and single channel architecture
  7. sysUpTime is not automatically prepended to poll requests -- users add it to OID lists if needed
**Plans:** 7 plans in 5 waves

Plans:
- [x] 10-01-PLAN.md — Config cleanup: SiteOptions optional, remove CommunityString fields, CommunityStringHelper, DeviceInfo/DeviceRegistry refactor (Wave 1)
- [x] 10-02-PLAN.md — Metric label taxonomy: site_name -> host_name, agent -> device_name + ip across all instruments and telemetry (Wave 2)
- [x] 10-03-PLAN.md — Trap path rewrite: single shared ITrapChannel, community string convention, no device registry dependency (Wave 2)
- [x] 10-04-PLAN.md — Poll path alignment, readiness check, ValidationBehavior, DI wiring, build fix (Wave 3)
- [x] 10-05-PLAN.md — Test updates: all test files updated for new interfaces, labels, and channel architecture (Wave 4)
- [x] 10-06-PLAN.md — GAP: Fix host_name to use NODE_NAME env var (K8s Downward API spec.nodeName) for persistent server identity (Wave 5)
- [x] 10-07-PLAN.md — GAP: Add per-device Port and CommunityString config, Simetra.* validation, remove sysUpTime prepend (Wave 5)

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Infrastructure Foundation | 5/5 | Complete | 2026-03-05 |
| 2. Device Registry and OID Map | 4/4 | Complete | 2026-03-05 |
| 3. MediatR Pipeline and Instruments | 6/6 | Complete | 2026-03-05 |
| 4. Counter Delta Engine | 4/4 | Complete | 2026-03-05 |
| 5. Trap Ingestion | 4/4 | Complete | 2026-03-05 |
| 6. Poll Scheduling | 4/4 | Complete | 2026-03-05 |
| 7. Leader Election and Role-Gated Export | 5/5 | Complete | 2026-03-05 |
| 8. Graceful Shutdown and Health Probes | 5/5 | Complete | 2026-03-05 |
| 9. Containerized Integration Testing | 3/3 | Complete | 2026-03-05 |
| 10. Metrics Redesign | 7/7 | Complete | 2026-03-06 |
