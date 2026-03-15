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
- 🚧 **v1.8 Combined Metrics** - Phases 37-40 (in progress)

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

### 🚧 v1.8 Combined Metrics (In Progress)

**Milestone Goal:** Poll groups can declare an aggregate computation (sum, subtract, absDiff, mean) over their individual SNMP GET responses. The collector computes the result and dispatches it as a named synthetic gauge through the full MediatR pipeline, appearing in Prometheus with `source="synthetic"` alongside the individual per-OID metrics.

#### Phase 37: Config and Runtime Models

**Goal**: The data types that describe a combined metric — config model, runtime record, and aggregation enum — exist with backward-compatible defaults so all downstream phases can reference stable types
**Depends on**: Nothing (type-only additions; no behavior change)
**Requirements**: CM-01
**Success Criteria** (what must be TRUE):
  1. A `PollOptions` entry with no `AggregatedMetricName` or `Aggregator` fields deserializes and behaves identically to before — no regression to existing poll group behavior
  2. A `PollOptions` entry with both `AggregatedMetricName` and `Aggregator` set deserializes without error and the values are accessible on the model
  3. `AggregationKind` enum has `Sum`, `Subtract`, `AbsDiff`, and `Mean` members; `CombinedMetricDefinition` runtime record holds MetricName, AggregationKind, and source OIDs list; both types compile and are referenceable from MetricPollInfo
  4. `MetricPollInfo` carries a `AggregatedMetrics` collection with a default empty value — existing `MetricPollInfo` construction sites require no changes

**Plans**: TBD

Plans:
- [x] 37-01: AggregationKind enum, CombinedMetricDefinition record, PollOptions + MetricPollInfo extensions + 13 unit tests

---

#### Phase 38: DeviceWatcherService Validation

**Goal**: Combined metric definitions in devices.json are validated at load time — invalid aggregator values and mismatched field presence are rejected with Error logs, name collisions with the OID map produce Warning logs, and valid definitions are resolved to `CombinedMetricDefinition` records stored on `MetricPollInfo`
**Depends on**: Phase 37 (AggregatedMetricOptions and CombinedMetricDefinition types must exist)
**Requirements**: CM-02, CM-03, CM-11, CM-12
**Success Criteria** (what must be TRUE):
  1. A poll group with `Aggregator: "invalid"` produces a structured Error log naming the device and poll group, and that poll group's combined metric definition is skipped — its individual OID polling still loads normally
  2. A poll group with `AggregatedMetricName` set but no `Aggregator` (or vice versa) produces a structured Error log — partial configuration is never silently accepted
  3. A poll group with a valid `AggregatedMetricName` that matches an existing OID map metric name produces a structured Warning log — the poll group loads normally and both metrics are distinguishable by their `oid` and `source` labels in Prometheus
  4. A fully valid combined metric definition results in a populated `CombinedMetricDefinition` on the corresponding `MetricPollInfo`, with resolved source OIDs ready for poll-time use

**Plans**: TBD

Plans:
- [x] 38-01: BuildPollGroups 5-rule validation + 10 unit tests (CM-02, CM-03, CM-11, CM-12)

---

#### Phase 39: Pipeline Bypass Guards

**Goal**: The MediatR pipeline safely passes synthetic messages through without corrupting their pre-set MetricName — `SnmpSource.Synthetic` exists as the discriminant, `OidResolutionBehavior` bypasses OID lookup for synthetic messages, and `ValidationBehavior` passes them through OID regex without rejection
**Depends on**: Phase 37 (SnmpSource enum lives alongside other pipeline types; Synthetic must be added before any message uses it)
**Requirements**: CM-04, CM-05, CM-06
**Success Criteria** (what must be TRUE):
  1. `SnmpSource.Synthetic` is a valid enum member and a synthetic `SnmpOidReceived` with `Source = SnmpSource.Synthetic` can be constructed and sent through the pipeline without compile errors
  2. A synthetic message with a pre-set `MetricName` of `"obp_combined_power"` exits `OidResolutionBehavior` with `MetricName` still equal to `"obp_combined_power"` — `OidMapService.Resolve` is never called for it
  3. A synthetic message with `Oid = "0.0"` (or the chosen sentinel) passes `ValidationBehavior` without being rejected — the OID regex guard does not block synthetic messages
  4. All existing non-synthetic pipeline behavior is unchanged — a regular Poll or Trap message still goes through full OID resolution as before

**Plans**: TBD

Plans:
- [x] 39-01: SnmpSource.Synthetic + OidResolution bypass + sentinel "0.0" + 4 tests (CM-04, CM-05, CM-06)

---

#### Phase 40: MetricPollJob Aggregate Dispatch

**Goal**: After completing individual per-varbind dispatches, MetricPollJob computes the configured aggregate and dispatches it as a named synthetic gauge through the MediatR pipeline — appearing in Prometheus with correct labels, incrementing the combined counter, logging skips with structured warnings, and routing to tenant vector slots
**Depends on**: Phase 38 (CombinedMetricDefinition on MetricPollInfo must be populated), Phase 39 (pipeline bypass guards must exist before any synthetic message is dispatched)
**Requirements**: CM-07, CM-08, CM-09, CM-10, CM-13, CM-14, CM-15
**Success Criteria** (what must be TRUE):
  1. A poll group with `AggregatedMetricName: "obp_combined_power"` and `Aggregator: "sum"` produces a `snmp_gauge` metric in Prometheus with labels `metric_name="obp_combined_power"`, `source="synthetic"`, `oid="0.0"` (or the chosen sentinel), and a numeric value equal to the sum of the individual OID values from that poll cycle
  2. When any source OID in a combined group returns an error or a non-numeric (snmp_info) value, no combined metric is emitted for that cycle and a Warning log entry is written naming the device, poll group, and reason — the individual per-OID metrics for that cycle are unaffected
  3. `snmp.combined.computed` counter increments by 1 each time a combined metric is successfully computed and dispatched — a poll cycle with no combined groups or a skipped combined group does not increment this counter
  4. A tenant that has registered `"obp_combined_power"` in its Metrics[] array receives the synthetic metric value in its tenant vector slot — the routing key `(ip, port, metricName)` resolves correctly for synthetic metrics
  5. An exception inside the combined metrics computation block is caught and logged as an Error naming combined metric computation as the source — it does not increment `snmp_poll_unreachable_total` and does not prevent individual varbind metrics from having been dispatched

**Plans**: TBD

Plans:
- [x] 40-01: DispatchAggregatedMetricAsync — sum/subtract/absDiff/mean, all-or-nothing guard, snmp.aggregated.computed counter, exception isolation + 10 tests (CM-07–10, CM-13–15)

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

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-15 after v1.8 milestone roadmap added*
