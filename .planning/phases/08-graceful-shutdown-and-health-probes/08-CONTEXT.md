# Phase 8: Graceful Shutdown and Health Probes - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

The application shuts down cleanly within 30 seconds under SIGTERM — releasing the K8s lease, draining in-flight work, and flushing telemetry — and K8s health probes (startup, readiness, liveness) correctly reflect application readiness and liveness via HTTP endpoints. GracefulShutdownService orchestrates the 5-step shutdown sequence with per-step CTS budgets. Health probes require adding a minimal Kestrel HTTP surface for K8s to call.

</domain>

<decisions>
## Implementation Decisions

### Shutdown step ordering
- Step sequence and potential overlap: Claude decides based on dependency analysis between steps (lease release must precede drain, drain must precede flush, etc.)
- Step timeout behavior: Claude decides the safest approach — cancel via CTS and move on, or log warning and continue
- Orchestration pattern: Claude decides based on how existing services (K8sLeaseElection, SnmpTrapListenerService, Quartz) already implement StopAsync — central orchestrator vs coordinated hybrid
- Telemetry flush scope: Claude decides based on .NET host shutdown behavior — SIGTERM only vs attempt on crash

### Health probe behavior
- Transport mechanism: Claude decides based on K8s probe capabilities and SnmpCollector's Generic Host architecture (minimal Kestrel vs TCP socket vs other)
- Liveness threshold: Claude decides based on K8s restart behavior and false-positive risk — single stale job vs majority stale vs configurable
- Grace multiplier value: Claude decides based on poll interval ranges and K8s liveness probe timing conventions
- Health endpoint port configuration: Claude decides based on K8s conventions and operational needs

### Drain semantics
- Drain scope: Claude decides what constitutes "in-flight" — channel consumers, MediatR handlers, Quartz jobs, or all of the above
- Drain timeout behavior: Claude decides whether to force-cancel via CTS or abandon silently when 8s budget exceeded
- Counter delta engine state: Claude decides based on Phase 4 semantics — okay to lose last baseline (re-established on next startup) vs flush final delta
- Channel consumer drain: Claude decides based on channel semantics and trap storm risk — drain remaining buffered varbinds vs stop immediately

### Observability during shutdown
- Log levels for shutdown steps: Claude decides based on operational value of each step
- Shutdown OTel metrics: Claude decides based on whether metrics can reliably flush during the shutdown sequence itself
- Console logging during shutdown: Claude decides based on operational needs (kubectl logs visibility)
- Failed step log level: Claude decides based on severity and whether step failure affects other pods
- Shutdown summary log: Claude decides based on operational value

### Claude's Discretion
- All shutdown step ordering and overlap decisions
- All health probe implementation decisions (transport, thresholds, port, grace multiplier)
- All drain scope and timeout decisions
- All observability decisions (log levels, metrics, console behavior)
- Health check class structure and registration pattern
- Job interval registry implementation for staleness calculation
- Liveness vector data structure and stamping mechanism
- CorrelationJob integration with job interval registry (if applicable)

</decisions>

<specifics>
## Specific Ideas

- ROADMAP SC#5 locks: liveness vector stamped by every job in finally block
- ROADMAP SC#3 locks: startup probe returns healthy ONLY after OID map loaded AND poll definitions registered
- ROADMAP SC#4 locks: readiness probe returns healthy ONLY when trap listener is bound on UDP 162 AND device registry is populated
- ROADMAP SC#1 locks: each shutdown step logged with its outcome
- SHUT-01 locks: GracefulShutdownService registered last in DI (stops first)
- SHUT-06 locks: telemetry flush uses independent CTS — always runs regardless of prior step outcomes
- SHUT-07 locks: each step has its own CancellationTokenSource budget
- SHUT-08 locks: total shutdown timeout 30 seconds
- Phase 7 decision [07-02]: K8sLeaseElection.StopAsync already deletes lease on shutdown — Phase 8 Step 1 should coordinate with this existing behavior (not duplicate it)
- Phase 1 decision [01-01]: Microsoft.NET.Sdk (Generic Host) not Microsoft.NET.Sdk.Web — adding health probes requires consideration of how to add HTTP without switching SDK

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 08-graceful-shutdown-and-health-probes*
*Context gathered: 2026-03-05*
