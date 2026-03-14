# Roadmap: SNMP Monitoring System

## Milestones

- ✅ **v1.0 Foundation** - Phases 1-10 (shipped 2026-03-07)
- ✅ **v1.1 Device Simulation** - Phases 11-14 (shipped 2026-03-08)
- ✅ **v1.2 Operational Enhancements** - Phases 15-16 (shipped 2026-03-08)
- ✅ **v1.3 Grafana Dashboards** - Phases 18-19 (shipped 2026-03-09)
- ✅ **v1.4 E2E System Verification** - Phases 20-24 (shipped 2026-03-09)
- ✅ **v1.5 Priority Vector Data Layer** - Phases 25-29 (shipped 2026-03-10)
- ✅ **v1.6 Organization & Command Map Foundation** - Phases 30-32 (shipped 2026-03-13)
- 🚧 **v1.7 Configuration Consistency & Tenant Commands** - Phases 33-36 (in progress)

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

### 🚧 v1.7 Configuration Consistency & Tenant Commands

**Milestone Goal:** CommunityString becomes explicit and validated across all config layers, tenant entries become self-describing with a full Commands data model, TenantVectorRegistry drops its DeviceRegistry/OidMapService dependencies, and all config file names align to a consistent naming convention.

---

#### Phase 33: Config Model Additions

**Goal**: All new C# types and fields that v1.7 requires exist in the codebase — tenant entries are fully self-describing in the options layer, the Commands data model has its complete shape, and optional observability fields are present. All additions are backward-compatible with existing configs.
**Depends on**: Nothing (purely additive)
**Requirements**: CS-01, CS-02, CS-05, TEN-01, TEN-02, TEN-09, TEN-10, TEN-12
**Success Criteria** (what must be TRUE):
  1. `DeviceOptions` has a `CommunityString` property holding the full credential string (e.g. `"Simetra.NPB-01"`), and `DeviceInfo.Name` is derived from it via `CommunityStringHelper.TryExtractDeviceName()` at load time — no consumer has to change its use of the short name
  2. `MetricSlotOptions` has optional `IntervalSeconds` (int, default 0) and required `Role` (string) fields
  3. `TenantOptions` has a `Commands` list of `CommandSlotOptions` (Ip, Port, CommandName, Value, ValueType) and optional `Name`
  4. `TenantVectorRegistry` constructor no longer takes `IOidMapService`; `DeriveIntervalSeconds()` deleted; uses `metric.IntervalSeconds` directly
  5. `MetricPollJob` uses `device.CommunityString` directly with no fallback derivation
  6. All config files use `"CommunityString": "Simetra.XXX"` instead of `"Name": "XXX"`

**Plans:** 2 plans

Plans:
- [x] 33-01-PLAN.md — DeviceOptions CommunityString rename + all consumers + config files (CS-01, CS-02, CS-05)
- [x] 33-02-PLAN.md — CommandSlotOptions + tenant model additions + IOidMapService removal (TEN-01, TEN-02, TEN-09, TEN-10, TEN-12)

---

#### Phase 34: CommunityString Validation & MetricPollJob Cleanup

**Goal**: Every CommunityString value in every config layer — devices, tenant metrics, tenant commands — is validated at load time against the `Simetra.*` pattern using `CommunityStringHelper.IsValidCommunityString()` as the single authoritative check. Invalid entries are skipped with structured Error logs. Duplicate device names are caught before silently overwriting the registry. Empty poll groups and the MetricPollJob CommunityString fallback are removed.
**Depends on**: Phase 33 (CommunityString fields must exist on all options types before validation can reference them)
**Requirements**: CS-03, CS-04, CS-06, CS-07, DEV-08, DEV-09, DEV-10, TEN-03, TEN-05, TEN-07, TEN-11, TEN-13
**Success Criteria** (what must be TRUE):
  1. DeviceRegistry skips duplicate IP+Port (Error log) and warns on duplicate CommunityString (Warning, both load)
  2. Zero-OID poll groups filtered from job registration (DEV-08)
  3. Tenant metric entries validated: structural (Ip, port, MetricName), Role (Evaluate/Resolved), MetricName in OidMap, IP+Port in DeviceRegistry — invalid = skip entry
  4. Tenant command entries validated: structural (Ip, port, CommandName), ValueType (Integer32/IpAddress/OctetString), non-empty Value, IP+Port in DeviceRegistry — invalid = skip entry
  5. TEN-13 post-validation gate: tenant requires ≥1 Resolved + ≥1 Evaluate metric + ≥1 command after validation
  6. Operator config ordering documented in ServiceCollectionExtensions.cs (CS-07)

**Plans:** 2 plans

Plans:
- [x] 34-01-PLAN.md — DeviceRegistry validation: dup IP+Port skip, CommunityString Warning, zero-OID filter, CS-07 doc (CS-03, CS-04, CS-07, DEV-08, DEV-10)
- [x] 34-02-PLAN.md — TenantVectorRegistry validation: per-entry skip, Role/ValueType/MetricName, TEN-13 gate (TEN-03, TEN-05, TEN-07, TEN-11, TEN-13)

---

#### Phase 35: TenantVectorRegistry Refactor & Validator Activation

**Goal**: `TenantVectorRegistry` is fully self-contained — it reads Device, Ip, CommunityString, and IntervalSeconds from config entries directly without calling `IDeviceRegistry` or `IOidMapService`, builds the Commands list from `CommandSlotOptions`, and the `TenantVectorOptionsValidator` enforces structural correctness on load. Unresolvable MetricNames are skipped with Error logs. CommandName lookup is deferred to execution time.
**Depends on**: Phase 33 (model fields must exist), Phase 34 (CommunityString validation pattern established)
**Requirements**: TEN-04, TEN-06, TEN-08, CLN-01, CLN-02
**Success Criteria** (what must be TRUE):
  1. All four watchers follow "watcher validates, registry stores" — DeviceRegistry and TenantVectorRegistry are pure data stores
  2. DeviceRegistry constructor takes only `ILogger` — no IOidMapService, no IOptions, no DNS
  3. TenantVectorRegistry constructor takes only `ILogger` — no IDeviceRegistry, no IOidMapService
  4. DeviceWatcherService.ValidateAndBuildDevicesAsync handles all device validation + DNS + OID resolution
  5. TenantVectorWatcherService.ValidateAndBuildTenants handles structural, Role, MetricName, IP+Port, TEN-13 gate
  6. TEN-06: CommandName stored as-is with Debug log — no command map lookup at load time
  7. ResolveIp() and DeriveIntervalSeconds() deleted from TenantVectorRegistry
  8. Both validators simplified to no-op (return Success)

**Plans:** 2 plans

Plans:
- [x] 35-01-PLAN.md — DeviceWatcher validates, DeviceRegistry pure store (9 files, 12 new watcher tests)
- [x] 35-02-PLAN.md — TenantVectorWatcher validates, TenantVectorRegistry pure store (6 files, 19 new watcher tests)

---

#### Phase 36: Config File Renames

**Goal**: All config file names, ConfigMap names, config keys, C# constants, K8s manifests, local dev files, and E2E test scripts are updated atomically to the new naming convention — `tenants.json` / `simetra-tenants`, `oid_metric_map.json` / `simetra-oid-metric-map`, `oid_command_map.json` / `simetra-oid-command-map`. No artifact retains the old name after this phase.
**Depends on**: Phase 35 (all logic changes complete before any mechanical rename introduces diff noise)
**Requirements**: REN-01, REN-02, REN-03, REN-04
**Success Criteria** (what must be TRUE):
  1. The local dev config directory contains `tenants.json`, `oid_metric_map.json`, and `oid_command_map.json` — the old filenames `tenantvector.json`, `oidmaps.json`, and `commandmaps.json` no longer exist anywhere in the repository
  2. K8s ConfigMap manifests (standalone, production) reference `simetra-tenants`, `simetra-oid-metric-map`, and `simetra-oid-command-map` as ConfigMap names and data keys — applying them to a cluster succeeds without naming conflicts
  3. The C# constants `ConfigMapName` and `ConfigKey` in `TenantVectorWatcherService`, `OidMapWatcherService`, and `CommandMapWatcherService` match the new names exactly — a pod reload after ConfigMap apply picks up new config data without any "skipping reload" log
  4. E2E test scripts and inline heredoc fixtures reference only the new file and ConfigMap names — running the full E2E suite against a cluster with the renamed ConfigMaps produces the same pass results as before the rename

**Plans:** TBD

---

## Progress

**Execution Order:** 33 → 34 → 35 → 36

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
| 34. CommunityString Validation & MetricPollJob Cleanup | v1.7 | 2/2 | Complete | 2026-03-14 |
| 35. TenantVectorRegistry Refactor & Validator Activation | v1.7 | 2/2 | Complete | 2026-03-15 |
| 36. Config File Renames | v1.7 | 0/TBD | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-14 after v1.7 roadmap — Phases 33-36 defined*
