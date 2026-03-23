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
- 🚧 **v2.5 Tenant Metrics Approach Modification** - Phases 76-80 (in progress)

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

### 🚧 v2.5 Tenant Metrics Approach Modification (In Progress)

**Milestone Goal:** Replace 6 per-tenant counter instruments with 6 percentage gauges, refactor EvaluateTenant to a gather-then-decide flow that records all metrics at a single exit point, flip the resolved metric direction to measure violated holders (consistent with evaluate), and update the dashboard and E2E scenarios to match.

#### Phase 76: Percentage Gauge Instruments

**Goal:** TenantMetricService exposes 6 percentage gauges (replacing 6 counters), with the resolved gauge measuring violated holders, and unchanged instruments preserved intact.
**Depends on:** Phase 75
**Requirements:** PGA-01, PGA-02, PGA-03, PGA-04, PGA-05, PGA-06, RMD-01, UCH-01, UCH-02, CLN-01, CLN-02, UTT-02
**Success Criteria** (what must be TRUE):
  1. ITenantMetricService exposes 6 RecordXxxPercent methods accepting pre-computed float values; no counter increment methods remain on the interface
  2. TenantMetricService registers 6 ObservableGauge instruments (stale, resolved, evaluate, dispatched, failed, suppressed percent) and zero counter instruments for tenant tier/command metrics
  3. Resolved percent measures violated holders (numerator = violated resolved), not non-violated — higher value means more violations
  4. tenant_state gauge and tenant_evaluation_duration_milliseconds histogram are registered unchanged; unit tests confirm their existence and behaviour are unaffected
  5. TenantMetricService unit tests pass asserting gauge API, percentage values, and correct resolved direction
**Plans:** 1 plan
Plans:
- [x] 76-01-PLAN.md — Replace 6 counters with 6 percentage gauges, rename tenant.state, rewrite unit tests

#### Phase 77: Gather-Then-Decide Evaluation Flow

**Goal:** EvaluateTenant collects all tier results (stale count, resolved violations, evaluate violations, command outcomes) before making any state decision, then records all metrics together at exit.
**Depends on:** Phase 76
**Requirements:** EFR-01, EFR-02, EFR-03, UTT-01
**Success Criteria** (what must be TRUE):
  1. EvaluateTenant has exactly one early return path (NotReady); all other paths complete all tier computations before branching to state determination
  2. All 6 percentage gauge calls and the state gauge call occur at a single exit point after state is determined — no metrics recorded mid-flow
  3. tenant_state is derived from gathered tier results and percentages, not from which tier caused an early return
  4. SnapshotJob unit tests pass asserting percentage values and state correctness for each evaluation path (Healthy, Resolved, Unresolved)
**Plans:** 2 plans
Plans:
- [x] 77-01-PLAN.md — Add count helpers and rewrite EvaluateTenant to gather-then-decide flow
- [x] 77-02-PLAN.md — Rewrite metric-assertion tests and add new percentage tests

#### Phase 78: Counter Reference Cleanup

**Goal:** All residual counter references, counting helper methods, and dead code are removed from SnapshotJob and CommandWorkerService after the metric service and flow changes land.
**Depends on:** Phase 77
**Requirements:** CLN-03
**Success Criteria** (what must be TRUE):
  1. No counter increment call sites remain in SnapshotJob or CommandWorkerService; no orphaned counting helper methods exist in either class
  2. Build compiles cleanly with no warnings related to removed counter members
**Plans:** 1 plan
Plans:
- [x] 78-01-PLAN.md — Remove dead CountResolvedNonViolated method

#### Phase 79: Dashboard Percentage Update

**Goal:** The Operations dashboard Tenant Status table displays percentage values instead of raw counts, with PromQL queries updated from increase() counter queries to direct gauge queries.
**Depends on:** Phase 76
**Requirements:** DSH-01, DSH-02
**Success Criteria** (what must be TRUE):
  1. Tenant Status table columns show percentage values (0-100) for stale, resolved, evaluate, dispatched, failed, suppressed — not raw counts
  2. All 6 column PromQL queries reference the gauge instrument names directly (no increase() or rate() wrapping)
**Plans:** 1 plan
Plans:
- [x] 79-01-PLAN.md — Update PromQL queries and column headers for percentage gauges

#### Phase 80: E2E Scenario Updates

**Goal:** E2E scenarios 107-112 assert on percentage gauge values and confirm the 6 percentage gauge instruments are present, replacing counter delta assertions.
**Depends on:** Phase 78
**Requirements:** E2E-01, E2E-02
**Success Criteria** (what must be TRUE):
  1. Scenarios 107-112 each pass asserting on percentage gauge values (0-100 range) appropriate to the scenario's tenant state
  2. Smoke test confirms all 6 percentage gauge instrument names are present in Prometheus; no counter instrument names from v2.4 appear
**Plans:** TBD

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
| 80. E2E Scenario Updates | v2.5 | 0/TBD | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-23 — Phase 79 complete (dashboard updated)*
