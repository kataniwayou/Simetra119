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

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-15 after v1.9 milestone phases added*
