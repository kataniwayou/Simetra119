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
- 🚧 **v2.6 E2E Manual Tenant Simulation Suite** - Phases 82-84 (in progress)

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

### 🚧 v2.6 E2E Manual Tenant Simulation Suite (In Progress)

**Milestone Goal:** A 17-script manual simulation suite that walks a human operator through every tenant state transition — P1 violations, P2 advance-gate blocking, and full cycle resolution — verified by watching Grafana, not automated assertions.

#### Phase 82: Fixture & Infrastructure

**Goal**: A 4-tenant test environment exists with collision-free OIDs, and a script runner with user approval between each step is ready to execute the suite.
**Depends on**: Phase 81 (v2.5 complete)
**Requirements**: FIX-01, FIX-02, FIX-03, FIX-04, RUN-01, RUN-02, RUN-03, RPT-01
**Success Criteria** (what must be TRUE):
  1. Applying the fixture ConfigMap produces 4 tenants (T1_P1, T2_P1, T1_P2, T2_P2) visible in Grafana with distinct OID suffixes and no collision
  2. After the grace window passes with no violations set, all 4 tenants show Healthy state in Grafana
  3. Running the script runner prompts for user approval before each numbered script and proceeds or aborts based on the response
  4. Scripts 114-130 appear as a named category in report.sh output alongside existing categories
**Plans**: TBD

Plans:
- [ ] 82-01: TBD

#### Phase 83: P1 Tenant Scripts (01-12)

**Goal**: Scripts 01-12 exist and correctly drive T2_P1 and T1_P1 through their full violation and resolution cycles, and prove the advance gate blocks P2 dispatch while any P1 tenant is Unresolved.
**Depends on**: Phase 82
**Requirements**: P1S-01, P1S-02, P1S-03, P2S-01, AGT-01
**Success Criteria** (what must be TRUE):
  1. Script 01 restarts all relevant pods; after running it, Grafana shows all tenants transitioning through NotReady then back to Healthy
  2. Scripts 02-04 show T2_P1 evaluate percentage rising in Grafana (25% → 75% → 100% Unresolved) with state visible in the tenant table
  3. Scripts 05-06 show T1_P2 violating while T2_P1 is Unresolved; Grafana confirms no dispatch metric increments for T1_P2 (advance gate blocks)
  4. Scripts 07-09 show T2_P1 resolved percentage rising until state flips to Resolved in Grafana
  5. Scripts 10-12 show T1_P1 cycling from partial violation to Unresolved and back to Resolved, visible in Grafana
**Plans**: TBD

Plans:
- [ ] 83-01: TBD

#### Phase 84: P2 Tenant Scripts (13-17)

**Goal**: Scripts 13-17 exist and drive T2_P2 through a full multi-state cycle, proving P2 tenants resume evaluation and dispatch once all P1 tenants are Resolved.
**Depends on**: Phase 83
**Requirements**: P2S-02, AGT-02
**Success Criteria** (what must be TRUE):
  1. Scripts 13-17 show T2_P2 progressing through Unresolved, Healthy, Unresolved, and Resolved states sequentially in Grafana
  2. Grafana shows dispatch metric increments for T2_P2 appearing only after all P1 tenants have reached Resolved state
**Plans**: TBD

Plans:
- [ ] 84-01: TBD

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
| 82. Fixture & Infrastructure | v2.6 | 0/TBD | Not started | - |
| 83. P1 Tenant Scripts (01-12) | v2.6 | 0/TBD | Not started | - |
| 84. P2 Tenant Scripts (13-17) | v2.6 | 0/TBD | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-24 — v2.6 roadmap added (phases 82-84)*
