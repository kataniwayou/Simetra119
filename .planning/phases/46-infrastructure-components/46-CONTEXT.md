# Phase 46: Infrastructure Components - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Build suppression cache, SnapshotJobOptions, ISnmpClient.SetAsync, and 3 command pipeline counters as independently testable components. No SnapshotJob or CommandWorker in this phase — those consume these components in Phases 47-48.

</domain>

<decisions>
## Implementation Decisions

### Suppression cache behavior
- Standalone singleton: `ISuppressionCache` interface + `SuppressionCache` class — injectable, testable
- Lazy expiry only — no sweep, no cleanup on tenant reload. Dead entries from deleted tenants expire naturally (harmless, never matched again)
- `TrySuppress(key, windowSeconds)` — window passed at check time (not stored in entry). If tenant config changes window, next check uses new value immediately.
- Return semantics: `true = suppressed (skip command)`, `false = not suppressed (proceed)`
- Stamps timestamp on every allowed call (returns false) — suppression window restarts from each command send
- Exposes `Count` property for diagnostics — no other introspection
- Keyed by `"{Ip}:{Port}:{CommandName}"` string

### SetAsync value dispatch
- Value+ValueType validation at **config load time** (TenantVectorWatcherService) — invalid entries skipped with Error log. CommandWorker receives pre-validated data.
- Supported ValueTypes: **Integer32, OctetString, IP** — covers all OBP and NPB device command types (INTEGER enums via Integer32, DisplayString via OctetString, IpAddress via IP)
- `ISnmpClient.SetAsync` accepts a **single Variable** (not a list) — one OID per call, matches CommandSlotOptions (one command = one OID)
- Returns `IList<Variable>` — mirrors GetAsync pattern, caller gets SET response varbinds for pipeline dispatch
- SET timeout: same multiplier pattern as polls — `IntervalSeconds × TimeoutMultiplier` with default 0.8. For SnapshotJob: `15 × 0.8 = 12s`
- `ParseSnmpData(value, valueType)` as **static helper on SharpSnmpClient** — keeps SNMP concerns together
- SharpSnmpLib types: `Integer32` → `new Integer32(int.Parse(value))`, `OctetString` → `new OctetString(value)`, `IpAddress` → `new IP(value)`

### SnapshotJobOptions defaults
- Config section: `"SnapshotJob"` — matches HeartbeatJob, CorrelationJob naming convention
- `IntervalSeconds`: default **15** (from spec)
- `TimeoutMultiplier`: default **0.8** — global on SnapshotJobOptions (not per-tenant), same as poll timeout pattern
- `SuppressionWindowSeconds` is NOT on SnapshotJobOptions — it's per-tenant on TenantOptions (default 60s, decided in Phase 45 context)
- Validation: `ValidateDataAnnotations` + `ValidateOnStart` — Range attributes on IntervalSeconds (1–300) and TimeoutMultiplier (0.1–0.9)

### Claude's Discretion
- Exact ISuppressionCache interface method signatures beyond TrySuppress and Count
- PipelineMetricService counter method naming (IncrementCommandSent vs IncrementCommandXxx)
- Test organization and fixture patterns for suppression cache tests
- Whether to add a SnapshotJobOptionsValidator or rely on DataAnnotations only

</decisions>

<specifics>
## Specific Ideas

- The poll timeout pattern (`intervalSeconds * pollGroup.TimeoutMultiplier`) is at MetricPollJob.cs line 93 — SET timeout should follow the identical pattern
- SharpSnmpLib IP type is `Lextm.SharpSnmpLib.IP` (NOT `IpAddress`) — CS0246 if wrong name used
- SharpSnmpLib timeout exception is `Lextm.SharpSnmpLib.Messaging.TimeoutException` (NOT `System.TimeoutException`) — needs using alias to avoid CS0104

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 46-infrastructure-components*
*Context gathered: 2026-03-16*
