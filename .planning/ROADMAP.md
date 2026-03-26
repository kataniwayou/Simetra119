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
- ✅ **v2.6 E2E Manual Tenant Simulation Suite** - Phases 82-83 (shipped 2026-03-24)
- 🚧 **v3.0 Preferred Leader Election** - Phases 84-89 (in progress)

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

<details>
<summary>✅ v2.6 E2E Manual Tenant Simulation Suite (Phases 82-83) - SHIPPED 2026-03-24</summary>

See `.planning/milestones/v2.6-ROADMAP.md` for details.

</details>

---

### v3.0 Preferred Leader Election (In Progress)

**Milestone Goal:** Pod co-located with SNMP devices gets leadership priority for lowest-latency monitoring, with full HA preserved when the preferred pod is absent.

#### Phase 84: Config and Interface Foundation
**Goal**: The system knows which pod is preferred and the two-lease design is locked in code before any behavioral changes exist
**Depends on**: Phase 83
**Requirements**: CFG-01, CFG-02 (CFG-03 and CFG-04 dropped)
**Success Criteria** (what must be TRUE):
  1. `LeaseOptions.PreferredNode` loads from config — app starts and behaves identically when the field is absent or empty
  2. Pod reads `PHYSICAL_HOSTNAME` env var at startup and determines `_isPreferredPod` — log line confirms preferred vs. non-preferred identity on startup
**Plans**: 1 plan

Plans:
- [x] 84-01: LeaseOptions.PreferredNode, IPreferredStampReader, PreferredLeaderService stub, DI registration, unit tests

#### Phase 85: PreferredHeartbeatService — Reader Path
**Goal**: Non-preferred pods maintain a live in-memory freshness signal by polling the heartbeat lease, with correct clock-skew tolerance and 404-as-stale semantics
**Depends on**: Phase 84
**Requirements**: HB-04
**Success Criteria** (what must be TRUE):
  1. `PreferredHeartbeatService` on a non-preferred pod polls the heartbeat lease at `HeartbeatRenewIntervalSeconds` cadence and updates `IsPreferredStampFresh`
  2. A 404 (lease not found) is treated identically to a stale timestamp — `IsPreferredStampFresh` returns false, no exception thrown
  3. Freshness threshold is `HeartbeatDurationSeconds + 5s` (clock-skew tolerance baked in) — a stamp older than this threshold yields false
  4. `IPreferredStampReader.IsPreferredStampFresh` returns a real derived value, not a stub — verified by unit test with a mocked lease response
**Plans**: 2 plans

Plans:
- [x] 85-01-PLAN.md — PreferredHeartbeatJob implementation, PreferredLeaderService volatile bool, DI wiring
- [x] 85-02-PLAN.md — Unit tests for UpdateStampFreshness and PreferredHeartbeatJob with mocked K8s

#### Phase 86: PreferredHeartbeatService — Writer Path and Readiness Gate
**Goal**: The preferred pod stamps the heartbeat lease only after it is genuinely ready, giving non-preferred pods an accurate presence signal that does not trigger premature yield
**Depends on**: Phase 85
**Requirements**: HB-01, HB-02, HB-03
**Success Criteria** (what must be TRUE):
  1. Preferred pod creates and renews the `snmp-collector-preferred` heartbeat lease at `HeartbeatRenewIntervalSeconds`, with pod identity and `renewTime` in the lease spec
  2. Stamping does not begin until `ReadinessHealthCheck` passes — a pod that has not completed watcher loading and first poll does not emit a heartbeat stamp
  3. On graceful shutdown the heartbeat lease is handled via TTL expiry (not explicit delete), preventing a 404 window that would cause non-preferred pods to race prematurely
  4. `PreferredHeartbeatService` is fully functional on both paths: writer on the preferred pod, reader on all others — verified by integration against the K8s Coordination API
**Plans**: 2 plans

Plans:
- [x] 86-01-PLAN.md — Writer path implementation, readiness gate, write-before-read Execute restructuring
- [x] 86-02-PLAN.md — Unit tests for writer path (constructor fix, create/replace, conflict, readiness gate)

#### Phase 87: K8sLeaseElection — Gate 1 (Backoff Before Acquire)
**Goal**: Non-preferred pods delay their leadership retry when the preferred pod is present, while preserving completely standard election behavior when the preferred pod is absent or unconfigured
**Depends on**: Phase 86
**Requirements**: ELEC-01, ELEC-03, ELEC-04
**Success Criteria** (what must be TRUE):
  1. When `IsPreferredStampFresh` is true, a non-preferred pod extends its retry delay — it does not immediately re-enter the `RunAndTryToHoldLeadershipForeverAsync` loop
  2. When `IsPreferredStampFresh` is false (stamp stale, lease absent, or `PreferredNode` not configured), the non-preferred pod competes with no added delay — behavior is identical to today's election
  3. The preferred pod itself is never subject to Gate 1 backoff — it competes immediately through the normal `LeaderElector` flow
  4. The `_innerCts` outer loop structure is in place and the `OnStoppedLeading` handler is proven idempotent (sets `_isLeader = false` only, no destructive teardown)
**Plans**: 2 plans

Plans:
- [x] 87-01-PLAN.md — K8sLeaseElection outer loop with _innerCts, Gate 1 backoff, CancelInnerElection method
- [x] 87-02-PLAN.md — Unit tests for backoff logic, CancelInnerElection safety, initial state verification

#### Phase 88: K8sLeaseElection — Gate 2 (Voluntary Yield While Leading)
**Goal**: A non-preferred pod that currently holds leadership releases it when the preferred pod recovers, allowing site-affinity to be restored without operator intervention
**Depends on**: Phase 87
**Requirements**: ELEC-02
**Success Criteria** (what must be TRUE):
  1. When a non-preferred pod is leader and `IsPreferredStampFresh` transitions to true (preferred pod has recovered), the leader voluntarily deletes the leadership lease
  2. After yielding, the preferred pod acquires leadership within one `RetryPeriod` through the normal `LeaderElector` flow — no force-acquire, no special path
  3. The yield path does not call `StopAsync` on the host — it cancels `_innerCts` only, allowing the outer loop to restart cleanly without affecting other hosted services
  4. End-to-end scenario verified: non-preferred pod leads → preferred pod stamp becomes fresh → non-preferred yields → preferred pod acquires → system returns to steady-state preferred leadership
**Plans**: 2 plans

Plans:
- [x] 88-01-PLAN.md — Yield condition check and YieldLeadershipAsync helper in PreferredHeartbeatJob
- [x] 88-02-PLAN.md — Unit tests for yield path (positive, negative conditions, delete failure)

#### Phase 89: Observability and Deployment Wiring
**Goal**: Every preferred-election decision is visible in logs, the deployment manifest enforces one-pod-per-node topology, and the node-name env var is correctly wired so the feature activates in production
**Depends on**: Phase 88
**Requirements**: OBS-01, DEP-01, DEP-02
**Success Criteria** (what must be TRUE):
  1. A structured INFO log line is emitted at each election decision point: backing off (stamp fresh, not competing), competing normally (stamp stale or feature off), yielding to preferred pod (stamp became fresh while leading), and heartbeat stamping started (preferred pod post-readiness)
  2. The deployment manifest includes a pod anti-affinity rule (`requiredDuringSchedulingIgnoredDuringExecution`, `kubernetes.io/hostname` topology key) preventing two collector pods from landing on the same node
  3. The pod spec injects `PHYSICAL_HOSTNAME` from `spec.nodeName` via Downward API env var — the preferred-election feature activates correctly in a multi-node cluster without manual config
**Plans**: 2 plans

Plans:
- [ ] 89-01-PLAN.md — Structured INFO logs at all 4 election decision points (OBS-01)
- [ ] 89-02-PLAN.md — Pod anti-affinity rule and PHYSICAL_HOSTNAME verification (DEP-01, DEP-02)

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
| 83. Command Interpreter | v2.6 | 1/1 | Complete | 2026-03-24 |
| 84. Config and Interface Foundation | v3.0 | 1/1 | Complete | 2026-03-25 |
| 85. PreferredHeartbeatService Reader Path | v3.0 | 2/2 | Complete | 2026-03-26 |
| 86. PreferredHeartbeatService Writer Path | v3.0 | 2/2 | Complete | 2026-03-26 |
| 87. Election Gate 1 — Backoff Before Acquire | v3.0 | 2/2 | Complete | 2026-03-26 |
| 88. Election Gate 2 — Voluntary Yield | v3.0 | 2/2 | Complete | 2026-03-26 |
| 89. Observability and Deployment Wiring | v3.0 | 0/2 | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-26 — Phase 89 planned*
