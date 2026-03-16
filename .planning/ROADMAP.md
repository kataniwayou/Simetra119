# Roadmap: SNMP Monitoring System

## Milestones

- ✅ **v1.0 Foundation** - Phases 1-10 (shipped 2026-03-07)
- ✅ **v1.1 Device Simulation** - Phases 11-14 (shipped 2026-03-08)
- ✅ **v1.2 Operational Enhancements** - Phases 15-16 (shipped 2026-03-08)
- ✅ **v1.3 Grafana Dashboards** - Phases 18-19 (shipped 2026-03-09)
- ✅ **v1.4 E2E System Verification** - Phases 20-24 (shipped 2026-03-09)
- ✅ **v1.5 Priority Vector Data Layer** - Phases 25-29 (shipped 2026-03-10)
- ✅ **v1.6 Organization & Command Map Foundation** - Phases 30-32 (shipped 2026-03-13)
- ✅ **v1.7 Configuration Consistency & Tenant Commands** - Phases 33-36 (shipped 2026-03-15)
- ✅ **v1.8 Combined Metrics** - Phases 37-40 (shipped 2026-03-15)
- ✅ **v1.9 Metric Threshold Structure & Validation** - Phases 41-42 (shipped 2026-03-15)
- ✅ **v1.10 Heartbeat Refactor & Pipeline Liveness** - Phases 43-44 (shipped 2026-03-15)
- 🚧 **v2.0 Tenant Evaluation & Control** - Phases 45-49 (in progress)

## Phases

<details>
<summary>✅ v1.0 through v1.4 (Phases 1-24) - SHIPPED</summary>

See `.planning/MILESTONES.md` and `.planning/milestones/` for archived details.

</details>

<details>
<summary>✅ v1.5 Priority Vector Data Layer (Phases 25-29) - SHIPPED 2026-03-10</summary>

See `.planning/MILESTONES.md` for details.

</details>

<details>
<summary>✅ v1.6 Organization & Command Map Foundation (Phases 30-32) - SHIPPED 2026-03-13</summary>

#### Phase 30: OID Map Integrity

**Goal**: Operators can detect configuration errors in oidmaps.json at load time, and any code that needs to reverse-resolve a metric name to its OID can do so via a stable interface method
**Depends on**: Nothing (foundation for Phase 31)
**Requirements**: MAP-01, MAP-02, MAP-03, MAP-04
**Success Criteria** (what must be TRUE):
  1. Loading an oidmaps.json with a duplicate OID key produces a structured log warning per duplicate that names the OID, both conflicting metric names, and which name was retained — no silent last-write-wins clobber
  2. Loading an oidmaps.json with a duplicate metric name value produces a structured log warning per duplicate that names the conflicting OIDs — both map entries remain visible in logs
  3. `IOidMapService.ResolveToOid("obp_channel_L1")` returns the correct OID string; `ResolveToOid("no-such-name")` returns null
  4. The reverse index is rebuilt atomically alongside the forward map on every hot-reload — a caller reading immediately after reload sees the new reverse map, never a partial state
  5. Validation runs before `OidMapService.UpdateMap` is called so that duplicate-warning log entries are never followed by contradictory "added" diff entries for the same load event

**Plans:** 2 plans

Plans:
- [x] 30-01-PLAN.md — Reverse index and ResolveToOid (OidMapService + IOidMapService + tests)
- [x] 30-02-PLAN.md — Duplicate OID/name validation in OidMapWatcherService + tests

---

#### Phase 31: Human-Name Device Config

**Goal**: Operators can reference metric names like "obp_channel_L1" instead of raw OID strings in devices.json poll entries, with full replacement of the Oids field by MetricNames and graceful handling of unresolvable names. Restructure oidmaps.json from flat dictionary to array of objects with explicit Oid/MetricName fields.
**Depends on**: Phase 30 (`IOidMapService.ResolveToOid` must exist before DeviceRegistry can call it)
**Requirements**: DEV-01, DEV-02, DEV-03, DEV-04, DEV-05, DEV-06, DEV-07
**Success Criteria** (what must be TRUE):
  1. A devices.json poll entry with `MetricNames: ["obp_channel_L1", "obp_r1_power_L1"]` starts polling both OIDs after device config load — MetricPollJob never receives metric name strings as OID arguments
  2. A MetricNames[] entry that has no match in the current OID map logs a structured warning with device name and metric name, and that entry is silently skipped — the device's other poll entries still register normally
  3. When device config reloads, names are resolved against the current OID map state at that moment (point-in-time resolution)
  4. Reload diff logging includes per-name resolution detail (resolved count, unresolved names listed)

**Plans:** 3 plans

Plans:
- [x] 31-01-PLAN.md — OidMap array restructure + C# model rename (PollOptions, Polls, MetricNames)
- [x] 31-02-PLAN.md — Name resolution in DeviceRegistry via IOidMapService + unit tests
- [x] 31-03-PLAN.md — Config file rewrite (devices.json, K8s ConfigMaps, E2E fixtures, E2E scenarios)

---

#### Phase 32: Command Map Infrastructure

**Goal**: A command map lookup table is operational — operators can load commandmaps.json via ConfigMap hot-reload or local file, and any in-process code can resolve a command name to its SET OID or vice versa
**Depends on**: Nothing (fully independent of Phases 30 and 31)
**Requirements**: CMD-01, CMD-02, CMD-03, CMD-04, CMD-05, CMD-06
**Success Criteria** (what must be TRUE):
  1. `CommandMapService.ResolveCommandOid("set-power-threshold")` returns the correct OID string; an unknown name returns null — both forward (OID → name) and reverse (name → OID) lookups work without throwing
  2. Updating the simetra-commandmaps ConfigMap in K8s triggers a hot-reload within seconds — structured diff log entries appear in pod logs showing added, removed, and changed command entries, plus total entry count
  3. In local dev mode (no K8s cluster), `CommandMapService` is populated from `config/commandmaps.json` on startup — no empty-map silent failure
  4. Loading a commandmaps.json with a duplicate OID key or a duplicate command name produces a structured warning per duplicate — same validation behavior as OID map integrity (Phase 30)
  5. The simetra-commandmaps ConfigMap manifest exists in the deploy directory and is ready to apply to the cluster

**Plans:** 3 plans

Plans:
- [x] 32-01-PLAN.md — ICommandMapService + CommandMapService + commandmaps.json + 12 unit tests
- [x] 32-02-PLAN.md — simetra-commandmaps K8s ConfigMap manifests (standalone + production)
- [x] 32-03-PLAN.md — CommandMapWatcherService + DI wiring + local dev fallback + 10 validation tests

</details>

---

<details>
<summary>✅ v1.7 Configuration Consistency & Tenant Commands (Phases 33-36) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.7-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v1.8 Combined Metrics (Phases 37-40) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.8-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v1.9 Metric Threshold Structure & Validation (Phases 41-42) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.9-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v1.10 Heartbeat Refactor & Pipeline Liveness (Phases 43-44) - SHIPPED 2026-03-15</summary>

See `.planning/milestones/v1.10-ROADMAP.md` for details.

</details>

---

### 🚧 v2.0 Tenant Evaluation & Control (In Progress)

**Milestone Goal:** SnapshotJob evaluates all tenants by priority on a 15s cycle, detects stale metrics, checks thresholds via a 4-tier logic tree, and issues SNMP SET commands through a Channel-backed worker — with suppression cache, 3 new pipeline counters, and full observability.

- [x] **Phase 45: Structural Prerequisites** — Close data propagation gaps so SnapshotJob evaluation logic is buildable
- [x] **Phase 46: Infrastructure Components** — Build suppression cache, options model, SetAsync extension, and command counters
- [x] **Phase 47: CommandWorkerService** — Channel-backed background worker that executes SET commands and dispatches responses
- [ ] **Phase 48: SnapshotJob 4-Tier Evaluation** — Quartz job driving full tenant evaluation loop with priority group traversal
- [ ] **Phase 49: Observability & Dashboard** — Structured evaluation logs, command execution logs, and dashboard panels
- [ ] **Phase 50: Label Rename** — Rename metric_name → resolved_name across all instruments and dashboards

---

#### Phase 45: Structural Prerequisites

**Goal**: The runtime data model is complete — MetricSlotHolder carries Role, Tenant carries Commands, and SnmpSource.Command exists — so all SnapshotJob evaluation logic can be written without placeholder stubs
**Depends on**: Nothing (closes gaps in existing model; no new components required)
**Requirements**: SNAP-01, SNAP-02, SNAP-03
**Success Criteria** (what must be TRUE):
  1. `MetricSlotHolder.Role` is populated at `TenantVectorRegistry.Reload` time — unit tests confirm that a holder built from a `MetricSlotOptions` with `Role="Evaluate"` reports `Role="Evaluate"` at runtime
  2. `Tenant.Commands` returns the `IReadOnlyList<CommandSlotOptions>` populated from `TenantOptions.Commands` at reload time — unit tests confirm Commands list is non-null and contains the correct entries
  3. `SnmpSource.Command` exists as an enum value — `OidResolutionBehavior` does NOT bypass resolution for Command source (Synthetic bypass is unchanged and unaffected)
  4. All existing tests remain green after the three property additions

**Plans:** 2 plans

Plans:
- [ ] 45-01-PLAN.md — SnmpSource.Command enum value + OidResolutionBehavior MetricName-guard refactor
- [ ] 45-02-PLAN.md — MetricSlotHolder.Role + Tenant.Commands propagation in TenantVectorRegistry.Reload + tests

---

#### Phase 46: Infrastructure Components

**Goal**: The suppression cache, job options, SetAsync capability, and command pipeline counters all exist as independently testable components with clean interfaces — SnapshotJob and CommandWorkerService can inject and test against them without being built yet
**Depends on**: Phase 45 (SnmpSource.Command must exist for PipelineMetricService counter wiring)
**Requirements**: SNAP-08, SNAP-09, SNAP-12, SNAP-13
**Success Criteria** (what must be TRUE):
  1. `ISuppressionCache.TrySuppress(key, windowSeconds)` returns false on first call and true on a second call within the window — cache entries expire correctly after the window elapses (lazy TTL)
  2. `ISnmpClient.SetAsync` exists on the interface and `SharpSnmpClient.SetAsync` delegates to `Messenger.SetAsync` — ValueType dispatch covers Integer32, OctetString, and IpAddress (using `Lextm.SharpSnmpLib.IP`)
  3. `SnapshotJobOptions` loads from the `"SnapshotJob"` config section and fails startup with a validation error if `IntervalSeconds` is below its minimum — `ValidateOnStart` is active
  4. `PipelineMetricService` exposes `IncrementCommandSent`, `IncrementCommandFailed`, and `IncrementCommandSuppressed` methods with `device_name` tag — all three counters are registered in the OTel meter

**Plans:** 3 plans

Plans:
- [ ] 46-01-PLAN.md — ISuppressionCache + SuppressionCache + TenantOptions.SuppressionWindowSeconds + Value+ValueType validation
- [ ] 46-02-PLAN.md — SnapshotJobOptions + ISnmpClient.SetAsync + SharpSnmpClient.SetAsync + ParseSnmpData
- [ ] 46-03-PLAN.md — PipelineMetricService command counters (snmp.command.sent/failed/suppressed)

---

#### Phase 47: CommandWorkerService

**Goal**: SNMP SET commands flow from enqueue to execution to MediatR pipeline — CommandWorkerService drains the channel, resolves community string at execution time, calls SetAsync, and dispatches each SET response varbind as SnmpOidReceived with source=Command through the full pipeline
**Depends on**: Phase 46 (ISnmpClient.SetAsync, ISuppressionCache, PipelineMetricService counters, SnapshotJobOptions must exist)
**Requirements**: SNAP-10, SNAP-11
**Success Criteria** (what must be TRUE):
  1. A `CommandRequest` enqueued into `ICommandChannel` is executed by `CommandWorkerService` — `ISnmpClient.SetAsync` is called with the correct endpoint, community string (resolved from IDeviceRegistry at execution time), and OID (resolved from ICommandMapService)
  2. Each varbind in the SET response becomes an `SnmpOidReceived` dispatched through the full MediatR pipeline with `Source=SnmpSource.Command` — the metric appears in Prometheus with `source="Command"`
  3. A SET failure increments `snmp.command.failed` and logs a Warning — the worker continues processing subsequent commands
  4. `CommandWorkerService` is registered via the Singleton-then-HostedService DI pattern — only one instance exists; the instance that runs as a hosted service is the same instance that SnapshotJob enqueues to

**Plans:** 2 plans

Plans:
- [ ] 47-01: CommandRequest record + ICommandChannel + CommandChannel (bounded, DropWrite)
- [ ] 47-02: CommandWorkerService + DI registration + unit tests (success, failure, OID resolution, SET response dispatch)

---

#### Phase 48: SnapshotJob 4-Tier Evaluation

**Goal**: SnapshotJob runs on a Quartz schedule, evaluates all tenant priority groups through the complete 4-tier logic tree, enqueues commands for tenants that reach Tier 4, and stamps liveness — the full closed-loop evaluation path is operational
**Depends on**: Phases 45, 46, 47 (all prerequisite components must exist)
**Requirements**: SNAP-04, SNAP-05, SNAP-06, SNAP-07, SNAP-14, SNAP-15
**Success Criteria** (what must be TRUE):
  1. A tenant with a stale metric (any holder past IntervalSeconds × GraceMultiplier, excluding Trap/IntervalSeconds=0) is skipped to Tier 4 — no threshold check occurs and no command is dispatched for that cycle
  2. A tenant with all Resolved metrics in violation proceeds past Tier 2 — a tenant with at least one Resolved metric in range stops at Tier 2 with no command
  3. A tenant with all Evaluate metrics in violation reaches Tier 4 and a `CommandRequest` is enqueued — a tenant with any Evaluate metric in range stops at Tier 3 with no command
  4. Priority group traversal is sequential across groups — a group where NOT all tenants reached Tier 4 stops execution and lower-priority groups are not evaluated that cycle
  5. SnapshotJob stamps `ILivenessVectorService` with key `"snapshot"` in its `finally` block — `LivenessHealthCheck` detects staleness if the job stops running
  6. Structured evaluation logs appear at Debug level for stale/resolved-gate/no-violation outcomes and at Information level for command-dispatched outcomes — each log includes tenant ID, priority, and tier reached

**Plans:** 4 plans

Plans:
- [ ] 48-01: SnapshotJob skeleton — Quartz registration, liveness stamp, correlation ID, [DisallowConcurrentExecution]
- [ ] 48-02: Tier 1 staleness detection + Tier 2 Resolved gate + unit tests
- [ ] 48-03: Tier 3 Evaluate check + Tier 4 command enqueue + suppression check + unit tests
- [ ] 48-04: Priority group traversal (parallel within group, sequential across groups, advance gate) + integration tests

---

#### Phase 49: Observability & Dashboard

**Goal**: Operators can see command execution activity in Grafana — three snmp.command.* panels are visible on the operations dashboard, and pod logs contain structured per-tenant evaluation records and per-command round-trip durations
**Depends on**: Phase 48 (core loop must be stable before observability add-ons)
**Requirements**: SNAP-16, SNAP-17
**Success Criteria** (what must be TRUE):
  1. The operations dashboard shows three new panels in the Pipeline Counters group (Row 5) — `snmp.command.sent`, `snmp.command.failed`, and `snmp.command.suppressed` — each 8 units wide on a single row
  2. Pod logs for a successful SET contain an Information-level entry with device name, command name, and round-trip duration in milliseconds
  3. Pod logs for a failed SET contain a Warning-level entry with device name, command name, the error, and round-trip duration in milliseconds

**Plans:** 2 plans

Plans:
- [ ] 49-01: Command execution logs (Stopwatch around SetAsync, Information/Warning level) + dashboard panels

---

#### Phase 50: Label Rename (metric_name → resolved_name)

**Goal**: Rename the `metric_name` label to `resolved_name` across all snmp_gauge and snmp_info instruments — the label serves both metric names (from oid_metric_map) and command names (from oid_command_map), and the current name is misleading for SET responses
**Depends on**: Phase 49 (all v2.0 functionality must be stable before breaking label change)
**Requirements**: SNAP-18
**Success Criteria** (what must be TRUE):
  1. All `snmp_gauge` and `snmp_info` time series use `resolved_name` label instead of `metric_name`
  2. Both Grafana dashboards updated with new label name in all PromQL queries
  3. E2E test assertions updated to use `resolved_name`

**Plans:** 2 plans

Plans:
- [ ] 50-01: Rename metric_name → resolved_name in SnmpMetricFactory + dashboards + E2E tests

---

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 25. Config Models | v1.5 | 1/1 | Complete | 2026-03-10 |
| 26. Core Data Types | v1.5 | 2/2 | Complete | 2026-03-10 |
| 27. Pipeline Integration | v1.5 | 2/2 | Complete | 2026-03-10 |
| 28. ConfigMap Watcher | v1.5 | 2/2 | Complete | 2026-03-10 |
| 29. K8s Deployment | v1.5 | 2/2 | Complete | 2026-03-10 |
| 30. OID Map Integrity | v1.6 | 2/2 | Complete | 2026-03-13 |
| 31. Human-Name Device Config | v1.6 | 3/3 | Complete | 2026-03-13 |
| 32. Command Map Infrastructure | v1.6 | 3/3 | Complete | 2026-03-13 |
| 33. Config Model Additions | v1.7 | 2/2 | Complete | 2026-03-14 |
| 34. CommunityString Validation | v1.7 | 2/2 | Complete | 2026-03-14 |
| 35. Watcher-Registry Refactor | v1.7 | 2/2 | Complete | 2026-03-15 |
| 36. Config File Renames | v1.7 | 2/2 | Complete | 2026-03-15 |
| 37. Config and Runtime Models | v1.8 | 1/1 | Complete | 2026-03-15 |
| 38. DeviceWatcherService Validation | v1.8 | 1/1 | Complete | 2026-03-15 |
| 39. Pipeline Bypass Guards | v1.8 | 1/1 | Complete | 2026-03-15 |
| 40. MetricPollJob Aggregate Dispatch | v1.8 | 1/1 | Complete | 2026-03-15 |
| 41. Threshold Model & Holder Storage | v1.9 | 1/1 | Complete | 2026-03-15 |
| 42. Threshold Validation & Config Files | v1.9 | 2/2 | Complete | 2026-03-15 |
| 43. Heartbeat Cleanup | v1.10 | 1/1 | Complete | 2026-03-15 |
| 44. Pipeline Liveness | v1.10 | 2/2 | Complete | 2026-03-15 |
| 45. Structural Prerequisites | v2.0 | 2/2 | Complete | 2026-03-16 |
| 46. Infrastructure Components | v2.0 | 3/3 | Complete | 2026-03-16 |
| 47. CommandWorkerService | v2.0 | 2/2 | Complete | 2026-03-16 |
| 48. SnapshotJob 4-Tier Evaluation | v2.0 | 0/4 | Not started | - |
| 49. Observability & Dashboard | v2.0 | 0/1 | Not started | - |
| 50. Label Rename | v2.0 | 0/1 | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-16 after v2.0 milestone roadmap created*
