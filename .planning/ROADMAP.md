# Roadmap: SNMP Monitoring System

## Milestones

- v1.0 Foundation - Phases 1-10 (shipped 2026-03-07)
- v1.1 Device Simulation - Phases 11-14 (shipped 2026-03-07)
- v1.2 Operational Enhancements - Phases 15+ (not started)

## Phases

<details>
<summary>v1.0 Foundation (Phases 1-10) - SHIPPED 2026-03-07</summary>

10 phases, 48 plans. Full MediatR pipeline, trap listener, poll scheduler, leader election, graceful shutdown, health probes, heartbeat loopback. See `.planning/milestones/` for archived details.

</details>

### v1.1 Device Simulation (Complete)

**Milestone Goal:** Populate OID maps for OBP and NPB devices with documentation, refine simulators to match realistic device profiles, and deploy integrated K8s simulation environment for end-to-end verification.

- [x] **Phase 11: OID Map Design and OBP Population** - Establish naming convention, decide map structure, populate OBP OIDs with docs
- [x] **Phase 12: NPB OID Population** - Populate NPB OIDs with documentation using established conventions
- [x] **Phase 13: Simulator Refinement** - Update both simulators for realistic OID subsets and trap behavior
- [x] **Phase 14: K8s Integration and E2E** - Deploy simulator pods, update ConfigMap, verify end-to-end pipeline

## Phase Details

### Phase 11: OID Map Design and OBP Population
**Goal**: OID map structure is decided, naming convention is established, OBP device OIDs are populated with full documentation
**Depends on**: Phase 10 (v1.0 complete)
**Requirements**: OIDM-01, OIDM-02, OIDM-03, DOC-01
**Success Criteria** (what must be TRUE):
  1. OID map entries follow device-type prefix + metric + index suffix naming (e.g., `obp_link_state_L1`, `obp_r1_power_L3`) and this convention is documented
  2. Separate `oidmap-*.json` files per device type, auto-scanned at startup, merged into single runtime dictionary
  3. K8s deployment uses directory mount (no subPath) for OID map hot-reload via ConfigMap updates
  4. OBP OID map contains entries for 4 links covering state, channel, and optical power (R1-R4) with realistic OID strings
  5. Each OBP OID has documentation specifying value meaning, units, and expected ranges
**Plans**: 3 plans (complete)

### Phase 12: NPB OID Population
**Goal**: NPB device OIDs are populated with full documentation, following the naming convention and structure established in Phase 11
**Depends on**: Phase 11
**Requirements**: OIDM-04, DOC-02
**Success Criteria** (what must be TRUE):
  1. NPB OID map contains entries for 8 ports covering system health, per-port traffic counters, and port status with realistic OID strings
  2. Each NPB OID has documentation specifying value meaning, units, and expected ranges
  3. NPB entries follow the same naming convention as OBP (e.g., `npb_port_rx_octets_P1`, `npb_port_status_P8`)
**Plans**: 1 plan
Plans:
- [ ] 12-01-PLAN.md -- Create oidmap-npb.json with 68 documented entries and update K8s ConfigMaps

### Phase 13: Simulator Refinement
**Goal**: Both OBP and NPB simulators respond with realistic OID subsets matching the populated OID maps and send appropriate trap types
**Depends on**: Phase 11, Phase 12
**Requirements**: SIM-01, SIM-02, SIM-03, SIM-04, SIM-05
**Success Criteria** (what must be TRUE):
  1. OBP simulator responds to SNMP GET for all 4-link OIDs defined in the OID map with realistic values
  2. OBP simulator sends StateChange traps for all 4 links with correct OID bindings
  3. NPB simulator responds to SNMP GET for all 8-port OIDs defined in the OID map with realistic values (system health + per-port traffic)
  4. NPB simulator sends link up/down traps with correct OID bindings
  5. Both simulators authenticate using `Simetra.{DeviceName}` community string convention (e.g., `Simetra.OBP-01`, `Simetra.NPB-01`)
**Plans**: 3 plans
Plans:
- [x] 13-01-PLAN.md -- OBP simulator rewrite: 24 OIDs, power random walk, StateChange traps, Simetra.OBP-01 community
- [x] 13-02-PLAN.md -- NPB simulator rewrite: 68 OIDs, traffic profiles, system health, portLinkChange traps, Simetra.NPB-01 community
- [x] 13-03-PLAN.md -- K8s deployment YAML updates: health probes, env vars, configmap-devices OID references

### Phase 14: K8s Integration and E2E
**Goal**: Simulator pods are deployed in K8s, snmp-collector ConfigMap has correct MetricPoll groups for both device types, and the full pipeline works end-to-end
**Depends on**: Phase 13
**Requirements**: SIM-06, POLL-01, POLL-02
**Success Criteria** (what must be TRUE):
  1. OBP and NPB simulator pods run in the K8s cluster and are reachable by snmp-collector
  2. snmp-collector ConfigMap contains MetricPoll groups for OBP that match the OBP OID map entries
  3. snmp-collector ConfigMap contains MetricPoll groups for NPB that match the NPB OID map entries
  4. Polled OBP and NPB metrics appear in Prometheus with correct metric names and labels
  5. Traps from both simulators flow through the pipeline and produce metrics in Prometheus
**Plans**: 3 plans
Plans:
- [x] 14-01-PLAN.md -- DNS resolution + CommunityString + devices.json loading in C# code
- [x] 14-02-PLAN.md -- ConfigMap devices.json with all 92 OIDs for OBP-01 and NPB-01
- [x] 14-03-PLAN.md -- E2E verification script querying Prometheus for poll and trap metrics

### v1.2 Operational Enhancements (Not Started)

**Milestone Goal:** Consolidate configuration into a single documented ConfigMap, replace file-based hot-reload with K8s API watch, and enable dynamic device/poll schedule reloading without pod restart.

- [ ] **Phase 15: K8s ConfigMap Watch and Unified Config** - Single ConfigMap with documented JSONC, K8s API watch for live reload of OID maps and device/poll config

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
- [ ] 15-01-PLAN.md -- Unified config model (SimetraConfigModel), mutable OidMapService + DeviceRegistry, registry cleanup methods, updated tests
- [ ] 15-02-PLAN.md -- ConfigMapWatcherService (K8s API watch with reconnect) and DynamicPollScheduler (Quartz job reconciliation)
- [ ] 15-03-PLAN.md -- DI wiring (ServiceCollectionExtensions + Program.cs), local dev config file, cleanup of legacy file scanning
- [ ] 15-04-PLAN.md -- K8s RBAC and ConfigMap manifest updates, unified simetra-config.json key, delete legacy oidmap files
- [ ] 15-05-PLAN.md -- Unit tests for DynamicPollScheduler reconciliation (add/remove/reschedule scenarios)

## Progress

**Execution Order:** 11 -> 12 -> 13 -> 14

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1-10 | v1.0 | 48/48 | Complete | 2026-03-07 |
| 11. OID Map Design and OBP Population | v1.1 | 3/3 | Complete | 2026-03-07 |
| 12. NPB OID Population | v1.1 | 1/1 | Complete | 2026-03-07 |
| 13. Simulator Refinement | v1.1 | 3/3 | Complete | 2026-03-07 |
| 14. K8s Integration and E2E | v1.1 | 3/3 | Complete | 2026-03-07 |
| 15. K8s ConfigMap Watch and Unified Config | v1.2 | 0/5 | Not started | - |
