# Requirements: SNMP Monitoring System

**Defined:** 2026-03-16
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.0 Requirements

Requirements for the Tenant Evaluation & Control milestone. Each maps to roadmap phases.

### Structural Prerequisites

- [ ] **SNAP-01**: `SnmpSource.Command` enum value added — SET response varbinds carry `source=Command` through the full MediatR pipeline; OidResolutionBehavior does NOT bypass for Command (unlike Synthetic)
- [ ] **SNAP-02**: `MetricSlotHolder.Role` property populated from `MetricSlotOptions.Role` during `TenantVectorRegistry.Reload` — SnapshotJob can partition holders into Evaluate vs Resolved at runtime
- [ ] **SNAP-03**: `Tenant.Commands` property (`IReadOnlyList<CommandSlotOptions>`) populated from `TenantOptions.Commands` during `TenantVectorRegistry.Reload` — SnapshotJob Tier 4 can access command targets per tenant

### Evaluation Engine

- [ ] **SNAP-04**: SnapshotJob Tier 1 — metric staleness detection: for each MetricSlotHolder (excluding Source=Trap and IntervalSeconds=0), staleness = `(Now - ReadSlot().Timestamp) > IntervalSeconds × GraceMultiplier`; if ANY metric stale → skip to Tier 4 (command queue); ReadSlot() null (never written) treated as "no data yet", not stale
- [ ] **SNAP-05**: SnapshotJob Tier 2 — Resolved metrics threshold check: check ALL Role=Resolved holders against Threshold (Min/Max); if ALL Resolved violated → END (no command); vacuous pass if no Resolved holders with thresholds
- [ ] **SNAP-06**: SnapshotJob Tier 3 — Evaluate metrics threshold check: check ALL Role=Evaluate holders against Threshold; if ALL Evaluate violated → continue to Tier 4 (commands); if NOT all violated → END (no command); vacuous fail if no Evaluate holders with thresholds (log Warning on startup)
- [ ] **SNAP-07**: Priority group traversal — same-priority tenants visited in parallel (Task.WhenAll); sequential across priority groups (lower value = higher priority); advance to next lower priority group ONLY if ALL tenants in current group reached Tier 4; stale tenants count as NOT violated for the advance gate

### Command Execution

- [ ] **SNAP-08**: `ISnmpClient.SetAsync` method added wrapping `Messenger.SetAsync(VersionCode.V2, endpoint, community, variables, ct)` — `SharpSnmpClient` delegates; ValueType dispatch: Integer32 → `new Integer32(int.Parse(value))`, OctetString → `new OctetString(value)`, IpAddress → `new IP(value)`
- [ ] **SNAP-09**: Suppression cache — `ConcurrentDictionary<string, DateTimeOffset>` singleton keyed by `"{Ip}:{Port}:{CommandName}"`; per-tenant `SuppressionWindowSeconds` config property; lazy TTL expiry (check age on access, no background sweep); `[DisallowConcurrentExecution]` eliminates check-then-suppress race
- [ ] **SNAP-10**: SET response dispatched through full MediatR pipeline — each response varbind creates `SnmpOidReceived{Source=SnmpSource.Command}`; OID resolved via existing OID map (NOT bypassed); recorded as `snmp_gauge`/`snmp_info` with `source="Command"`
- [ ] **SNAP-11**: `CommandWorkerService` — `BackgroundService` draining bounded `Channel<CommandRequest>` (DropWrite, TryWrite failure path); resolves community string from `IDeviceRegistry` at execution time (not enqueue time); resolves command OID from `ICommandMapService.ResolveCommandOid`; registered via Singleton-then-HostedService DI pattern

### Configuration

- [ ] **SNAP-12**: `SnapshotJobOptions` — `IntervalSeconds` (default 15), `TimeoutMultiplier` (default 0.8); bound from `"SnapshotJob"` config section; validated with `ValidateDataAnnotations` + `ValidateOnStart`. Note: `SuppressionWindowSeconds` is per-tenant on `TenantOptions` (default 60s), not on SnapshotJobOptions.

### Observability

- [ ] **SNAP-13**: 3 pipeline counters added to `PipelineMetricService` — `snmp.command.sent` (SET dispatched successfully), `snmp.command.failed` (SET SNMP error/timeout), `snmp.command.suppressed` (SET skipped by suppression cache); all with `device_name` tag
- [ ] **SNAP-14**: SnapshotJob liveness — stamps `ILivenessVectorService` with key `"snapshot"` in finally block; interval registered in `IJobIntervalRegistry`; `LivenessHealthCheck` detects staleness automatically
- [ ] **SNAP-15**: Structured evaluation logs per tenant per SnapshotJob run — Debug level for stale/resolved-gate/no-violation outcomes; Information level for command-dispatched outcomes; includes tenant ID, priority, tier reached, holder counts
- [ ] **SNAP-16**: Command execution logs with round-trip duration — Information level for success (device, command, duration ms); Warning level for failure (device, command, error, duration ms); uses `Stopwatch` around `SetAsync`
- [ ] **SNAP-17**: Operations dashboard — 3 `snmp.command.*` panels (sent, failed, suppressed) on a single row (w=8 each), added to Pipeline Counters group as Row 5

## Future Requirements

Deferred to future milestones. Tracked but not in current roadmap.

### Advanced Evaluation

- **EVAL-01**: Per-tenant evaluation outcome counters with `tenant_id` label (cardinality concern if > 20 tenants)
- **EVAL-02**: Suppression cache diagnostics exposed in health endpoint
- **EVAL-03**: Evaluation history / audit log persistence

### Command Extensions

- **CMD-01**: Parallel command execution (multiple workers draining channel concurrently)
- **CMD-02**: Command acknowledgment / confirmation flow
- **CMD-03**: Command retry with backoff (currently: next SnapshotJob cycle is the retry)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Automatic SET retry | Double-apply risk on slow devices; next SnapshotJob cycle serves as retry |
| Hysteresis / debouncing | Per-tenant suppression window is the damping mechanism |
| Cross-tenant command conflict resolution | Priority groups + suppression cache handle this |
| Follow-up GET after SET | SET response contains confirmed device value |
| Command queue persistence | SnapshotJob re-queues within one cycle after restart |
| Parallel command execution | Serial worker sufficient; avoids SNMP device concurrency issues |
| Leader-gated SnapshotJob | All replicas evaluate consistently; SET commands are idempotent (set-to-value, not toggle) |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SNAP-01 | Phase 45 | Complete |
| SNAP-02 | Phase 45 | Complete |
| SNAP-03 | Phase 45 | Complete |
| SNAP-04 | Phase 48 | Pending |
| SNAP-05 | Phase 48 | Pending |
| SNAP-06 | Phase 48 | Pending |
| SNAP-07 | Phase 48 | Pending |
| SNAP-08 | Phase 46 | Complete |
| SNAP-09 | Phase 46 | Complete |
| SNAP-10 | Phase 47 | Complete |
| SNAP-11 | Phase 47 | Complete |
| SNAP-12 | Phase 46 | Complete |
| SNAP-13 | Phase 46 | Complete |
| SNAP-14 | Phase 48 | Pending |
| SNAP-15 | Phase 48 | Pending |
| SNAP-16 | Phase 49 | Pending |
| SNAP-17 | Phase 49 | Pending |

**Coverage:**
- v2.0 requirements: 17 total
- Mapped to phases: 17
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-16*
*Last updated: 2026-03-16 after v2.0 roadmap created — all 17 requirements mapped*
