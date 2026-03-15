# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.8 Combined Metrics — Phase 37 (Config and Runtime Models)

## Current Position

Phase: 38 of 40 (DeviceWatcherService Validation)
Plan: 01 of 1 (complete)
Status: Phase 38 complete — ready for Phase 39
Last activity: 2026-03-15 — Completed 38-01-PLAN.md (DeviceWatcherService combined metric validation)

Progress: [####################] v1.0-v1.7 complete | [██░░░░░░░░] 2/4 v1.8 phases

## Performance Metrics

**Velocity:**
- Total plans completed: 89 (v1.0 through v1.7, plus 37-01)
- Average duration: ~25 min
- Total execution time: ~36.7 hours

**Recent Trend:**
- Last milestone (v1.7): 8 plans, 4 phases
- 37-01: 2 min (purely additive types, no behavior)
- Trend: Stable

*Updated after each plan completion*

## Accumulated Context

### Key Architectural Facts (v1.8 relevant)

- All pods maintain tenant vector state (no leader gating) — synthetic metrics route identically to poll metrics
- OidResolutionBehavior: unconditional `msg.MetricName = _oidMapService.Resolve(msg.Oid)` — NO bypass guard exists yet (critical: Phase 39 must add it before any synthetic dispatch)
- DeviceWatcherService.ValidateAndBuildDevicesAsync: internal static async — all per-entry validation happens here (mirrors v1.7 watcher pattern)
- MetricPollInfo is a positional record — `AggregatedMetrics` is an init-only property (not positional param) with default `[]`, source-compatible with all existing construction sites (CONFIRMED: 286 pre-existing tests pass unchanged)
- `SnmpSource` enum currently has only `Poll` and `Trap` — `Synthetic` must be added in Phase 39
- Aggregator terms: `"sum"`, `"subtract"`, `"absDiff"`, `"mean"` (lowercase; case-insensitive matching at load time)
- TypeCode selection: Subtract/AbsDiff → Integer32 (signed, result can be negative); Sum/Mean → Gauge32 (unsigned)
- Sentinel OID: `"0.0"` passes existing ValidationBehavior OID regex without guard; Prometheus label will show `oid="0.0"`
- Bypass guard decision: Option B — guard on `Source == SnmpSource.Synthetic` (consistent, unambiguous, future-safe)
- Ratio aggregation: excluded from v1.8 — `AggregationKind.Ratio` may exist in enum but BuildPollGroups treats it as invalid Action

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

### v1.8 Pre-Phase Decisions (record in plans)

- Phase 39: Name the bypass guard option (Option B: `Source == SnmpSource.Synthetic`) as a named decision
- Phase 39: Name the sentinel OID value (`"0.0"`) as a named decision
- Phase 40: Ratio is an invalid Action value in v1.8 — BuildPollGroups skips with Error log (same as unknown string)

### Known Tech Debt

None.

### Blockers/Concerns

None. All four phases have HIGH confidence per research summary. Phase 39 must complete before Phase 40.

## Session Continuity

Last session: 2026-03-15T09:17:55Z
Stopped at: Completed 38-01-PLAN.md — BuildPollGroups combined metric validation (5 rules), 10 unit tests, 312 total passing
Resume file: None
