# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.8 Combined Metrics — Phase 39 (Pipeline Bypass Guards)

## Current Position

Phase: 39 of 40 (Pipeline Bypass Guards)
Plan: 01 of 1 (complete)
Status: Phase 39 complete — ready for Phase 40
Last activity: 2026-03-15 — Completed 39-01-PLAN.md (SnmpSource.Synthetic + OidResolutionBehavior bypass guard)

Progress: [####################] v1.0-v1.7 complete | [███░░░░░░░] 3/4 v1.8 phases

## Performance Metrics

**Velocity:**
- Total plans completed: 90 (v1.0 through v1.7, plus 37-01 through 39-01)
- Average duration: ~25 min
- Total execution time: ~36.7 hours

**Recent Trend:**
- Last milestone (v1.7): 8 plans, 4 phases
- 37-01: 2 min (purely additive types, no behavior)
- 39-01: 2 min (surgical: 3 lines production code, 4 new tests)
- Trend: Stable

*Updated after each plan completion*

## Accumulated Context

### Key Architectural Facts (v1.8 relevant)

- All pods maintain tenant vector state (no leader gating) — synthetic metrics route identically to poll metrics
- OidResolutionBehavior: has bypass guard `if (msg.Source == SnmpSource.Synthetic) { return await next(); }` — synthetic messages skip OID resolution, MetricName preserved as set at publish time (COMPLETE: Phase 39)
- DeviceWatcherService.ValidateAndBuildDevicesAsync: internal static async — all per-entry validation happens here (mirrors v1.7 watcher pattern)
- MetricPollInfo is a positional record — `AggregatedMetrics` is an init-only property (not positional param) with default `[]`, source-compatible with all existing construction sites (CONFIRMED: 286 pre-existing tests pass unchanged)
- `SnmpSource` enum has three members: `Poll`, `Trap`, `Synthetic` (COMPLETE: Phase 39)
- Aggregator terms: `"sum"`, `"subtract"`, `"absDiff"`, `"mean"` (lowercase; case-insensitive matching at load time)
- TypeCode selection: Subtract/AbsDiff → Integer32 (signed, result can be negative); Sum/Mean → Gauge32 (unsigned)
- Sentinel OID: `"0.0"` passes existing ValidationBehavior OID regex without guard; Prometheus label will show `oid="0.0"`
- Bypass guard decision: Option B — guard on `Source == SnmpSource.Synthetic` (COMPLETE: Phase 39)
- Ratio aggregation: excluded from v1.8 — `AggregationKind.Ratio` may exist in enum but BuildPollGroups treats it as invalid Action
- Synthetic messages must have `DeviceName` set at publish time — ValidationBehavior runs before OidResolutionBehavior (pipeline order: Logging → Exception → Validation → OidResolution)

### Phase 37 Decisions

- `AggregatedMetrics` on `MetricPollInfo` is an **init-only property** (not positional param) with default `[]` — all existing construction sites unchanged
- No `[JsonPropertyName]` attributes on new PollOptions fields — `PropertyNameCaseInsensitive = true` in existing deserializer covers it
- `CombinedMetricDefinition` is a `sealed record` for value equality and immutability
- `AggregationKind` enum does NOT include `Ratio` in v1.8 (excluded per pre-phase decision)
- Confirmed: `Enum.TryParse<AggregationKind>("absDiff", ignoreCase: true)` = AbsDiff (camelCase preserved)

### Phase 38 Decisions

- **resolvedOids.Count < 2** for minimum-2 check (not MetricNames.Count) — prevents CombinedMetricDefinition with fewer SourceOids than configured names at poll time
- OID map collision = **Error + skip** combined metric (not Warning) — real metric takes unconditional priority
- Invalid combined metric **never skips the poll group** — result.Add always executes; only combinedMetric is null
- `seenAggregatedNames` HashSet uses `StringComparer.Ordinal` — metric names are case-sensitive identifiers, scoped to per-device BuildPollGroups call

### Phase 39 Decisions

- **Option B bypass guard:** `Source == SnmpSource.Synthetic` (consistent, unambiguous, future-safe)
- **Sentinel OID "0.0":** passes existing OID regex `^\d+(\.\d+){1,}$` without any ValidationBehavior changes
- **Bypass return form:** `return await next();` — Handle returns `Task<TResponse>`, result must be returned explicitly
- **Guard placement:** first statement inside `if (notification is SnmpOidReceived msg)` block, before `_oidMapService.Resolve`

### v1.8 Pre-Phase Decisions (record in plans)

- ~~Phase 39: Name the bypass guard option (Option B: `Source == SnmpSource.Synthetic`) as a named decision~~ (DONE)
- ~~Phase 39: Name the sentinel OID value (`"0.0"`) as a named decision~~ (DONE)
- Phase 40: Ratio is an invalid Action value in v1.8 — BuildPollGroups skips with Error log (same as unknown string)

### Known Tech Debt

None.

### Blockers/Concerns

None. Phase 40 has HIGH confidence per research summary.

## Session Continuity

Last session: 2026-03-15T09:37:25Z
Stopped at: Completed 39-01-PLAN.md — SnmpSource.Synthetic + OidResolutionBehavior bypass guard, 4 new tests, 316 total passing
Resume file: None
