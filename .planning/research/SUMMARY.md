# Research Summary — v2.0 Tenant Evaluation & Control

**Project:** Simetra119 SNMP Collector
**Domain:** SNMP monitoring agent — closed-loop tenant evaluation with SNMP SET command execution
**Researched:** 2026-03-16
**Confidence:** HIGH

---

## Executive Summary

The v2.0 milestone adds a `SnapshotJob` — a Quartz-scheduled, `[DisallowConcurrentExecution]` job that periodically evaluates all tenants by priority and issues SNMP SET commands when threshold criteria are met. Every API required for this milestone already exists in the codebase: `Lextm.SharpSnmpLib 12.5.7` provides `Messenger.SetAsync`, `System.Threading.Channels` (BCL) provides the command queue pattern already in use for trap processing, `System.Collections.Concurrent` provides the suppression cache, and `Quartz.Extensions.Hosting 3.15.1` handles job scheduling. Zero new NuGet packages are required.

The recommended implementation is a four-component addition: a `SnapshotJob` that evaluates tenants through a strict 4-tier logic tree and enqueues commands into a bounded `Channel<CommandRequest>`; a `CommandWorkerService` (background service) that drains the channel and executes `ISnmpClient.SetAsync`; a `SuppressionCache` (`ConcurrentDictionary<string, DateTimeOffset>`) that prevents duplicate commands within a configurable window; and a `SnapshotJobOptions` configuration POCO governing interval and suppression window. Two structural gaps in the existing model must be closed before this feature is buildable: `MetricSlotHolder` must expose `Role` (currently stored in `MetricSlotOptions` but not propagated to the runtime holder), and `Tenant` must carry its `CommandSlotOptions` list at runtime.

The primary risks are: (1) a suppression check-then-suppress race is prevented by `[DisallowConcurrentExecution]` — this must not be weakened; (2) the `CommandWorker` singleton registration requires the established Singleton-then-HostedService DI pattern — using `AddHostedService<CommandWorker>()` directly creates a second instance that is never injected; (3) `SnmpSource.Command` must NOT bypass OID resolution (unlike `Synthetic`) because SET response OIDs are real device OIDs in the OID map; and (4) community string for SET must be resolved from `IDeviceRegistry.TryGetByIpPort` at execution time in `CommandWorker`, not at enqueue time in `SnapshotJob`.

---

## Key Findings

### Recommended Stack

All infrastructure required for v2.0 is present in the existing stack. SharpSnmpLib 12.5.7 exposes `Messenger.SetAsync(VersionCode, IPEndPoint, OctetString, IList<Variable>, CancellationToken)` with return type `Task<IList<Variable>>` — verified by reflection against the installed DLL. The `Variable(ObjectIdentifier, ISnmpData)` constructor handles SET payload construction. Exception types for failure handling are `Lextm.SharpSnmpLib.Messaging.TimeoutException` and `Lextm.SharpSnmpLib.Messaging.ErrorException`, both extending `OperationException` — distinct from BCL `System.TimeoutException`, requiring a `using` alias to avoid CS0104.

**Core technologies:**

- `Lextm.SharpSnmpLib 12.5.7`: SNMP SET via `Messenger.SetAsync` — reflected directly from installed DLL, confirmed at this version
- `System.Threading.Channels` (BCL): `Channel<CommandRequest>` bounded queue with `DropOldest` backpressure — already used by `TrapChannel`, same pattern
- `System.Collections.Concurrent` (BCL): `ConcurrentDictionary<string, DateTimeOffset>` for suppression cache — no background eviction needed, bounded by config size
- `Quartz.Extensions.Hosting 3.15.1`: `SnapshotJob` registration with `[DisallowConcurrentExecution]`, simple interval trigger, `WithMisfireHandlingInstructionNextWithRemainingCount` — same pattern as all existing jobs

**Critical naming notes:**

- SharpSnmpLib's IP address type is `Lextm.SharpSnmpLib.IP`, NOT `IpAddress` — using `IpAddress` produces CS0246
- SharpSnmpLib throws `Lextm.SharpSnmpLib.Messaging.TimeoutException` on timeout, NOT `System.TimeoutException`

### Expected Features

**Must have — all 13 table stakes are required for correctness and safety:**

- **TS-01** `SnmpSource.Command` enum value — SET responses need a distinct source for correct OTel labels; `OidResolutionBehavior` does NOT bypass `Command` (unlike `Synthetic`)
- **TS-02** `MetricSlotHolder.Role` property — required for Tier 2/3 evaluation partitioning; currently missing from holder despite being in config options
- **TS-03** Staleness detection (Tier 1) — excludes `Trap`-sourced holders (no expected interval) and `IntervalSeconds=0` holders; tenant stale if ANY slot is stale
- **TS-04** Threshold violation check (Tiers 2/3) — "all violated" semantics; vacuous pass for empty Resolved-role holders; vacuous fail for empty Evaluate-role holders (log Warning on startup)
- **TS-05** Priority group traversal — parallel within group, sequential across groups; advance only if ALL tenants in group reached Tier 4
- **TS-06** Suppression cache — keyed by `(Ip, Port, CommandName)`, not tenant-scoped; TTL from `SnapshotJobOptions.SuppressionWindowSeconds`
- **TS-07** OID resolution at execution time — not at enqueue time; hot-reload may change OID between enqueue and execute
- **TS-08** `Messenger.SetAsync` execution with `ValueType → ISnmpData` dispatch (`Integer32`/`OctetString`/`IP`)
- **TS-09** SET response dispatched through full MediatR pipeline with `Source=Command`
- **TS-10** Three new pipeline counters: `snmp.command.sent`, `snmp.command.failed`, `snmp.command.suppressed` with `device_name` tag
- **TS-11** Liveness stamp in `finally` block, interval registered in `IJobIntervalRegistry`
- **TS-12** `SnapshotJobOptions` with `IntervalSeconds` (default 15), `SuppressionWindowSeconds` (default 15–300), `TimeoutMultiplier` (default 0.5)
- **TS-13** Channel-backed `CommandWorkerService` — bounded, `DropOldest`, serial consumer; `TryWrite` in enqueue path (do not block SnapshotJob)

**Should build (low cost, high operational value):**

- **D-01** Structured evaluation logs per tenant per run (Debug for Stale/Resolved/NoViolation, Information for Commanded)
- **D-03** Command execution log with round-trip duration via `Stopwatch` around `SetAsync`
- **D-05** `snmp.command.*` panels on operations dashboard (Phase 18)

**Evaluate before committing:**

- **D-02** Evaluation outcome counters per tenant — adds `tenant_id` cardinality; acceptable if tenant count is small (< 20)
- **D-04** Suppression cache diagnostics in health endpoint — useful for debugging misconfigured suppression windows

**Explicitly excluded (anti-features):**

- AF-01: No automatic SET retry — double-apply risk on slow devices; next cycle is the retry mechanism
- AF-02: No hysteresis/debouncing — suppression window is the damping mechanism
- AF-03: No cross-tenant command conflict resolution — priority groups + suppression cache handle this
- AF-04: No follow-up GET after SET — SET response contains confirmed device value
- AF-05: No command queue persistence — SnapshotJob re-queues within one cycle after restart
- AF-06: No parallel command execution — serial worker sufficient, avoids SNMP device concurrency concerns

### Architecture Approach

The `SnapshotJob` is a pure reader: it reads `ITenantVectorRegistry.Groups` (single volatile read, immutable snapshot) and `MetricSlotHolder.ReadSlot()` (Volatile.Read, thread-safe concurrent with `TenantVectorFanOutBehavior` writes). It enqueues `CommandRequest` objects into `ICommandChannel.Writer` using non-blocking `TryWrite` (never blocks the evaluation loop on channel capacity). `CommandWorker` runs independently as a background service, dequeuing commands and calling `ISnmpClient.SetAsync`, then dispatching each SET response varbind through the full MediatR pipeline as `SnmpOidReceived{Source=SnmpSource.Command}`. `[DisallowConcurrentExecution]` on `SnapshotJob` is the only concurrency guard needed — it makes the suppression check-then-suppress sequence non-racy.

**Major components:**

1. `SnapshotJob` (`Jobs/SnapshotJob.cs`) — `[DisallowConcurrentExecution] IJob`; drives 4-tier evaluation across all priority groups; pure reader of registry; enqueues commands; stamps liveness
2. `CommandWorkerService` (`Services/CommandWorkerService.cs`) — `BackgroundService`; drains `ICommandChannel.Reader`; resolves community string from `IDeviceRegistry`; calls `ISnmpClient.SetAsync`; dispatches SET response through `ISender.Send`
3. `SuppressionCache` (`Pipeline/SuppressionCache.cs`) — `ConcurrentDictionary<string, DateTimeOffset>` singleton; keyed by `"{Ip}:{Port}:{CommandName}"`; lazy TTL expiry; no background cleanup thread
4. `ISnmpClient.SetAsync` extension — single new method on existing interface; `SharpSnmpClient` delegates to `Messenger.SetAsync`
5. `SnapshotJobOptions` (`Configuration/SnapshotJobOptions.cs`) — `IntervalSeconds`, `SuppressionWindowSeconds`, `TimeoutMultiplier`; validated on startup
6. `SnmpSource.Command` enum value — SET response varbinds; flows through full MediatR pipeline without OID resolution bypass

**Build order (each step compilable and testable before the next):**

| Step | What | Why This Order |
|------|------|----------------|
| 1 | `SnmpSource.Command` | All new components reference this value |
| 2 | `MetricSlotHolder.Role` + `TenantVectorRegistry.Reload` update | SnapshotJob tier evaluation requires Role at runtime |
| 3 | `Tenant.Commands` property | SnapshotJob tier 4 needs CommandSlotOptions per tenant |
| 4 | `SnapshotJobOptions` | SnapshotJob and Quartz registration depend on it |
| 5 | `ISuppressionCache` + `SuppressionCache` | No external deps; SnapshotJob and tests depend on it |
| 6 | `ISnmpClient.SetAsync` + `SharpSnmpClient` | CommandWorker depends on it |
| 7 | `PipelineMetricService` new counters | Both SnapshotJob and CommandWorker inject it |
| 8 | `CommandRequest` record + `ICommandChannel` | CommandWorker queue type; SnapshotJob enqueue interface |
| 9 | `CommandWorkerService` | Requires steps 6–8 |
| 10 | `SnapshotJob` | Requires steps 1–5, 7–8 |
| 11 | `ServiceCollectionExtensions` updates | Wires all components into DI |
| 12 | Unit tests | Tier evaluation, suppression logic, SET pipeline flow |

### Critical Pitfalls

The PITFALLS.md file covers v1.6, v1.7, and combined-metrics pitfalls — most are for prior milestones. The following are the highest-impact pitfalls specifically relevant to v2.0 implementation:

1. **Singleton-then-HostedService pattern required for CommandWorker** — `AddSingleton<CommandWorker>()` + `AddSingleton<ICommandWorker>(sp => sp.GetRequiredService<CommandWorker>())` + `AddHostedService(sp => sp.GetRequiredService<CommandWorker>())`. Using `AddSingleton<ICommandWorker, CommandWorker>()` + `AddHostedService<CommandWorker>()` separately creates two instances. SnapshotJob enqueues to the non-running instance's channel — commands are silently lost.

2. **Do NOT bypass OID resolution for `SnmpSource.Command`** — unlike `Synthetic`, SET response OIDs are real device OIDs present in the OID map. Bypassing `OidResolutionBehavior` (as Synthetic does) sets `MetricName = null`, causing TenantVectorFanOut to skip fan-out and OtelMetricHandler to record `"Unknown"`. The `Synthetic` bypass must NOT be extended to `Command`.

3. **Community string must be resolved in CommandWorker, not SnapshotJob** — `CommandSlotOptions` has `Ip/Port` but no `CommunityString`. Resolving from `IDeviceRegistry.TryGetByIpPort` inside SnapshotJob's evaluation loop mixes evaluation with execution concerns and complicates testing. CommandWorker resolves at execution time and handles "device not found" gracefully.

4. **`[DisallowConcurrentExecution]` must not be removed** — the suppression check-then-suppress in SnapshotJob is not atomic. It is safe only because `[DisallowConcurrentExecution]` guarantees a single execution at any moment. Removing this attribute would require locking the entire suppression write path.

5. **`TryWrite` (non-blocking) must be used to enqueue commands from SnapshotJob** — `WriteAsync` blocks until channel space is available. If CommandWorker is processing slowly and the channel is full, blocking SnapshotJob's Quartz thread cascades into liveness probe failures. A `TryWrite` failure increments `snmp.command.failed` and logs a Warning.

6. **v1.7 Pitfall G applies here: Value/ValueType mismatch is a silent runtime crash** — `CommandSlotOptions.Value` must be validated as parseable as `CommandSlotOptions.ValueType` at config load time. A config entry `{ "ValueType": "Integer32", "Value": "not-a-number" }` passes structural validation but throws `FormatException` at execution time inside `CommandWorker`.

---

## Structural Gaps

These gaps exist in the current codebase and must be closed before SnapshotJob evaluation logic can be written. They are missing data propagation, not new features.

| Gap | Current State | Required State | Risk if Deferred |
|-----|--------------|----------------|-----------------|
| `MetricSlotHolder.Role` | Not stored on holder; exists only in `MetricSlotOptions` | `string Role { get; }` populated in `TenantVectorRegistry.Reload` | SnapshotJob cannot partition holders into Evaluate vs Resolved — Tiers 2 and 3 are not implementable |
| `Tenant.Commands` property | `TenantOptions.Commands` exists in config; not exposed on runtime `Tenant` type | `IReadOnlyList<CommandSlotOptions> Commands { get; }` populated in `TenantVectorRegistry.Reload` | SnapshotJob Tier 4 cannot access command targets |
| `MetricSlotHolder.Role` type decision | Unresolved | String constant ("Evaluate"/"Resolved") or new `MetricSlotRole` enum | Implementation inconsistency if not decided first; string constants match existing `MetricSlotOptions.Role` |
| `IDeviceRegistry.TryGetByIpPort` compatibility | Works for poll device IP:Port pairs | Must work for `CommandSlotOptions.Ip:Port` (may be different devices) | CommandWorker cannot resolve community string for SET commands |

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | `Messenger.SetAsync` verified by reflection against SharpSnmpLib.dll 12.5.7; all required types confirmed at this version; exception hierarchy verified |
| Features | HIGH | All 13 table stakes derived from full codebase analysis; SET capability verified against SharpSnmpLib API docs; edge case matrix derived from existing source behavior |
| Architecture | HIGH | All component patterns read directly from source (`TrapChannel`, `CorrelationJob`, `K8sLeaseElection` singleton-then-hosted pattern, `TenantVectorRegistry` volatile read pattern) |
| Pitfalls | HIGH | All v2.0-relevant pitfalls derived from direct source inspection; v1.6/v1.7 pitfalls are background context and generally do not block v2.0 except Pitfall G (Value/ValueType validation) |

**Overall confidence: HIGH**

All research is grounded in direct codebase reading with zero training-data speculation. The only MEDIUM-confidence source is the SharpSnmpLib API docs page — the reflection result for this is HIGH.

### Gaps to Address During Planning

1. **`MetricSlotHolder.Role` type** — string constant vs enum. Recommendation: enum is cleaner at evaluation time (no stringly-typed comparisons), but string constant matches existing `MetricSlotOptions.Role` field. Decide and be consistent in Phase A plan.

2. **Background worker vs inline execution** — TS-13 recommends a Channel-backed worker. If max commands per SnapshotJob cycle is confirmed to be 3 or fewer, inline execution is simpler and removes a component. Assess in Phase C plan.

3. **`IDeviceRegistry.TryGetByIpPort` compatibility with CommandSlotOptions** — verify before building CommandWorker. If incompatible, either add a new lookup method or add `CommunityString` to `CommandSlotOptions`.

4. **Leader gating on SnapshotJob** — all replicas vs leader-only. All replicas is correct for idempotent SET operations. If commands are non-idempotent (toggle vs set-to-value), leader gate is required. Must be documented as an explicit assumption in Phase D plan.

5. **Staleness scope clarification** — FEATURES.md TS-03 (any holder stale = tenant stale) vs ARCHITECTURE.md Tier 1 pseudocode (Evaluate-role holders only). FEATURES.md is authoritative. Resolve before unit tests are written.

---

## Implications for Roadmap

Based on the combined research, the natural phase structure follows the component dependency graph. Every dependency is within the existing codebase — there are no external integrations to coordinate.

### Phase A: Structural Prerequisites

**Rationale:** `MetricSlotHolder.Role` and `Tenant.Commands` are missing data propagation in `TenantVectorRegistry.Reload`. Nothing in SnapshotJob evaluation can be written without them. They are small, low-risk, independently testable changes. `SnmpSource.Command` also belongs here — it is required by every subsequent component.

**Delivers:** `SnmpSource.Command` enum value; `MetricSlotHolder.Role` populated from `MetricSlotOptions.Role`; `Tenant.Commands` populated from `TenantOptions.Commands`; `TenantVectorRegistry.Reload` tests updated to cover Role and Commands propagation.

**Addresses:** TS-01, TS-02 (structural gaps)

**Avoids:** Building tier evaluation against a holder model that lacks Role (would require immediate rework)

**Research flag:** No research needed. Direct property additions, existing `MetricSlotHolder` construction pattern applies.

---

### Phase B: Infrastructure Components

**Rationale:** Suppression cache, options model, and `ISnmpClient.SetAsync` have no mutual dependencies and no dependency on SnapshotJob or CommandWorker. Building them first gives clean, mockable interfaces for the remaining phases.

**Delivers:** `ISuppressionCache` / `SuppressionCache` with full thread-safety tests; `SnapshotJobOptions` with `ValidateDataAnnotations`; `ISnmpClient.SetAsync` + `SharpSnmpClient` delegation; `PipelineMetricService` extended with 4 command counters.

**Addresses:** TS-06, TS-10, TS-12

**Avoids:** Pitfall — `Lextm.SharpSnmpLib.IP` type (not `IpAddress`); Pitfall — `SnmpTimeout` alias for CS0104

**Research flag:** No research needed. All APIs verified by reflection.

---

### Phase C: CommandWorkerService

**Rationale:** CommandWorker is the execution engine. It can be built and fully tested before SnapshotJob exists by directly enqueuing `CommandRequest` records in tests. Building it before SnapshotJob allows SnapshotJob to be tested with a real (or mocked) worker.

**Delivers:** `ICommandChannel` / `CommandChannel` (bounded, `DropOldest`); `CommandWorkerService` (`BackgroundService`); Singleton-then-HostedService DI registration; unit tests for OID resolution, community string lookup, `SetAsync` success/failure, SET response MediatR dispatch with `Source=Command`.

**Addresses:** TS-07, TS-08, TS-09, TS-13

**Avoids:** Pitfall — Singleton-then-HostedService DI two-instance bug; Pitfall — community string resolved at execution time; Pitfall — `TryWrite` vs `WriteAsync` backpressure; Pitfall G — Value/ValueType validation

**Research flag:** No research needed. `ChannelConsumerService` and `K8sLeaseElection` patterns are directly applicable.

---

### Phase D: SnapshotJob — 4-Tier Evaluation Loop

**Rationale:** SnapshotJob depends on everything from Phases A–C. With all dependencies built and tested, SnapshotJob becomes a straightforward orchestration layer. The 4-tier logic tree has a complete edge case matrix in FEATURES.md that maps directly to unit test cases.

**Delivers:** `SnapshotJob` with full 4-tier evaluation; priority group traversal with "all violated" advance gate; Quartz registration; liveness stamp; correlation ID wiring; integration tests covering stale tenant skip, resolved gate stop, evaluate all-violated command dispatch, group advance, group stop.

**Addresses:** TS-03, TS-04, TS-05, TS-11

**Avoids:** Pitfall — `[DisallowConcurrentExecution]` must not be removed; Pitfall — suppression state must be injected singleton; Pitfall — `TryWrite` must be used for channel enqueue

**Research flag:** No research needed. Edge case matrix in FEATURES.md is comprehensive and sufficient for test derivation.

---

### Phase E: Observability

**Rationale:** With the core loop working, differentiator features add operational visibility with minimal risk. These are add-ons to already-passing tests.

**Delivers:** D-01 structured evaluation logs; D-03 command execution logs with round-trip duration; D-05 `snmp.command.*` dashboard panels; D-02 and D-04 as conditional additions based on tenant count.

**Addresses:** D-01, D-03, D-05 (and optionally D-02, D-04)

**Research flag:** No research needed. Dashboard follows Phase 18 JSON editing pattern.

---

### Phase Ordering Rationale

- Phase A must precede all other phases because the structural gaps block all evaluation logic and are the lowest-risk changes.
- Phase B must precede C and D because `ISuppressionCache`, options, and `ISnmpClient.SetAsync` are required interfaces that make Phases C and D independently testable.
- Phase C must precede D because SnapshotJob enqueues into `ICommandChannel` — the interface must exist before the enqueue call compiles.
- Phase E is last because observability features require the core loop to be stable.
- No external dependencies exist across any phase — all phases operate entirely within the existing codebase.

### Research Flags

Phases needing deeper research during planning: **None.** All four research dimensions are HIGH confidence, all APIs are verified against the installed DLL, and all patterns are derived from existing working code in the same repository.

Phases with standard patterns (skip research-phase): **All phases.**

---

## Open Questions (Consolidated)

These are design decisions, not research gaps. They must be resolved during implementation planning.

| # | Question | Source | Recommendation |
|---|----------|--------|----------------|
| OQ-1 | `MetricSlotHolder.Role` type: string constant vs `MetricSlotRole` enum? | FEATURES.md, ARCHITECTURE.md | Enum preferred (no stringly-typed comparisons at evaluation time); string constant is lower friction. Decide in Phase A. |
| OQ-2 | Background worker vs inline SET execution in SnapshotJob? | FEATURES.md Structural Gap 4 | Channel-backed worker is correct unless max commands per cycle is confirmed ≤ 3. Assess in Phase C. |
| OQ-3 | Community string lookup: does `IDeviceRegistry.TryGetByIpPort` work for CommandSlotOptions targets? | FEATURES.md Structural Gap 3, ARCHITECTURE.md Anti-Pattern 4 | Verify in Phase C before building CommandWorker. If not found, add `CommunityString` to `CommandSlotOptions`. |
| OQ-4 | SnapshotJob leader gating: all replicas fire, or leader only? | ARCHITECTURE.md Open Question 4 | All replicas recommended for idempotent SET. If commands are non-idempotent, leader gate required. Document assumption in Phase D. |
| OQ-5 | Staleness scope: all holders, or only Evaluate-role holders? | FEATURES.md TS-03 vs ARCHITECTURE.md Tier 1 | FEATURES.md TS-03 is authoritative: any holder stale = tenant stale. Resolve before unit tests. |
| OQ-6 | "All violated" for group advance: includes stale tenants? | ARCHITECTURE.md Open Question 1 | Stale = not violated for advance gate purposes. Treat stale as healthy assumption — stops cascade on missing data. |
| OQ-7 | Tier 2 semantics: "all Resolved violated" or "any Resolved healthy stops"? | ARCHITECTURE.md Open Question 2 | If ANY Resolved metric is healthy (in range), stop. "All violated" means no healthy Resolved metric remains. |

---

## Sources

### Primary (HIGH confidence — direct codebase reading)

- `src/SnmpCollector/Pipeline/ISnmpClient.cs` + `SharpSnmpClient.cs` — existing interface, `GetAsync` delegation pattern, extension point
- `src/SnmpCollector/Pipeline/TrapChannel.cs` — `Channel<T>` bounded channel pattern for command queue
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — `volatile Groups` field, `SortedDictionary` priority ordering, `Reload` carry-over pattern
- `src/SnmpCollector/Pipeline/MetricSlotHolder.cs` — `ReadSlot()`, `Threshold`, `Source`, `IntervalSeconds`, `GraceMultiplier`; Role absent
- `src/SnmpCollector/Pipeline/Tenant.cs` — `Id`, `Priority`, `Holders`; Commands absent
- `src/SnmpCollector/Configuration/MetricSlotOptions.cs` — `Role`, `Threshold`, `GraceMultiplier`
- `src/SnmpCollector/Configuration/CommandSlotOptions.cs` — `Ip`, `Port`, `CommandName`, `Value`, `ValueType`
- `src/SnmpCollector/Configuration/ThresholdOptions.cs` — `Min: double?`, `Max: double?`
- `src/SnmpCollector/Pipeline/SnmpSource.cs` — `Poll`, `Trap`, `Synthetic`; `Command` absent
- `src/SnmpCollector/Pipeline/ICommandMapService.cs` — `ResolveCommandOid` method
- `src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs` — Synthetic bypass pattern; Command must NOT be added here
- `src/SnmpCollector/Pipeline/Behaviors/TenantVectorFanOutBehavior.cs` — routing by `(ip, port, metricName)`
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — `[DisallowConcurrentExecution]`, liveness stamp, correlation ID, SNMP dispatch pattern
- `src/SnmpCollector/Jobs/CorrelationJob.cs` — `finally { _liveness.Stamp(jobKey); }` pattern
- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — configurable interval options, liveness stamp
- `src/SnmpCollector/Telemetry/PipelineMetricService.cs` — 12-counter pattern, `device_name` tag convention
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — Singleton-then-HostedService DI pattern, Quartz registration, thread pool sizing
- `src/SnmpCollector/Pipeline/LivenessVectorService.cs` — `ConcurrentDictionary` stamp pattern for suppression cache model
- `src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs` — `IJobIntervalRegistry` staleness threshold computation
- `SharpSnmpLib.dll` 12.5.7 via reflection — `Messenger.SetAsync` overloads, `Variable` constructors, `IP`/`Integer32`/`OctetString` constructors, `ErrorException`/`TimeoutException` hierarchy
- `.planning/PROJECT.md` v2.0 spec — `SnapshotJob` description, 4-tier evaluation, priority group semantics, suppression cache, `snmp.command.*` counter spec

### Secondary (MEDIUM confidence)

- SharpSnmpLib 12.5.7 API docs — `Messenger.SetAsync` method list confirmed on docs page (reflection result for this is HIGH; docs page alone is MEDIUM)

---

*Research completed: 2026-03-16*
*Ready for roadmap: yes*
