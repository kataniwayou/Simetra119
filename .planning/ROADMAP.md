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
- 🚧 **v2.4 Tenant Vector Metrics** - Phases 72-75 (in progress)

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

### 🚧 v2.4 Tenant Vector Metrics (In Progress)

**Milestone Goal:** Expose per-tenant internal evaluation state from SnapshotJob as 8 OTel instruments (6 counters, 1 gauge, 1 histogram) on a third meter that exports on all instances, and surface them in a new operations dashboard table with per-pod tenant status.

#### Phase 72: TenantMetricService & Meter Registration

**Goal:** The `SnmpCollector.Tenant` meter and all 8 tenant metric instruments exist as a registered singleton, exporting on both leader and follower instances — unblocking all downstream instrumentation work.
**Depends on:** Phase 71 (v2.3 complete)
**Requirements:** TMET-01, TMET-02, TMET-03, TMET-04, TMET-05, TMET-06, TMET-07, TMET-08, TMET-09
**Success Criteria** (what must be TRUE):
  1. `TenantMetricService` constructs without error and all 8 instruments are accessible by name in tests
  2. The `SnmpCollector.Tenant` meter is registered via `AddMeter` in `ServiceCollectionExtensions` alongside the two existing meters
  3. `TelemetryConstants.TenantMeterName` constant exists and is used by `TenantMetricService` — no magic strings
  4. `MetricRoleGatedExporter` requires no changes — the tenant meter passes through ungated on all instances
  5. All 6 counters, the state gauge, and the duration histogram have correct instrument names and `tenant_id`/`priority` labels confirmed by unit test construction
**Plans:** 2 plans

Plans:
- [ ] 72-01-PLAN.md — Create TenantMetricService, interface, TenantState enum, DI registration, and unit tests
- [ ] 72-02-PLAN.md — Migrate SnapshotJob from TierResult to TenantState, add ITenantMetricService injection

#### Phase 73: SnapshotJob Instrumentation

**Goal:** Every SnapshotJob evaluation cycle records live per-tenant counter, gauge, and histogram data to Prometheus — making the internal evaluation state observable from outside the process for the first time.
**Depends on:** Phase 72
**Requirements:** TSJI-01, TSJI-02, TSJI-03, TSJI-04
**Success Criteria** (what must be TRUE):
  1. Each of the 4 tier exit points in `EvaluateTenant` increments the correct counter by the actual holder/command count (not by 1)
  2. `tenant_state` gauge is recorded with the correct enum value (0-3) at every tier exit, including the Healthy path
  3. A per-tenant `Stopwatch` inside `EvaluateTenant` records histogram duration before each return — not wrapped around the `Task.WhenAll` group
  4. Command outcome counters (dispatched, failed, suppressed) increment at the dispatch decision site inside `EvaluateTenant`, not in `CommandWorkerService`
**Plans:** TBD

Plans:
- [ ] 73-01: TBD

#### Phase 74: Grafana Dashboard Panel

**Goal:** The operations dashboard shows a real-time per-tenant per-pod status table with state color mapping, tier counter rates, P99 duration, trend arrows, and copyable PromQL — giving operators immediate visibility into evaluation health across all replicas.
**Depends on:** Phase 73
**Requirements:** TDSH-01, TDSH-02, TDSH-03, TDSH-04, TDSH-05, TDSH-06
**Success Criteria** (what must be TRUE):
  1. A new tenant metrics table panel appears in the operations dashboard after the existing commands panels, with all 14 required columns (Host, Pod, Tenant, Priority, State, Dispatched, Failed, Suppressed, Stale, Resolved, Evaluate, P99 ms, Trend, PromQL)
  2. The State column displays color-coded text labels (green=Healthy, red=Unresolved, yellow=Resolved, grey=NotReady) driven by the enum value mapping
  3. Existing Host and Pod dashboard filter variables cascade correctly to filter tenant table rows
  4. The Trend column shows delta arrows derived from `delta(tenant_command_dispatched...)` via a Join by field transformation
  5. The PromQL column contains copyable per-row query strings using the `label_join`/`label_replace` pattern from the business dashboard
**Plans:** TBD

Plans:
- [ ] 74-01: TBD

#### Phase 75: E2E Validation Scenarios

**Goal:** E2E scenario scripts confirm the full path from SnapshotJob evaluation through OTel export to Prometheus, verifying every instrument, all-instances export, and correct label values — proving the feature works end-to-end before v2.4 ships.
**Depends on:** Phase 74
**Requirements:** TE2E-01, TE2E-02, TE2E-03, TE2E-04, TE2E-05
**Success Criteria** (what must be TRUE):
  1. All 8 tenant metric instruments appear in Prometheus with `tenant_id` and `priority` labels after a SnapshotJob cycle completes
  2. Tier counter increments match known evaluation paths — stale, resolved-gate, evaluate-violated, and commanded scenarios each produce the expected counter delta
  3. Follower pods export tenant metrics (non-zero values queryable by pod label) while `snmp_gauge`/`snmp_info` remain absent on follower pods
  4. `tenant_state` gauge values for all 4 enum states (0=NotReady, 1=Healthy, 2=Resolved, 3=Unresolved) are verified against controlled evaluation fixture outcomes
  5. `tenant_gauge_duration_milliseconds` histogram P99 is present in Prometheus and reports a value greater than zero
**Plans:** TBD

Plans:
- [ ] 75-01: TBD

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
| 72. TenantMetricService & Meter Registration | v2.4 | 0/2 | Not started | - |
| 73. SnapshotJob Instrumentation | v2.4 | 0/? | Not started | - |
| 74. Grafana Dashboard Panel | v2.4 | 0/? | Not started | - |
| 75. E2E Validation Scenarios | v2.4 | 0/? | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-23 — Phase 72 planned (2 plans in 2 waves)*
