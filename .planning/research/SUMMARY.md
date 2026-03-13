# Research Summary: v1.6 Organization & Command Map Foundation

**Project:** SnmpCollector — v1.6 Organization and Command Map Foundation
**Domain:** SNMP monitoring agent — OID map integrity, human-name device config, command map infrastructure
**Researched:** 2026-03-13
**Confidence:** HIGH (all findings derived from direct codebase analysis; zero speculation)

---

## Executive Summary

This milestone adds three largely independent capability blocks to the existing SNMP collector agent: OID map integrity validation (catching duplicate OIDs and duplicate metric names before they cause silent data loss), human-name device config (letting operators reference metric names like `"obp_channel_L1"` instead of raw OID strings in `devices.json`), and command map infrastructure (a new `CommandMapService` + `CommandMapWatcherService` that holds the lookup table from command name to SET OID — the prerequisite for any future SNMP write operations). All three blocks are pure pattern-replication work against the existing `OidMapService` / `OidMapWatcherService` architecture. Zero new NuGet packages are required.

The recommended approach is strict sequencing within the human-name block: build the OID map reverse index (`OidMapService._reverseMap` + `IOidMapService.ResolveOid`) first, because it is the foundation that `DeviceWatcherService` human-name resolution depends on. OID map duplicate validation is independent of the reverse index and can be built in parallel. Command map infrastructure (`CommandMapService` + `CommandMapWatcherService`) has no dependency on either of the other two blocks and can be built at any point. All three blocks are additive — no existing callers break, backward compatibility is explicit in the feature set (TS-07: `Oids[]` and `Metrics[]` coexist in any poll group).

The primary risks in this milestone are not architectural: they are operational sequencing traps. The most dangerous is Pitfall 3 — deploying `devices.json` entries with human names before the resolution path in `DeviceWatcherService` is in place, which causes `MetricPollJob` to pass metric name strings to SharpSnmpLib's `ObjectIdentifier` constructor and throw `FormatException` on every poll (looks like all affected devices became simultaneously unreachable). The second-most dangerous is Pitfall 4: if human-name resolution happens at schedule time rather than at device-load time, an OID map hot-reload silently leaves `MetricPollInfo.Oids` with stale OID strings. The research recommendation is **Strategy A** — resolve at device-load time in `DeviceWatcherService`, not at poll time in `MetricPollJob` — lower risk and consistent with the existing architecture's graceful-degrade philosophy.

---

## Key Findings

### Recommended Stack

Zero new dependencies. Every data structure and pattern needed is already present in the codebase or in the .NET 9 BCL. The `FrozenDictionary<string, string>` volatile-swap pattern used by `OidMapService` and `DeviceRegistry` is directly replicable for `CommandMapService` and for the new `_reverseMap` field on `OidMapService`. `HashSet<string>` (BCL) handles duplicate detection. `BackgroundService` (Microsoft.Extensions.Hosting 9.0.0, already a dependency) is the base for `CommandMapWatcherService`. The Kubernetes client (KubernetesClient 18.0.13, already a dependency) handles the ConfigMap watch loop with reconnect.

**Core components (all existing, no version changes, no `.csproj` modifications):**
- `System.Collections.Frozen.FrozenDictionary` — lock-free O(1) lookup for all map reads; volatile-swap pattern for atomic hot-reload (used in `OidMapService`, `DeviceRegistry`, `TenantVectorRegistry`)
- `System.Collections.Generic.HashSet<string>` — duplicate detection during map load (OID key uniqueness, metric name value uniqueness)
- `Microsoft.Extensions.Hosting.BackgroundService` — base class for `CommandMapWatcherService`; mirrors `OidMapWatcherService` exactly
- `KubernetesClient 18.0.13` — K8s ConfigMap watch API with 5s reconnect loop and `SemaphoreSlim` reload serialization (established pattern, clone from `OidMapWatcherService`)
- `Lextm.SharpSnmpLib 12.5.7` — no changes; command map OIDs are future SET targets not consumed by this milestone

See `.planning/research/STACK.md` for full pattern reuse map and per-feature stack decisions.

---

### Expected Features

This milestone has 12 table-stakes features, 4 high-value differentiators to build in the same milestone, 2 medium-complexity differentiators to evaluate, and 7 explicit anti-features.

**Must have — table stakes (all 12):**
- **TS-01** Duplicate OID key detection in OID map load — prevents silent last-write-wins clobber on copy-paste errors in `oidmaps.json`
- **TS-02** Duplicate metric name detection in OID map load — prevents two OIDs sharing a name and creating Prometheus cardinality ambiguity
- **TS-03** `Metrics[]` field on `MetricPollOptions` — new `List<string>` property accepting human metric names alongside existing `Oids[]`
- **TS-04** Human name resolution at device load time in `DeviceWatcherService` — translates `Metrics[]` entries to OID strings before `DeviceRegistry.ReloadAsync`
- **TS-05** Unknown metric name warning during resolution — logs structured warning per unresolvable name; skips entry, does not reject whole device
- **TS-06** `OidMapService` reverse index — second `FrozenDictionary` (metric name → OID), built alongside forward map in `UpdateMap`, exposed via `IOidMapService.ResolveOid(string metricName)`
- **TS-07** Backward compat — `Oids[]` and `Metrics[]` coexist in any poll group, allowing incremental migration of `devices.json`
- **TS-08** `commandmaps.json` file format definition — flat JSON object (OID → command name), same schema as `oidmaps.json`
- **TS-09** `CommandMapService` — volatile FrozenDictionary singleton; forward (OID → name) + reverse (name → OID); `ResolveCommandOid`, `EntryCount`, `UpdateMap`
- **TS-10** `CommandMapWatcherService` — `BackgroundService` watching `simetra-commandmaps` ConfigMap, structural clone of `OidMapWatcherService`
- **TS-11** Local dev fallback for `CommandMapWatcherService` — load `config/commandmaps.json` from filesystem in non-K8s mode, mirroring `Program.cs` `OidMapService` init pattern
- **TS-12** Command map duplicate validation — same OID key + command name value duplicate detection as TS-01/TS-02, applied to `commandmaps.json` on load

**Should have — differentiators (build in same milestone, low cost):**
- **D-01** Structured log format for OID map warnings (`OidMapDuplicateOid`, `OidMapDuplicateName` event tags) — enables Loki alerting on config errors
- **D-04** `CommandMapService` diff logging on reload — identical to existing `OidMapService` diff pattern; essential for hot-reload observability
- **D-05** `Metrics[]` name convention validation — rejects OID-format strings accidentally placed in `Metrics[]` instead of `Oids[]`
- **D-06** Entry count logging for `CommandMapService` at startup and on reload — makes empty command map (parse error, missing ConfigMap) immediately visible

**Evaluate before committing:**
- **D-02** Reload diff including metric name translation changes — useful but requires cross-watcher diff awareness between `OidMapService` and `DeviceRegistry`
- **D-03** OID map change triggers device config re-resolution — significant cross-watcher coupling; weigh against simpler alternative (document that re-resolution requires touching `devices.json`)

**Explicitly do NOT build — anti-features:**
- AF-01: SNMP SET command execution
- AF-02: Command authorization / access control
- AF-03: Command parameter schemas or typed command request objects
- AF-04: Mandatory migration of `devices.json` to human names
- AF-05: Separate per-device-type command ConfigMaps
- AF-06: Command map HTTP API or Prometheus export
- AF-07: Hard startup failure on unresolvable metric names (soft warning, TS-05)

See `.planning/research/FEATURES.md` for full dependency graph, MVP recommendation, and feature complexity breakdown.

---

### Architecture Approach

All new components fit inside the existing `Pipeline/` (services, interfaces) and `Services/` (watchers) directory structure with no new directories. The core architectural pattern is unchanged: every hot-reloadable map is a `volatile FrozenDictionary` on a singleton service, swapped atomically by a corresponding `BackgroundService` watcher that watches a Kubernetes ConfigMap. `CommandMapService` becomes the fourth instance of this pattern (alongside `OidMapService`, `DeviceRegistry`, and `TenantVectorRegistry`).

**Modified components:**
1. `Services/OidMapWatcherService.cs` — add `ValidateDuplicateMetricNames()` private method called after JSON parse, before `UpdateMap`; validation is advisory (log warning, continue reload) — ~20 lines
2. `Pipeline/IOidMapService.cs` — add `ResolveOid(string metricName)` method to interface — 1 method
3. `Pipeline/OidMapService.cs` — add `_reverseMap` volatile `FrozenDictionary`, `BuildReverseMap()`, `ResolveOid()` impl; call `BuildReverseMap` in constructor and `UpdateMap` — ~20 lines
4. `Services/DeviceWatcherService.cs` — add `IOidMapService` constructor dependency; add `ResolveHumanNames()` private method; call between JSON parse and `DeviceRegistry.ReloadAsync` — ~40 lines
5. `Extensions/ServiceCollectionExtensions.cs` + `Program.cs` — DI registration and local-dev init for `ICommandMapService` and `CommandMapWatcherService` — ~25 lines combined

**New files:**
- `Pipeline/ICommandMapService.cs` — interface: `ResolveCommandOid(string)`, `EntryCount`, `UpdateMap(Dictionary<string,string>)`
- `Pipeline/CommandMapService.cs` — volatile FrozenDictionary singleton; structural mirror of `OidMapService`
- `Services/CommandMapWatcherService.cs` — `BackgroundService`; clone of `OidMapWatcherService` with `ConfigMapName = "simetra-commandmaps"`, `ConfigKey = "commandmaps.json"`
- `config/commandmaps.json` — local dev fallback command map file
- `deploy/k8s/snmp-collector/simetra-commandmaps.yaml` — Kubernetes ConfigMap manifest

**Files that do NOT change:** `MetricPollJob`, `DeviceRegistry`, `DynamicPollScheduler`, `OidResolutionBehavior`, `TenantVectorWatcherService`, `TenantVectorRegistry`, all MediatR pipeline behaviors.

**Data flow summary:**

```
OID map load (modified):
  simetra-oidmaps → OidMapWatcherService
                      [NEW] ValidateDuplicateMetricNames() → log warnings, continue
                    → OidMapService.UpdateMap
                        BuildFrozenMap()    → volatile swap _map
                        [NEW] BuildReverseMap() → volatile swap _reverseMap

Device load (modified):
  simetra-devices → DeviceWatcherService
                      [NEW] ResolveHumanNames(devices, _oidMapService)
                            per OID entry: if IsMetricName → ResolveOid → replace
                    → DeviceRegistry.ReloadAsync → DynamicPollScheduler.ReconcileAsync

Command map load (new path):
  simetra-commandmaps → CommandMapWatcherService → CommandMapService.UpdateMap
                                                     BuildFrozenMap() → volatile swap
```

**Critical ordering note:** Duplicate detection in the OID map watcher must run BEFORE `OidMapService.UpdateMap()` is called and before any diff log computation (Pitfall 1). If placed inside `UpdateMap` after diff computation, phantom "added" log entries appear for maps that were subsequently rejected.

**Build order (recommended):**
1. **Step 1** — `OidMapService` reverse index + `IOidMapService.ResolveOid` (no dependencies; foundation for Step 3)
2. **Step 2** — OID map duplicate validation in `OidMapWatcherService` (independent of Steps 1 and 3; can run in parallel)
3. **Step 3** — Human-name resolution in `DeviceWatcherService` (depends on Step 1; `ResolveOid` must exist)
4. **Step 4** — `CommandMapService` + `ICommandMapService` (no dependency on Steps 1-3; fully parallel)
5. **Step 5** — `CommandMapWatcherService` + DI + config files (depends on Step 4)

See `.planning/research/ARCHITECTURE.md` for component boundaries, full integration sketches, anti-patterns, and startup ordering analysis.

---

### Critical Pitfalls

**Top 5 from the 13 identified in PITFALLS.md:**

1. **Human names in `devices.json` deployed before resolution path exists (Pitfall 3 — CRITICAL)** — `MetricPollJob` passes metric name strings to `ObjectIdentifier(oid)` in SharpSnmpLib, which throws `FormatException`. Every poll for every affected device fails, presenting as simultaneous unreachability. Prevention: implement and deploy `DeviceWatcherService.ResolveHumanNames()` (Step 3) before any `devices.json` format change reaches the cluster. The phase plan must enforce this sequencing as a deployment gate.

2. **Duplicate validation placement emits phantom log entries on rejection (Pitfall 1 — CRITICAL)** — Adding duplicate detection inside `OidMapService.UpdateMap()` after diff computation means rejected maps still produce "OidMap added: X → Y" log lines. Operators see contradictory signals: entries appear added but the reload-complete log is absent. Prevention: validate in `OidMapWatcherService.HandleConfigMapChangedAsync()` before calling `UpdateMap()`, not inside `UpdateMap()`.

3. **Strategy B (resolve at schedule time) leaves stale OIDs after OID map hot-reload (Pitfall 4 — CRITICAL)** — If `DeviceWatcherService` resolves names to OIDs and stores them in `MetricPollInfo.Oids`, a subsequent OID map ConfigMap change does not trigger device reload. `MetricPollInfo.Oids` retains old OID strings; metrics appear under wrong names or as "Unknown" with no errors in logs. Prevention: adopt **Strategy A** — resolve in `DeviceWatcherService` before `DeviceRegistry.ReloadAsync`, which re-resolves on every device-config reload using the current live OID map.

4. **OID map rename silently breaks TenantVectorRegistry routing (Pitfall 5 — CRITICAL)** — `TenantVectorRegistry` routing index is keyed by `(ip, port, metricName)`. After an OID map rename, `OidResolutionBehavior` immediately uses the new name, but `TenantVectorFanOutBehavior` routes against the old name from the stale index. Tenant metric slots go dark with no errors in logs. Prevention: on OID map hot-reload, also trigger `TenantVectorRegistry.Reload()` — or explicitly document this as a known limitation and defer cross-watcher coordination to a dedicated phase. This is a pre-existing architectural gap not introduced by this milestone.

5. **Validation comparer mismatch between validation pass and runtime (Pitfall 10 — MODERATE)** — Using `StringComparer.Ordinal` in duplicate detection but `StringComparer.OrdinalIgnoreCase` at runtime means case-variant OID duplicates pass validation but still collapse silently in the `FrozenDictionary`. Prevention: use `StringComparer.OrdinalIgnoreCase` in all validation passes to match runtime dictionary semantics. Document the comparer choice in a code comment.

**Also important (moderate pitfalls to address in phase plans):**
- **Pitfall 6** — `CommandMapWatcherService` registered only in K8s path without local-dev `Program.cs` init → empty command map in all development runs
- **Pitfall 7** — `DevicesOptionsValidator` has no device name uniqueness check → `DeviceRegistry._byName` silently overwrites duplicate names
- **Pitfall 8** — Format change to `devices.json` breaks all E2E fixture files simultaneously → audit and update fixtures atomically in the same commit
- **Pitfall 9** — Changing Quartz job key scheme causes delete-all/add-all churn → keep IP-based job keys; document explicitly
- **Pitfall 12** — ConfigMap name for command map not established as a constant → decide `"simetra-commandmaps"` in planning; derive all references from one constant
- **Pitfall 13** — Heartbeat OID seed skipped by pre-merge validation → run duplicate validation against merged dictionary (after `MergeWithHeartbeatSeed`); reject `"Heartbeat"` as a user-supplied metric name value

See `.planning/research/PITFALLS.md` for the full 13-pitfall catalog with detection signals, code-level evidence, and phase tags.

---

## Implications for Roadmap

The research identifies three independent capability blocks with clear internal dependency ordering and no cross-block dependencies. The natural phase structure is one phase per block, with internal steps ordered by dependency.

### Phase A: OID Map Integrity (Validation + Reverse Index)

**Rationale:** Foundational. The reverse index (TS-06, `IOidMapService.ResolveOid`) is the prerequisite for all human-name device config work. Duplicate validation (TS-01, TS-02) touches the same files (`OidMapService.cs`, `OidMapWatcherService.cs`) and the same test surface. Co-locating them in one phase avoids two separate PRs to the same service. This phase has zero dependencies on anything in Phases B or C.

**Delivers:** Structured duplicate warnings on OID map load (TS-01, TS-02, D-01); `IOidMapService.ResolveOid(string metricName)` new interface method (TS-06); `OidMapService._reverseMap` volatile `FrozenDictionary` built and swapped alongside forward map; unit tests covering duplicate rejection, reverse lookup, case-insensitivity, heartbeat seed inclusion.

**Addresses:** TS-01, TS-02, TS-06, D-01

**Avoids:** Pitfall 1 (validation before diff/swap), Pitfall 2 (value-uniqueness check, not just key-uniqueness), Pitfall 10 (comparer consistency — use `OrdinalIgnoreCase`), Pitfall 11 (test coverage in same commit), Pitfall 13 (validate against merged dictionary; reject `"Heartbeat"` as user value)

**Research flag:** Standard patterns — no additional research needed. `OidMapService` and `OidMapWatcherService` are fully readable, patterns are established.

---

### Phase B: Human-Name Device Config

**Rationale:** Depends on Phase A (`IOidMapService.ResolveOid` must exist). This is the highest operational-value feature of the milestone — eliminates raw OID strings from `devices.json`. Must be sequenced so the resolution path is deployed before any format change to `devices.json` in the cluster (Pitfall 3). Also requires auditing and updating all E2E fixture YAML files atomically (Pitfall 8).

**Delivers:** `Metrics[]` field on `MetricPollOptions` (TS-03); `ResolveHumanNames()` in `DeviceWatcherService` using Strategy A (TS-04); graceful-degrade on OID map startup race (unresolved names logged as warnings, entry left as raw string); unknown name warnings (TS-05); `Oids[]` / `Metrics[]` coexistence (TS-07); name convention validation rejecting OID-format strings in `Metrics[]` (D-05); `config/devices.json` updated to human names; all E2E device fixtures updated atomically.

**Addresses:** TS-03, TS-04, TS-05, TS-07, D-05

**Avoids:** Pitfall 3 (resolution before format change — deployment gate), Pitfall 4 (Strategy A, not B), Pitfall 7 (add device name uniqueness to `DevicesOptionsValidator`), Pitfall 8 (E2E fixture audit — all fixtures updated in same commit), Pitfall 9 (IP-based Quartz job keys unchanged — document explicitly)

**Decision to record in phase plan:** Confirm Strategy A as the official resolution strategy. Document in `DeviceWatcherService` why resolution is at device-load time, not poll time.

**Research flag:** MEDIUM complexity. The startup race between `OidMapWatcherService` and `DeviceWatcherService` should be verified with an explicit test (feed `DeviceWatcherService` a config with human names when `OidMapService._reverseMap` is empty; assert graceful-degrade behavior). The logic is correct by design; the test confirms it.

---

### Phase C: Command Map Infrastructure

**Rationale:** Fully independent of Phases A and B — no cross-dependency. The command map is the prerequisite for any future SNMP SET milestone. Building it while the watcher pattern is fresh costs very little (structural clone of `OidMapWatcherService`). Can be parallelized with Phase A or B if capacity allows, or sequenced after Phase B.

**Delivers:** `ICommandMapService` interface (TS-09); `CommandMapService` with volatile FrozenDictionary forward + reverse maps, diff logging (D-04), entry count logging (D-06) (TS-09); `commandmaps.json` format definition (TS-08); `CommandMapWatcherService` with K8s watch, local dev fallback, SemaphoreSlim, graceful delete handling (TS-10, TS-11); command map duplicate validation on load (TS-12); `config/commandmaps.json` local dev file; `deploy/k8s/snmp-collector/simetra-commandmaps.yaml`; DI registration in both K8s and local-dev paths.

**Addresses:** TS-08, TS-09, TS-10, TS-11, TS-12, D-04, D-06

**Avoids:** Pitfall 6 (local-dev init path in `Program.cs` — mirrors `OidMapService` pattern exactly), Pitfall 12 (ConfigMap name `"simetra-commandmaps"` as a constant — decide before any code is written)

**Research flag:** Standard patterns — structural copy of `OidMapWatcherService`. No additional research needed. Only planning decision required is locking the ConfigMap name constant before any code begins.

---

### Phase Ordering Rationale

- Phase A before Phase B: `IOidMapService.ResolveOid` must exist before `DeviceWatcherService` can call it. Attempting to build Phase B before Phase A would require mocking or stubbing the new interface method.
- Phase A and Phase C are independent: both can be built in parallel by separate developers, or sequenced A → C.
- Phase B deployment must precede any `devices.json` format change to the cluster: this is a deployment sequencing constraint, not a code sequencing constraint. The phase plan must include a deployment gate step.
- D-02 and D-03 deferred pending evaluation: cross-watcher reload coordination adds coupling between independently-operating watchers. The simpler alternative for D-03 — documenting that adding a new OID map entry requires a `devices.json` touch to re-trigger name resolution — is acceptable for most operational workflows. Confirm with operators before committing to cross-watcher coupling.

### Research Flags

**Needs explicit testing (not additional research):**
- Phase B: startup race test — `DeviceWatcherService` with human-name config against empty `OidMapService`; assert graceful-degrade produces warnings, does not throw, proceeds with raw strings

**Standard patterns (no research-phase needed):**
- Phase A: `OidMapService` modifications are self-contained; patterns established in 3+ locations
- Phase C: structural clone of `OidMapWatcherService`; no novel patterns

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All patterns in use in 3+ locations in the codebase. Zero new packages. Verified against `.csproj`. No dependency version conflicts. |
| Features | HIGH | Derived from direct codebase analysis of all referenced source files. Scale estimates for OBP (744 command instances, 1,040 metric instances) verified from `Docs/OBP-Device-Analysis.md`. NPB counts are MEDIUM — taken from milestone description, not independently verified against the full NPB-Device-Analysis.md text. |
| Architecture | HIGH | All integration points verified by direct file reads. Startup race condition is a known pattern in this codebase (independent `BackgroundService` watchers). Graceful-degrade strategy matches existing patterns. No new architectural patterns introduced. |
| Pitfalls | HIGH | All 13 pitfalls verified against specific source file locations and line references. Pitfall 5 (TenantVectorRegistry routing staleness) is a pre-existing gap that this milestone does not introduce but also does not close. |

**Overall confidence:** HIGH

### Gaps to Address

1. **NPB command and metric counts** (MEDIUM confidence): The milestone description states approximately 250 NPB command objects and 390 NPB metrics. These counts should be verified against `Docs/NPB-Device-Analysis.md` before `commandmaps.json` is populated for Phase C. The count affects ConfigMap size expectations and initial file validation effort.

2. **D-03 decision — OID map change triggers device re-resolution**: Research identifies this as a real user-facing issue (adding a new OID map entry to fix an unresolvable `Metrics[]` name does not automatically take effect until `devices.json` is re-applied). The roadmap should explicitly record whether D-03 is deferred to a future milestone or included in Phase B with an explicit cross-watcher notification mechanism. The research recommendation is to defer and document, but this requires product confirmation.

3. **Pitfall 5 scope decision — TenantVectorRegistry routing staleness**: If OID map renames are planned in production during this milestone (e.g., as part of cleaning up OID naming conventions enabled by the new validation), Pitfall 5 must be mitigated in Phase A. If no OID renames are planned, it can be deferred. This decision must be recorded in the Phase A plan before coding starts.

4. **ConfigMap name finalization**: `"simetra-commandmaps"` is the research recommendation (mirrors `"simetra-oidmaps"` naming convention). This must be locked before Phase C begins. Any deviation cascades to RBAC rules, Helm chart templates, E2E fixture files, and the watcher constant. Record as a named decision in Phase C plan.

---

## Sources

### Primary — HIGH Confidence (direct codebase analysis)

- `src/SnmpCollector/Pipeline/OidMapService.cs` — internal state, `UpdateMap` sequence, `MergeWithHeartbeatSeed`, `BuildFrozenMap`, `ContainsMetricName`, `EntryCount`
- `src/SnmpCollector/Pipeline/IOidMapService.cs` — current interface surface; confirmed `ResolveOid` is absent
- `src/SnmpCollector/Services/OidMapWatcherService.cs` — `HandleConfigMapChangedAsync` parse/validate/update sequence; watcher pattern template for `CommandMapWatcherService`
- `src/SnmpCollector/Services/DeviceWatcherService.cs` — constructor dependencies; `HandleConfigMapChangedAsync` flow; confirmed `IOidMapService` not yet injected
- `src/SnmpCollector/Services/DynamicPollScheduler.cs` — job key format `metric-poll-{configAddress}_{port}-{pi}`; `ReconcileAsync` diff logic
- `src/SnmpCollector/Jobs/MetricPollJob.cs` — direct `new ObjectIdentifier(oid)` without pre-validation; source of Pitfall 3
- `src/SnmpCollector/Pipeline/TenantVectorRegistry.cs` — routing index keyed by metric name; no oidmap-change trigger; source of Pitfall 5
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` — `_byName` silent overwrite on duplicate name; `ReloadAsync` signature
- `src/SnmpCollector/Configuration/Validators/DevicesOptionsValidator.cs` — IP+Port duplicate check; confirmed no name uniqueness check; source of Pitfall 7
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — `IsInCluster()` gate pattern; local-dev else branch
- `src/SnmpCollector/Configuration/MetricPollOptions.cs` — `Oids: List<string>`; confirmed `Metrics` field absent
- `src/SnmpCollector/config/oidmaps.json` — 98 entries (27 OBP + 71 NPB); flat JSON object format confirmed
- `src/SnmpCollector/config/devices.json` — raw OID format in `MetricPolls.Oids`; 26 OBP OIDs + 70 NPB OIDs
- `src/SnmpCollector/SnmpCollector.csproj` — confirmed package versions; no new packages needed
- `tests/e2e/scenarios/` — 27 scenarios; fixture-based YAML format; manual maintenance
- `tests/SnmpCollector.Tests/Pipeline/OidMapServiceTests.cs` — existing test coverage; confirmed no duplicate metric name tests
- `Docs/OBP-Device-Analysis.md` — 8 NMU + 23 per-link × 32 links = 744 command instances; 16 NMU + 32 per-link × 32 links = 1,040 metric instances

### Secondary — MEDIUM Confidence

- Milestone description — NPB approximately 250 command objects, approximately 390 metrics (not independently verified against `Docs/NPB-Device-Analysis.md`)

---

*Research completed: 2026-03-13*
*Ready for roadmap: yes*
