# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-04)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** Phase 3 — MediatR Pipeline (Phase 2 complete — 4/4 plans done)

## Current Position

Phase: 3 of 8 (MediatR Pipeline and Instruments) — In progress
Plan: 2 of 6 in phase 3
Status: In progress — 03-02 complete, next: 03-03 (OidResolutionBehavior)
Last activity: 2026-03-05 — Completed 03-02-PLAN.md (LoggingBehavior + ExceptionBehavior open-generic pipeline behaviors in Pipeline/Behaviors/)

Progress: [███░░░░░░░] 27% (11/40 plans across all phases estimated)

## Performance Metrics

**Velocity:**
- Total plans completed: 9
- Average duration: ~3-5 min
- Total execution time: ~35 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-infrastructure-foundation | 5 | ~20 min | ~4 min |
| 02-device-registry-and-oid-map | 4 | ~14 min | ~3.5 min |
| 03-mediatr-pipeline-and-instruments | 2 (of 6) | ~3 min | ~1.5 min |

**Recent Trend:**
- Last 10 plans: 01-01 through 01-05 (foundation), 02-01 through 02-04, 03-01 (pipeline foundation)
- Trend: Consistent ~2-6 min execution

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

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 6] MetricRoleGatedExporter uses reflection to set internal ParentProvider on BaseExporter<Metric> — verify against OTel 1.15.0 internals and add breakage-detection test during Phase 7 planning
- [Phase 4] Counter delta wrap-around and sysUpTime reboot detection require explicit unit test cases before any counter metrics reach Prometheus — design before coding

## Session Continuity

Last session: 2026-03-05T01:37:47Z
Stopped at: Completed 03-02-PLAN.md — LoggingBehavior (Debug OID/IP/Source) and ExceptionBehavior (catch-log-swallow + IncrementErrors) created in Pipeline/Behaviors/; build zero errors. Next: 03-03 (OidResolutionBehavior).
Resume file: None
