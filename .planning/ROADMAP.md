# Roadmap: SNMP Monitoring System

## Milestones

- ✅ **v1.0 Foundation** - Phases 1-10 (shipped 2026-03-07)
- ✅ **v1.1 Device Simulation** - Phases 11-14 (shipped 2026-03-08)
- ✅ **v1.2 Operational Enhancements** - Phases 15-16 (shipped 2026-03-08)
- ✅ **v1.3 Grafana Dashboards** - Phases 18-19 (shipped 2026-03-09)
- ✅ **v1.4 E2E System Verification** - Phases 20-24 (shipped 2026-03-09)
- 🚧 **v1.5 Priority Vector Data Layer** - Phases 25-29 (in progress)

## Phases

<details>
<summary>✅ v1.0 through v1.4 (Phases 1-24) - SHIPPED</summary>

See `.planning/MILESTONES.md` and `.planning/milestones/` for archived details.

</details>

### 🚧 v1.5 Priority Vector Data Layer (In Progress)

**Milestone Goal:** Stateful in-memory data layer that organizes SNMP metrics into prioritized tenants with independent value cells and fan-out routing from the existing pipeline.

#### Phase 25: Config Models and Validation

**Goal**: Operator can define tenants with prioritized metric slots in a validated JSON configuration
**Depends on**: Nothing (foundation for v1.5)
**Requirements**: CFG-01, CFG-02
**Success Criteria** (what must be TRUE):
  1. tenantvector.json deserializes into a typed POCO hierarchy (TenantVectorOptions with tenants, priorities, and metric definitions)
  2. Validation rejects duplicate tenant IDs, invalid IP/port ranges, metric_names not found in the OID map, and duplicate (ip, port, metric_name) within a tenant
  3. Validation passes for a well-formed config with multiple tenants containing overlapping metrics across tenants
  4. Unit tests cover all validation rules with both positive and negative cases
**Plans**: 1 plan

Plans:
- [x] 25-01: Config POCOs and IValidateOptions validator with unit tests

#### Phase 26: Core Data Types and Registry

**Goal**: Tenant metric slots exist in memory as an ordered priority structure with a lock-free routing index
**Depends on**: Phase 25
**Requirements**: DAT-01, DAT-02, DAT-03, DAT-04
**Success Criteria** (what must be TRUE):
  1. TenantVectorRegistry holds tenants grouped by priority order, each tenant containing its configured metric slots
  2. MetricSlot stores value (double + optional string) and updated_at as an immutable record swapped atomically via Volatile.Write -- no torn reads
  3. Routing index is a FrozenDictionary keyed by (ip, port, metric_name) returning the list of (tenant_id, slot reference) targets
  4. Calling Reload() with new config atomically rebuilds the entire registry and routing index via volatile swap -- concurrent readers see either old or new state, never partial
  5. Unit tests verify slot atomicity, routing lookups, priority ordering, and rebuild correctness
**Plans**: 2 plans

Plans:
- [x] 26-01: Core data types: MetricSlot, MetricSlotHolder, RoutingKey, Tenant, PriorityGroup with unit tests
- [x] 26-02: TenantVectorRegistry with FrozenDictionary routing index, atomic rebuild, DI registration, and unit tests

#### Phase 27: Pipeline Integration

**Goal**: Every resolved SNMP sample that matches a tenant metric route is written to the correct slot(s) without disrupting existing OTel export
**Depends on**: Phase 26
**Requirements**: PIP-01, PIP-02, PIP-03, PIP-04, OBS-02
**Success Criteria** (what must be TRUE):
  1. TenantVectorFanOutBehavior runs after OidResolution in the MediatR chain -- it looks up the routing index by (ip, port, metric_name) and writes values to all matching tenant slots
  2. Port is resolved via DeviceRegistry.TryGetDeviceByName(DeviceName) -- no changes to the SnmpOidReceived message contract
  3. Heartbeat samples (IsHeartbeat) and unresolved OIDs (MetricName == "Unknown") are skipped and never routed to tenant slots
  4. If the fan-out behavior throws any exception, it catches internally and always calls next() -- OtelMetricHandler fires regardless
  5. Pipeline counter snmp_tenantvector_routed_total increments for each successful slot write
**Plans**: 2 plans

Plans:
- [ ] 27-01-PLAN.md -- Heartbeat normalization, ValueExtractionBehavior, OtelMetricHandler refactor to pre-extracted values
- [ ] 27-02-PLAN.md -- MetricSlot TypeCode, TenantVectorFanOutBehavior, pipeline counter, DI registration

#### Phase 28: ConfigMap Watcher and Local Dev

**Goal**: Tenant vector configuration hot-reloads from a K8s ConfigMap in production and from a local file in development
**Depends on**: Phase 27
**Requirements**: CFG-03, OBS-01
**Success Criteria** (what must be TRUE):
  1. TenantVectorWatcherService watches the simetra-tenantvector ConfigMap via K8s API and triggers registry rebuild on change
  2. In local-dev mode (no K8s), the service loads tenantvector.json from the file system
  3. On reload, structured diff logging reports tenants added, removed, and changed (metric count delta)
  4. OID map changes trigger a routing index rebuild so metric_name renames do not cause silent routing misses
**Plans**: TBD

Plans:
- [ ] 28-01: TenantVectorWatcherService (K8s watch + local dev fallback)
- [ ] 28-02: Diff logging and OID map change subscription

#### Phase 29: K8s Deployment and E2E Validation

**Goal**: Tenant vector is deployed to the K8s cluster and verified end-to-end with real SNMP data flowing through the fan-out pipeline
**Depends on**: Phase 28
**Requirements**: DEP-01, DEP-02
**Success Criteria** (what must be TRUE):
  1. simetra-tenantvector ConfigMap manifest exists and is applied to the cluster
  2. Deployment.yaml mounts the ConfigMap so TenantVectorWatcherService can read it
  3. After deployment, snmp_tenantvector_routed_total counter is incrementing in Prometheus (proving samples are routing to tenant slots)
  4. ConfigMap update triggers watcher reload and diff log entries appear in pod logs
**Plans**: TBD

Plans:
- [ ] 29-01: ConfigMap manifest and deployment.yaml updates
- [ ] 29-02: E2E validation of fan-out routing and hot-reload

## Progress

**Execution Order:** 25 → 26 → 27 → 28 → 29

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 25. Config Models | v1.5 | 1/1 | Complete | 2026-03-10 |
| 26. Core Data Types | v1.5 | 2/2 | Complete | 2026-03-10 |
| 27. Pipeline Integration | v1.5 | 0/2 | Not started | - |
| 28. ConfigMap Watcher | v1.5 | 0/2 | Not started | - |
| 29. K8s Deployment | v1.5 | 0/2 | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-10 after Phase 27 planning*
