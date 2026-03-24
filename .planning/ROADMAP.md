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
- ✅ **v2.2 Progressive E2E Snapshot Suite** - Phases 62-65 (shipped 2026-03-22)
- ✅ **v2.3 Metric Validity & Correctness** - Phases 66-71 (shipped 2026-03-22)
- ✅ **v2.4 Tenant Vector Metrics** - Phases 72-75 (shipped 2026-03-23)
- ✅ **v2.5 Tenant Metrics Approach Modification** - Phases 76-81 (shipped 2026-03-23)
- 🚧 **v2.6 E2E Manual Tenant Simulation Suite** - Phases 82-83 (in progress)

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

<details>
<summary>✅ v2.2 Progressive E2E Snapshot Suite (Phases 62-65) - SHIPPED 2026-03-22</summary>

See `.planning/milestones/v2.2-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v2.3 Metric Validity & Correctness (Phases 66-71) - SHIPPED 2026-03-22</summary>

See `.planning/milestones/v2.3-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v2.4 Tenant Vector Metrics (Phases 72-75) - SHIPPED 2026-03-23</summary>

See `.planning/milestones/v2.4-ROADMAP.md` for details.

</details>

---

<details>
<summary>✅ v2.5 Tenant Metrics Approach Modification (Phases 76-81) - SHIPPED 2026-03-23</summary>

See `.planning/milestones/v2.5-ROADMAP.md` for details.

</details>

---

### v2.6 E2E Manual Tenant Simulation Suite (In Progress)

**Milestone Goal:** An interactive command interpreter that lets a human operator drive any tenant into any violation state on demand — using a terse CLI pattern syntax — so tenant state transitions can be verified by watching Grafana without managing 17 script files.

#### Phase 82: Fixture & OID Mapping

**Goal**: A 4-tenant environment with collision-free OIDs is live in the cluster, all tenants start Healthy, and a hardcoded mapping file defines every OID suffix and value needed to violate or restore any tenant role.
**Depends on**: Phase 81 (v2.5 complete)
**Requirements**: FIX-01, FIX-02, FIX-03, FIX-04, MAP-01, MAP-02
**Success Criteria** (what must be TRUE):
  1. Applying the fixture ConfigMap produces 4 tenants (T1_P1, T2_P1, T1_P2, T2_P2) visible in Grafana with distinct OID suffixes and no collision between any two tenants
  2. After the grace window passes with no violations set, all 4 tenants show Healthy state in Grafana
  3. The OID metric map contains entries for every OID suffix used by all 4 tenants
  4. The hardcoded mapping file lists per-tenant per-role OID suffixes with healthy and violated values, and adding a new tenant or metric requires adding a single line
**Plans**: 2 plans

Plans:
- [x] 82-01-PLAN.md — Register 24 v2.6 OIDs in simulator, OID metric map, and device poll config
- [x] 82-02-PLAN.md — Create 4-tenant fixture ConfigMap and OID mapping file

#### Phase 83: Command Interpreter

**Goal**: A command interpreter accepts `{Tenant}-{V/S}-{#}E-{#}R` patterns from the Claude Code CLI, validates them against the mapping, translates them to simulator HTTP API calls, and produces clear errors for invalid input.
**Depends on**: Phase 82
**Requirements**: CMD-01, CMD-02, CMD-03, CMD-04, CMD-05, CMD-06, CMD-07, CMD-08
**Success Criteria** (what must be TRUE):
  1. Running a valid pattern (e.g. `T1_P1-V-2E-1R`) against the interpreter causes the simulator to receive the correct OID value HTTP calls and Grafana reflects the expected violation state
  2. Running a stale-mode pattern (e.g. `T1_P1-S-1E-0R`) causes the interpreter to call sim_set_oid_stale for the specified metrics rather than setting a violated value
  3. Non-violated metrics in the pattern are set to their healthy value, not left at whatever state they were in
  4. An unknown tenant name produces an error listing all valid tenant names
  5. A count exceeding available metrics for that tenant/role produces an error identifying the limit
  6. A malformed pattern produces an error showing the expected format
**Plans**: 1 plan

Plans:
- [ ] 83-01-PLAN.md — Create standalone sim_command.sh command interpreter

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
| 63. Two Tenant Independence | v2.2 | 2/2 | Complete | 2026-03-20 |
| 64. Advance Gate Logic | v2.2 | 3/3 | Complete | 2026-03-20 |
| 65. E2E Runner Fixes & Flaky Stabilization | v2.2 | 1/1 | Complete | 2026-03-22 |
| 66. Pipeline Event Counters | v2.3 | 3/3 | Complete | 2026-03-22 |
| 67. Poll & Trap Infrastructure Counters | v2.3 | 2/2 | Complete | 2026-03-22 |
| 68. Command Counters | v2.3 | 4/4 | Complete | 2026-03-22 |
| 69. Business Metric Value Correctness | v2.3 | 2/2 | Complete | 2026-03-22 |
| 70. Label Correctness | v2.3 | 2/2 | Complete | 2026-03-22 |
| 71. Negative Proofs | v2.3 | 1/1 | Complete | 2026-03-22 |
| 72. TenantMetricService & Meter Registration | v2.4 | 2/2 | Complete | 2026-03-23 |
| 73. SnapshotJob Instrumentation | v2.4 | 2/2 | Complete | 2026-03-23 |
| 74. Grafana Dashboard Panel | v2.4 | 1/1 | Complete | 2026-03-23 |
| 75. E2E Validation Scenarios | v2.4 | 3/3 | Complete | 2026-03-23 |
| 76. Percentage Gauge Instruments | v2.5 | 1/1 | Complete | 2026-03-23 |
| 77. Gather-Then-Decide Evaluation Flow | v2.5 | 2/2 | Complete | 2026-03-23 |
| 78. Counter Reference Cleanup | v2.5 | 1/1 | Complete | 2026-03-23 |
| 79. Dashboard Percentage Update | v2.5 | 1/1 | Complete | 2026-03-23 |
| 80. E2E Scenario Updates | v2.5 | 2/2 | Complete | 2026-03-23 |
| 81. E2E Partial Percentage Scenario | v2.5 | 1/1 | Complete | 2026-03-23 |
| 82. Fixture & OID Mapping | v2.6 | 2/2 | Complete | 2026-03-24 |
| 83. Command Interpreter | v2.6 | 0/1 | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-24 — Phase 83 planned (1 plan, 1 wave)*
