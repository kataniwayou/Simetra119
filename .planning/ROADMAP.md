# Roadmap: SNMP Monitoring System

## Milestones

- v1.0 Foundation - Phases 1-10 (shipped 2026-03-07)
- v1.1 Device Simulation - Phases 11-14 (shipped 2026-03-08)
- v1.2 Operational Enhancements - Phases 15+ (shipped 2026-03-07)

## Phases

<details>
<summary>v1.0 Foundation (Phases 1-10) - SHIPPED 2026-03-07</summary>

10 phases, 48 plans. Full MediatR pipeline, trap listener, poll scheduler, leader election, graceful shutdown, health probes, heartbeat loopback. See `.planning/milestones/` for archived details.

</details>

<details>
<summary>v1.1 Device Simulation (Phases 11-14) - SHIPPED 2026-03-08</summary>

4 phases, 10 plans. OBP + NPB OID maps (92 entries), SNMP simulators with traps, K8s E2E integration with devices.json. See `.planning/milestones/v1.1-ROADMAP.md` for archived details.

</details>

### v1.2 Operational Enhancements (Complete)

**Milestone Goal:** Consolidate configuration into a single documented ConfigMap, replace file-based hot-reload with K8s API watch, and enable dynamic device/poll schedule reloading without pod restart.

- [x] **Phase 15: K8s ConfigMap Watch and Unified Config** - Single ConfigMap with documented JSONC, K8s API watch for live reload of OID maps and device/poll config

## Phase Details (v1.2)

### Phase 15: K8s ConfigMap Watch and Unified Config
**Goal**: All device configuration (devices, poll schedules) lives in a single documented ConfigMap key, loaded via K8s API watch with full live reload -- no pod restart needed for any config change
**Depends on**: Phase 14 (v1.1 complete)
**Requirements**: OPS-01, CFG-01, CFG-02, CFG-03
**Success Criteria** (what must be TRUE):
  1. Single ConfigMap key contains all device entries with JSONC documentation comments
  2. Separate oidmap-*.json and devices.json files are removed -- single source of truth
  3. K8s API watch detects ConfigMap changes and reloads device config + poll definitions at runtime
  4. Adding/removing devices or changing poll OIDs/intervals takes effect without pod restart (Quartz jobs re-registered)
  5. RBAC updated with configmaps read/watch permission
  6. Local development fallback works without K8s (file-based loading when not in cluster)
**Plans**: 5 plans
Plans:
- [x] 15-01-PLAN.md -- Unified config model (SimetraConfigModel), mutable OidMapService + DeviceRegistry, registry cleanup methods, updated tests
- [x] 15-02-PLAN.md -- ConfigMapWatcherService (K8s API watch with reconnect) and DynamicPollScheduler (Quartz job reconciliation)
- [x] 15-03-PLAN.md -- DI wiring (ServiceCollectionExtensions + Program.cs), local dev config file, cleanup of legacy file scanning
- [x] 15-04-PLAN.md -- K8s RBAC and ConfigMap manifest updates, unified simetra-config.json key, delete legacy oidmap files
- [x] 15-05-PLAN.md -- Unit tests for DynamicPollScheduler reconciliation (add/remove/reschedule scenarios)

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1-10 | v1.0 | 48/48 | Complete | 2026-03-07 |
| 11-14 | v1.1 | 10/10 | Complete | 2026-03-08 |
| 15. K8s ConfigMap Watch and Unified Config | v1.2 | 5/5 | Complete | 2026-03-07 |
