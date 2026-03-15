# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-15)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.8 Combined Metrics — COMPLETE (all 4 phases done)

## Current Position

Phase: 40 of 40 (MetricPollJob Aggregate Dispatch)
Plan: 01 of 1 (complete)
Status: Phase 40 complete — v1.8 Combined Metrics feature complete
Last activity: 2026-03-15 — Completed 40-01-PLAN.md (aggregate dispatch + 10 tests, 326 total passing)

Progress: [####################] v1.0-v1.7 complete | [██████████] 4/4 v1.8 phases

## Performance Metrics

**Velocity:**
- Total plans completed: 91 (v1.0 through v1.7, plus 37-01 through 40-01)
- Average duration: ~25 min
- Total execution time: ~36.8 hours

**Recent Trend:**
- Last milestone (v1.7): 8 plans, 4 phases
- 37-01: 2 min (purely additive types, no behavior)
- 39-01: 2 min (surgical: 3 lines production code, 4 new tests)
- 40-01: 4 min (134 production LOC, 10 new tests, 326 total passing)
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

### Phase 40 Decisions

- **DispatchResponseAsync extended with pollGroup param** — Option A (cleaner than splitting at call site in Execute)
- **OID dict built inline in DispatchAggregatedMetricAsync** — per-combined-metric, keeps method self-contained
- **Math.Clamp for overflow safety** — silent clamp when double result exceeds Integer32/Gauge32 range
- **Aggregate exceptions: log Error only** — do NOT call RecordFailure per locked CM decision

### v1.8 Pre-Phase Decisions (record in plans)

- ~~Phase 39: Name the bypass guard option (Option B: `Source == SnmpSource.Synthetic`) as a named decision~~ (DONE)
- ~~Phase 39: Name the sentinel OID value (`"0.0"`) as a named decision~~ (DONE)
- ~~Phase 40: Ratio is an invalid Action value in v1.8 — BuildPollGroups skips with Error log (same as unknown string)~~ (confirmed in BuildPollGroups; no action needed in 40-01)

### Known Tech Debt

None.

### Blockers/Concerns

None. v1.8 Combined Metrics is complete.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 056 | Add snmp.aggregated.computed panel to ops dashboard | 2026-03-15 | 23c5a5a | [056-add-aggregated-computed-to-ops-dashboard](./quick/056-add-aggregated-computed-to-ops-dashboard/) |
| 057 | Add aggregate poll groups + tenant routing to devices/tenants configs (all locations) | 2026-03-15 | bc35f6a | [057-demo-aggregate-metrics-in-devices](./quick/057-demo-aggregate-metrics-in-devices/) |

## Session Continuity

Last session: 2026-03-15T10:51:28Z
Stopped at: Completed quick/057 — Aggregate poll groups and tenant entries added to all config locations
Resume file: None
