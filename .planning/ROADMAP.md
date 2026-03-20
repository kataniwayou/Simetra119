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
- ✅ **v2.0 Tenant Evaluation & Control** - Phases 45-50 (shipped 2026-03-17)
- ✅ **v2.1 E2E Tenant Evaluation Tests** - Phases 51-61 (shipped 2026-03-20)
- 🚧 **v2.2 Progressive E2E Snapshot Suite** - Phases 62-64 (in progress)

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

See `.planning/milestones/v1.6-ROADMAP.md` for details.

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

<details>
<summary>✅ v2.0 Tenant Evaluation & Control (Phases 45-50) - SHIPPED 2026-03-17</summary>

See `.planning/milestones/v2.0-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v2.1 E2E Tenant Evaluation Tests (Phases 51-61) - SHIPPED 2026-03-20</summary>

See `.planning/milestones/v2.1-ROADMAP.md` for details.

</details>

---

### 🚧 v2.2 Progressive E2E Snapshot Suite (In Progress)

**Milestone Goal:** Progressive 3-stage E2E test suite that validates every SnapshotJob evaluation state with a single tenant, proves two-tenant independence, and exercises all advance gate combinations with four tenants -- each stage gated on the previous passing.

#### Phase 62: Single Tenant Evaluation States

**Goal**: Every SnapshotJob evaluation outcome (Not Ready, Stale, Resolved, Unresolved, Healthy, Suppressed) is observable and verified through a single-tenant fixture with one priority group
**Depends on**: Phase 61 (v2.1 E2E infrastructure: simulator HTTP endpoints, sim.sh helpers, OID map)
**Requirements**: PSS-01, PSS-02, PSS-06, PSS-07, PSS-08, PSS-09, PSS-10, PSS-INF-02, PSS-INF-03 (PSS-03/04/05 deferred per research)
**Success Criteria** (what must be TRUE):
  1. A scenario script applies a 1-tenant configmap fixture, waits less than the grace window (6s = TimeSeriesSize(3) x IntervalSeconds(1) x GraceMultiplier(2)), and the snapshot log shows "Not Ready" for that tenant -- confirming the readiness gate fires before enough samples accumulate
  2. After priming a tenant to healthy, calling sim_set_oid_stale on poll-sourced OIDs causes the snapshot log to show tier=1 Stale followed by tier=4 Unresolved with command dispatch
  3. Setting all resolved-role metrics out of range produces a tier=2 Resolved log with zero command dispatches, while setting only some resolved-role metrics out of range causes evaluation to continue past tier=2 to tier=3
  4. Setting all evaluate-role metrics out of range produces tier=4 Unresolved with commands dispatched (snmp_command_sent_total increments), while setting all metrics in-range produces tier=3 Healthy with no commands dispatched
  5. Triggering tier=4 Unresolved twice within the suppression window shows snmp_command_suppressed_total incrementing on the second cycle -- commands are suppressed, not re-dispatched

**Plans**: 2 plans

Plans:
- [x] 62-01-PLAN.md -- Fixture, report category, scenarios 53-55 (Not Ready, Stale, Resolved)
- [x] 62-02-PLAN.md -- Scenarios 56-58 (Unresolved, Healthy, Suppression)

---

#### Phase 63: Two Tenant Independence

**Goal**: Two tenants in the same priority group evaluate independently -- one tenant's state does not affect the other's evaluation result or command dispatch
**Depends on**: Phase 62 (1-tenant fixture and scenario patterns established)
**Requirements**: PSS-11, PSS-12, PSS-13, PSS-INF-01
**Success Criteria** (what must be TRUE):
  1. With a 2-tenant fixture, setting T1 metrics to healthy and T2 metrics to evaluate-violated produces T1=Healthy and T2=Unresolved in snapshot logs within the same evaluation cycle -- T1 has no commands dispatched while T2 does
  2. Setting T1 to resolved-violated and T2 to healthy produces T1=Resolved (tier=2) and T2=Healthy (tier=3) in snapshot logs -- each tenant's tier is determined solely by its own metric values
  3. Setting both tenants to evaluate-violated produces both T1=Unresolved and T2=Unresolved with independent command dispatch (snmp_command_sent_total increments for both tenant device targets)
  4. The Stage 2 runner script checks FAIL_COUNT from Stage 1 and exits without running any Stage 2 scenarios if Stage 1 had failures

**Plans**: TBD

---

#### Phase 64: Advance Gate Logic

**Goal**: All seven advance gate combinations (3 pass, 4 block) are verified with a 4-tenant 2-group fixture -- gate-pass means G2 is evaluated, gate-block means G2 is never evaluated
**Depends on**: Phase 63 (2-tenant independence proven, stage gating infrastructure)
**Requirements**: PSS-14, PSS-15, PSS-16, PSS-17, PSS-18, PSS-19, PSS-20
**Success Criteria** (what must be TRUE):
  1. When all G1 tenants are Resolved (tier=2) or all are Healthy (tier=3), the advance gate passes and G2 tenants appear in snapshot evaluation logs -- confirming gate-pass for uniform non-Unresolved G1 states
  2. When G1 has a mix of Resolved and Healthy tenants (no Unresolved), the advance gate still passes and G2 is evaluated -- confirming mixed non-Unresolved states do not block
  3. When any G1 tenant is Unresolved (tier=4) -- whether all G1 are Unresolved, or mixed with Resolved, or mixed with Healthy -- the advance gate blocks and G2 tenants do NOT appear in snapshot evaluation logs (verified by absence of G2 tenant tier logs within a 10-15s observation window)
  4. When all G1 tenants are Not Ready (before grace window ends), the advance gate blocks and G2 tenants are not evaluated -- confirming Not Ready is treated as a blocking state
  5. The Stage 3 runner script checks FAIL_COUNT from Stage 2 and exits without running any Stage 3 scenarios if Stage 2 had failures

**Plans**: TBD

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
| 48. SnapshotJob 4-Tier Evaluation | v2.0 | 4/4 | Complete | 2026-03-16 |
| 49. Observability & Dashboard | v2.0 | 1/1 | Complete | 2026-03-16 |
| 50. Label Rename | v2.0 | 1/1 | Complete | 2026-03-16 |
| 51. Simulator HTTP Control Endpoint | v2.1 | 2/2 | Complete | 2026-03-17 |
| 52. Test Library and Config Artifacts | v2.1 | 3/3 | Complete | 2026-03-17 |
| 53. Single-Tenant Scenarios | v2.1 | 3/3 | Complete | 2026-03-17 |
| 54. Multi-Tenant Scenarios | v2.1 | 2/2 | Complete | 2026-03-17 |
| 55. Advanced Scenarios | v2.1 | 2/2 | Complete | 2026-03-17 |
| 56. Tenant Validation Hardening | v2.1 | 2/2 | Complete | 2026-03-18 |
| 57. Deterministic Watcher Startup Order | v2.1 | 2/2 | Complete | 2026-03-18 |
| 58. SnapshotJob Tier Simulation Tests | v2.1 | 3/3 | Complete | 2026-03-18 |
| 59. Advance Gate Fix & Starvation Sim | v2.1 | 2/2 | Complete | 2026-03-19 |
| 60. Readiness Window for Holders | v2.1 | 2/2 | Complete | 2026-03-19 |
| 61. New E2E Suite Snapshot | v2.1 | 3/3 | Complete | 2026-03-19 |
| 62. Single Tenant Evaluation States | v2.2 | 2/2 | Complete | 2026-03-20 |
| 63. Two Tenant Independence | v2.2 | 0/TBD | Not started | - |
| 64. Advance Gate Logic | v2.2 | 0/TBD | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-20 after Phase 62 complete*
