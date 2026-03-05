# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-04)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** All 8 phases complete — v1 milestone done. Ready for audit.

## Current Position

Phase: 8 of 8 (Graceful Shutdown and Health Probes) — Complete
Plan: 5 of 5 complete
Status: All phases done — 137 tests passing (0 failures). Phase 8 added 23 new tests for health checks, liveness vector, job interval registry, graceful shutdown.
Last activity: 2026-03-05 — Completed Phase 8 (graceful shutdown + health probes). All v1 requirements delivered.

Progress: [████████████████████] 100% (37/37 plans across all phases)

## Performance Metrics

**Velocity:**
- Total plans completed: 37
- Average duration: ~3-5 min
- Total execution time: ~120 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-infrastructure-foundation | 5 | ~20 min | ~4 min |
| 02-device-registry-and-oid-map | 4 | ~14 min | ~3.5 min |
| 03-mediatr-pipeline-and-instruments | 6 | ~24 min | ~4 min |
| 04-counter-delta-engine | 4 | ~5 min | ~1.3 min |
| 05-trap-ingestion | 4 | ~31 min | ~7.75 min |
| 06-poll-scheduling | 4 | ~14 min | ~3.5 min |
| 07-leader-election-and-role-gated-export | 5 | ~15 min | ~3 min |
| 08-graceful-shutdown-and-health-probes | 5 | ~15 min | ~3 min |

**Recent Trend:**
- All 37 plans complete across 8 phases
- Trend: Consistent ~2-8 min execution

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Init]: MediatR 12.5.0 (MIT) locked — do not upgrade to v13+ (RPL-1.5 license)
- [Init]: SNMPv2c only — no v3 auth/USM
- [Init]: All instances poll and receive traps — leader election gates metric export only
- [Init]: Counter delta engine is its own phase (Phase 4) — correctness risk, no shortcuts
- [Init]: OTel cardinality must be locked in Phase 2 before any instruments are created in Phase 3
- [01-01]: Microsoft.NET.Sdk (Generic Host) not Microsoft.NET.Sdk.Web — SnmpCollector has no HTTP surface
- [01-01]: Quartz.Extensions.Hosting (not Quartz.AspNetCore) — correct package for Generic Host
- [01-01]: SiteOptions.Role = "standalone" default replaces ILeaderElection for Phase 1; Phase 7 makes it dynamic
- [01-01]: Microsoft.Extensions.Hosting 9.0.0 added explicitly — non-Web SDK requires it for Host.CreateApplicationBuilder
- [01-01]: OtlpOptions.ServiceName defaults to "snmp-collector" (not "simetra-supervisor")
- [01-02]: prometheusremotewrite exporter used (not prometheus scrape exporter) — PUSH-03 compliance
- [01-02]: otel/opentelemetry-collector-contrib image required (not core) — prometheusremotewrite is contrib-only
- [01-02]: Prometheus --web.enable-remote-write-receiver flag mandatory — without it all remote_write pushes rejected with HTTP 405
- [01-02]: resource_to_telemetry_conversion.enabled: true propagates OTel resource attributes as Prometheus labels
- [01-03]: SnmpConsoleFormatter shows BOTH globalId and operationId (Simetra showed operationId OR globalId)
- [01-03]: SnmpLogEnrichmentProcessor takes string role not Func<string> — static Phase 1, dynamic Phase 7
- [01-03]: TelemetryConstants has single MeterName (no LeaderMeterName/InstanceMeterName split — no role-gating in Phase 1)
- [01-03]: AsyncLocal<string?> _operationCorrelationId MUST be static — instance AsyncLocal does not flow through async context
- [01-04]: No WithTracing in AddSnmpTelemetry (LOG-07: SnmpCollector emits no distributed traces)
- [01-04]: Direct AddOtlpExporter on metrics — no MetricRoleGatedExporter in Phase 1 (no leader election)
- [01-04]: Microsoft.Extensions.Options.DataAnnotations 9.0.0 required separately — ValidateDataAnnotations() not in base Options package
- [01-04]: OptionsValidationException catch wraps host.RunAsync(), not builder.Build() — ValidateOnStart fires during RunAsync host startup
- [01-05]: IValidateOptions custom messages use "Section:Field is required" format for operational clarity — all future validators should follow same pattern
- [01-05]: SiteOptions validation fires during builder.Build() (DI init for OTel logging processor), not during RunAsync — "Configuration validation failed:" prefix not shown for SiteOptions path; this is pre-existing behavior, not a bug
- [02-01]: DeviceOptions.CommunityString is string? nullable — per-device override, null/empty falls back to SnmpListenerOptions.CommunityString; no validation needed (intentionally optional)
- [02-01]: MetricPollOptions.Oids is List<string> (plain OID strings) — no OidEntryOptions wrapper; TypeCode determined at runtime from SNMP GET response
- [02-01]: DeviceType removed from DeviceOptions entirely — SnmpCollector is device-agnostic, flat OID map replaces device modules
- [02-01]: IPAddress.TryParse used in DevicesOptionsValidator for IP format check — catches hostnames/typos at startup
- [02-02]: DevicesOptions uses Configure<IConfiguration> delegate (not .Bind()) — JSON "Devices" is array; .Bind(GetSection("Devices")) maps array index keys as POCO property names, silently leaving Devices list empty
- [02-02]: OidMapOptions uses Configure<IConfiguration> delegate — GetSection("OidMap").Bind(opts.Entries) maps flat JSON object keys to Dictionary<string,string>
- [02-02]: AllDevices returns _byIp.Values (not separate ordered list) — adequate for Phase 6 scheduler which needs to enumerate, not order, devices
- [02-02]: OidMapService diff logging at Information level (not Debug) — config changes are operator-relevant events
- [02-04]: IHostedLifecycleService.StartingAsync used (not IHostedService.StartAsync) — fires before Quartz QuartzHostedService.StartAsync, ensuring audit completes before any jobs run
- [02-04]: CardinalityAuditService WarningThreshold = 10,000 series — Prometheus performance bound from RESEARCH.md
- [02-04]: OID dimension = max(oidMapEntries, uniquePollOids) — traps may send OIDs absent from poll groups; OID map is the correct upper bound
- [03-01]: SnmpOidReceived is a sealed class not a record — behaviors enrich properties in-place; AgentIp uses set not init (trap path may update post-construction)
- [03-01]: System.Diagnostics using required for TagList alongside System.Diagnostics.Metrics — TagList is in System.Diagnostics namespace (DiagnosticSource package, transitive via OTel)
- [03-01]: PipelineMetricService takes IMeterFactory not Meter directly — follows OTel hosting pattern where factory manages meter lifetime
- [03-02]: LoggingBehavior pattern-matches notification is SnmpOidReceived before logging — other notification types pass through silently to next()
- [03-02]: ExceptionBehavior returns default! (not Unit.Value) — TResponse is generic; default! is safe for both Unit and any other TResponse
- [03-02]: ExceptionBehavior always wraps next() in try/catch regardless of notification type — pipeline guard is universal, not type-gated
- [03-03]: ValidationBehavior checks msg.DeviceName is null before calling TryGetDevice — poll path sets DeviceName at publish time; only trap path needs registry lookup
- [03-03]: OidResolutionBehavior always calls next() even when MetricName resolves to Unknown sentinel — handlers decide what to do with unresolved OIDs; no silent data loss
- [03-03]: Rejection uses return default! not throw — avoids triggering error counter path and keeps overhead low for rejected-but-not-exceptional events
- [03-04]: ISnmpData has no shared numeric accessor -- cast to concrete Integer32/Gauge32/TimeTicks per switch arm; safe because switch is already discriminated on TypeCode
- [03-04]: snmp_gauge and snmp_info stored as object in ConcurrentDictionary<string, object> -- Gauge<T> and Counter<T> share no common generic base in .NET OTel
- [03-04]: Counter32/Counter64 deferred to Phase 4: LogDebug emitted, IncrementHandled NOT called, no metric recorded
- [03-04]: snmp_info value label truncated at 128 chars (125 + "...") to bound OTel label cardinality
- [03-05]: AddSnmpPipeline inserted after AddSnmpConfiguration — OidResolutionBehavior/ValidationBehavior depend on IOidMapService/IDeviceRegistry registered by AddSnmpConfiguration
- [03-06]: SnmpOidReceived changed from INotification to IRequest<Unit> — MediatR IPipelineBehavior only fires for IRequest<T> via ISender.Send; INotification via IPublisher.Publish bypasses all behaviors entirely (silent dead code bug)
- [03-06]: OtelMetricHandler changed from INotificationHandler to IRequestHandler<SnmpOidReceived, Unit> — required for ISender.Send dispatch path
- [03-06]: Behavior constraints changed from 'where T : INotification' to 'where T : notnull' — required since SnmpOidReceived no longer implements INotification
- [03-06]: TaskWhenAllPublisher removed from AddSnmpPipeline — not applicable to IRequest<Unit> request/response pipeline
- [03-06]: RequestHandlerDelegate<TResponse> in MediatR 12.5.0 takes CancellationToken parameter — test lambdas must use 'ct =>' not '() =>'
- [03-06]: Phase 5/6 MUST use ISender.Send(snmpOidReceived) not IPublisher.Publish — IPublisher.Publish bypasses the entire behavior pipeline
- [03-gap]: LoggingBehavior now takes PipelineMetricService and calls IncrementPublished() for every SnmpOidReceived — closes PMET-01 (snmp.event.published counter)
- [03-gap]: SnmpMetricFactoryTests uses MeterListener on real SnmpMetricFactory to verify all 5 OTel tags including site_name — closes SC#1 label coverage
- [04-01]: SysUpTimeCentiseconds is uint? nullable on SnmpOidReceived — null means unavailable; delta engine conservatively treats current < previous as reboot when null
- [04-01]: RecordCounter last param named delta (not value) — signals it is a computed difference, not a raw SNMP reading
- [04-01]: snmp_counter instrument name for Counter<double> — follows snmp_gauge/snmp_info naming convention
- [04-02]: Counter32 wrap casts previousValue to uint before subtraction — avoids 64-bit arithmetic inflating the wrap delta when previous is stored as ulong
- [04-02]: Counter64 current < previous always treated as reboot — 64-bit rollover in practice takes years at max SNMPv2c rates; conservative reboot treatment is correct
- [04-02]: sysUpTime keyed by agent (not oid|agent) — device reboot resets all OID counters simultaneously; one uptime per device is the correct granularity
- [04-02]: AddOrUpdate closure captures previousValue as ulong? — null = add path (first poll); non-null = update path; single atomic ConcurrentDictionary operation
- [04-04]: CounterDeltaEngine tests use NullLogger (not mock) — log output not under test; tests focus on CounterRecords list assertions only
- [04-04]: xUnit creates new class instance per test — each test gets a fresh CounterDeltaEngine with empty ConcurrentDictionary state; no test isolation setup required
- [05-01]: SingleWriter=false on BoundedChannelOptions — multiple concurrent UDP receive callbacks may write to the same device channel; single-writer optimization unsafe
- [05-01]: DropCounter sealed class with Interlocked.Increment on long field — ConcurrentDictionary<string,long> cannot use Interlocked.Increment(ref dict[key]) in C#; DropCounter wrapper enables lock-free increment
- [05-01]: Warning logged every 100 drops per device — bounds log volume during trap storms while maintaining visibility
- [05-01]: device_name tag on snmp.trap.dropped only — auth_failed and unknown_device use site_name only (device not yet known at those rejection points)
- [05-02]: Device lookup (TryGetDevice) ordered before community auth — DeviceInfo holds expected community string; auth impossible before lookup
- [05-02]: MapToIPv4() called on RemoteEndPoint.Address — dual-stack hosts may produce IPv6-mapped IPv4 addresses; IDeviceRegistry is keyed on IPv4
- [05-02]: UserRegistry created once in constructor — SharpSnmpLib requires it even for v2c; reuse avoids allocation per datagram
- [05-02]: ProcessDatagram is synchronous — ChannelWriter<T>.TryWrite is non-blocking; async would add latency with no benefit on hot trap path
- [05-02]: StopAsync: base.StopAsync first (cancels ExecuteAsync), then CompleteAll — ensures producer stops before consumers are signaled to drain
- [05-03]: ISender.Send used (not IPublisher.Publish) in ChannelConsumerService — SnmpOidReceived is IRequest<Unit>; IPublisher.Publish bypasses IPipelineBehavior entirely
- [05-03]: IncrementTrapReceived called BEFORE ISender.Send — counts varbinds entering pipeline, not handler success
- [05-03]: OperationCanceledException break ordered before general Exception catch — avoids treating cancellation as a warning during normal host shutdown
- [05-03]: DeviceName from VarbindEnvelope.DeviceName (pre-resolved at listener time) — no double device registry lookup in consumer
- [05-04]: ProcessDatagram changed private -> internal with InternalsVisibleTo — testable without exposing to production callers
- [05-04]: [Collection(NonParallelCollection.Name)] with DisableParallelization=true for MeterListener-using test classes — MeterListener is a global .NET runtime listener; parallel tests with same meter name cause cross-contamination of measurement lists
- [05-04]: CapturingChannelManager uses ChannelWriter subclass (TryWrite captures to list) — gives exact synchronous write capture without buffering or async complexity
- [05-04]: WaitForAsync polling (10ms intervals, 5s timeout) for BackgroundService consumer tests — more reliable than fixed delays
- [06-01]: DeviceUnreachabilityTracker threshold hardcoded at 3 (not configurable) per locked CONTEXT.md decision
- [06-01]: OrdinalIgnoreCase on ConcurrentDictionary<string, DeviceState> — device names are user-configured strings that may vary in case between configuration files and Quartz JobDataMap usage
- [06-01]: Singleton tracker (not per-job instance field) — Quartz DI creates new job instance per execution; per-job state is lost between runs
- [06-01]: Inner DeviceState class avoids struct-update atomicity issues in ConcurrentDictionary; volatile int + Interlocked for lock-free counting without locks
- [06-01]: RecordFailure/RecordSuccess return true ONLY on state transition (not on every call) — MetricPollJob (Plan 02) uses return value to decide whether to fire OTel counter and log transition
- [06-02]: Device lookup failure returns before try block — config errors must NOT increment snmp.poll.executed; only actual poll attempts count
- [06-02]: sysUpTime varbind own SnmpOidReceived carries SysUpTimeCentiseconds=null — extracted in same iteration but local is null at dispatch time; subsequent OIDs carry the extracted value
- [06-02]: noSuchObject/noSuchInstance/EndOfMibView logged at Debug (not Warning) — expected for devices not exposing all OIDs; Warning would cause noise
- [06-02]: Bare OperationCanceledException re-thrown (host shutdown) — Quartz needs to see cancellation for graceful shutdown; swallowing it makes job report success on shutdown
- [06-03]: Thread pool maxConcurrency = 1 (CorrelationJob) + sum(device.MetricPolls.Count) — 1:1 thread-per-job, no starvation possible
- [06-03]: DevicesOptions bound eagerly (pre-DI) to devicesOptions.Devices — DI container not yet built when AddQuartz lambda runs; same pattern as AddSnmpConfiguration
- [06-03]: for loops (not foreach) inside AddQuartz lambdas — prevents C# lambda closure capture bug on loop variables
- [06-03]: PollSchedulerStartupService committed with ServiceCollectionExtensions in one commit — build requires both files simultaneously
- [06-04]: ISnmpClient wraps static Messenger.GetAsync — interface injection pattern for MetricPollJob testability; SharpSnmpClient registered as singleton in AddSnmpPipeline
- [06-04]: sysUpTime extraction must happen AFTER dispatch in DispatchResponseAsync loop — extract BEFORE assigned the wrong value (500) to the sysUpTime varbind's own SnmpOidReceived; intent is null for its own message, extracted value for subsequent OIDs
- [06-04]: CapturingSender implements ISender via explicit interface implementation — ISender.Send<TRequest> requires `where TRequest : IBaseRequest` constraint; implicit implementation constraint mismatch causes CS0425
- [06-04]: StubJobExecutionContext must implement Put(object,object), Get(object), JobInstance (IJob) — these are required IJobExecutionContext members not obvious from MetricPollJob usage alone
- [06-04]: EmptyAsyncEnumerable<T> inline helper used for ISender.CreateStream overloads — avoids System.Linq.Async dependency
- [07-01]: Two-meter architecture: MeterName ("SnmpCollector") exported by all instances for pipeline health; LeaderMeterName ("SnmpCollector.Leader") exported only by leader for business metrics (snmp_gauge, snmp_counter, snmp_info)
- [07-01]: AlwaysLeaderElection is sealed with expression-body properties — tests needing custom behavior should mock ILeaderElection directly
- [07-01]: LeaseOptions defaults: Name="snmp-collector-leader", Namespace="default" (SnmpCollector-specific, not Simetra-inherited)
- [07-01]: SiteOptions.PodIdentity is nullable string — Plan 04 DI wiring will PostConfigure from HOSTNAME env var (K8s pod name), fallback to Environment.MachineName
- [07-02]: K8sLeaseElection ported directly from Simetra reference with namespace swap only — no structural differences
- [07-02]: StopAsync calls base.StopAsync first (cancels stoppingToken) before lease delete — prevents race where election loop re-acquires immediately after delete
- [07-02]: RenewDeadline = DurationSeconds - 2 (fixed 2s window before TTL) — matches Simetra reference pattern
- [07-03]: MetricRoleGatedExporter accepts BaseExporter<Metric> inner (not OtlpMetricExporter) — generic type enables testing with stub exporters without real OTLP endpoint
- [07-03]: ExportResult.Success for empty follower batch — Failure triggers OTel SDK exponential retry; intentional suppression is not a network error
- [07-03]: ParentProvider reflection: one-time lazy SetValue in first Export call — SDK sets ParentProvider after constructor runs; only reflection can reach internal setter
- [07-03]: SnmpMetricFactory meter change: meterFactory.Create(TelemetryConstants.LeaderMeterName) is the ONLY change — instruments inherit meter name from Meter object automatically
- [07-03]: PipelineMetricService left on MeterName (unchanged) — pipeline counters must export from ALL instances regardless of leader status
- [07-04]: Tasks 1+2 committed atomically — SnmpLogEnrichmentProcessor Func<string> signature required before ServiceCollectionExtensions compiles (CS1660 if done separately)
- [07-04]: k8s namespace fully-qualified in ServiceCollectionExtensions — avoids `using k8s;` at file level; K8s types used only in one conditional block
- [07-04]: LeaseOptions binding inside IsInCluster() branch only — binding with ValidateOnStart outside K8s branch would cause startup failures in local dev
- [07-04]: appsettings.json Lease section always present — documents config surface; binding only activates in K8s path so values safely ignored in local dev
- [07-05]: MeterProvider-based approach for MetricRoleGatedExporter tests — Metric is a sealed SDK class; constructing it directly is unsupported; real pipeline with ForceFlush() is more realistic
- [07-05]: CapturingExporter.ExportCallCount == 0 asserts all-gated follower case — inner exporter not called; Success returned without forwarding (not Failure which triggers OTel retry)
- [07-05]: AlwaysLeaderElection used as K8sLeaseElection stand-in in DI singleton test — tests the registration PATTERN not K8s cluster behavior; avoids requiring live cluster in unit tests

### Pending Todos

None yet.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 001 | Verify SNMPv2c only (SNMPv3 not supported) | 2026-03-05 | — (verification only) | [001-verify-snmpv2c-only](./quick/001-verify-snmpv2c-only/) |

### Blockers/Concerns

- [Phase 7] MetricRoleGatedExporter reflection ParentProvider propagation confirmed working in OTel 1.15.0. ParentProvider breakage detection test added in 07-05: any future OTel SDK upgrade that removes/renames the property will surface immediately in CI.

## Session Continuity

Last session: 2026-03-05
Stopped at: Phase 8 complete (5/5 plans done). All 8 phases complete. v1 milestone ready for audit.
Resume file: None
