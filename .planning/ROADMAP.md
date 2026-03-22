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
- 🚧 **v2.3 Metric Validity & Correctness** - Phases 66-71 (in progress)

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

### 🚧 v2.3 Metric Validity & Correctness (In Progress)

**Milestone Goal:** Every pipeline counter, command counter, business metric value, metric label, and negative-path assertion is verified by E2E scenarios against the live SNMP simulator and Prometheus.

#### Phase 66: Pipeline Event Counters

**Goal**: The MediatR pipeline event counters faithfully reflect what enters and exits the pipeline during a normal run.
**Depends on**: Phase 65 (stable E2E runner)
**Requirements**: MCV-01, MCV-02, MCV-03, MCV-04, MCV-05, MCV-06, MCV-07
**Success Criteria** (what must be TRUE):
  1. A poll cycle producing N OIDs causes `snmp.event.published` to increase by exactly N.
  2. A trap delivering M OIDs causes `snmp.event.published` to increase by exactly M.
  3. `snmp.event.handled` increases only for OIDs that are mapped in oidmaps.json; unmapped OIDs produce no handled increment.
  4. `snmp.event.rejected` increases only for OIDs not in oidmaps.json; mapped OIDs produce no rejected increment.
  5. `snmp.event.errors` reads 0 after a complete normal E2E run.
**Plans:** 3 plans
Plans:
- [x] 66-01-PLAN.md — Add assert_delta_eq/ge helpers and report category
- [x] 66-02-PLAN.md — Scenarios 69-72: poll published, trap published, handled parity, handled-not-for-rejected
- [x] 66-03-PLAN.md — Scenarios 73-75: rejected behavior (unmapped), rejected stays 0 (mapped), errors stays 0

#### Phase 67: Poll & Trap Infrastructure Counters

**Goal**: The SNMP-layer infrastructure counters accurately track poll execution, trap authentication, device reachability state transitions, and tenant fan-out writes.
**Depends on**: Phase 66
**Requirements**: MCV-08, MCV-09, MCV-10, MCV-11, MCV-12, MCV-13
**Success Criteria** (what must be TRUE):
  1. `snmp.poll.executed` increases by 1 each poll cycle regardless of OID-level success.
  2. `snmp.trap.received` increases for traps with a valid community string and does not increase for traps with an invalid community string.
  3. `snmp.trap.auth_failed` increases for every trap with a bad community string.
  4. After 3 consecutive poll failures to an unreachable IP, `snmp.poll.unreachable` has increased; once that device becomes reachable again, `snmp.poll.recovered` increases.
  5. `snmp.tenantvector.routed` increases when a tenant vector fan-out write completes.
**Plans:** 2 plans
Plans:
- [x] 67-01-PLAN.md — Scenarios 76-79: poll.executed, trap.received, trap.received negative, trap.auth_failed + report category update
- [x] 67-02-PLAN.md — Scenarios 80-82: poll.unreachable, poll.recovered, tenantvector.routed

#### Phase 68: Command Counters

**Goal**: The SNMP SET command lifecycle counters correctly reflect dispatch, suppression, and failure at tier=4.
**Depends on**: Phase 67
**Requirements**: CCV-01, CCV-02, CCV-03, CCV-04
**Success Criteria** (what must be TRUE):
  1. When SnapshotJob evaluates a tenant at tier=4 and enqueues a SET command, `snmp.command.dispatched` increases by 1.
  2. A second tier=4 evaluation within the suppression window increments both `snmp.command.dispatched` and `snmp.command.suppressed` simultaneously (dispatched fires on every tier=4 enqueue, suppressed fires when within suppression window).
  3. Triggering a SET command to an unreachable device causes `snmp.command.failed` to increase (timeout path).
**Plans:** 4 plans
Plans:
- [x] 68-01-PLAN.md — Scenarios 83-84: command.dispatched (CCV-01), command.suppressed + dispatched-unchanged (CCV-02/03) + report category
- [x] 68-02-PLAN.md — Scenario 85: command.failed via unmapped CommandName (CCV-04) + new fixture
- [ ] 68-03-PLAN.md — Gap closure: fix CCV-03 assertion (dispatched fires on every tier=4, not mutually exclusive with suppression)
- [ ] 68-04-PLAN.md — Gap closure: rewrite CCV-04 to use timeout path (unreachable IP) instead of unmapped CommandName

#### Phase 69: Business Metric Value Correctness

**Goal**: Every SNMP type produced by the simulator is reflected with its exact numeric or string value in Prometheus.
**Depends on**: Phase 66
**Requirements**: MVC-01, MVC-02, MVC-03, MVC-04, MVC-05, MVC-06, MVC-07, MVC-08
**Success Criteria** (what must be TRUE):
  1. `snmp_gauge` for a Gauge32 OID reports the exact integer the simulator was configured with (e.g., set 42, Prometheus shows 42).
  2. `snmp_gauge` correctly represents Integer32, Counter32, Counter64, and TimeTicks values from the simulator.
  3. `snmp_info` value label matches the OctetString and IpAddress values exactly as set in the simulator.
  4. After changing a simulator value (42 to 99), `snmp_gauge` reflects the new value within the next poll cycle.
**Plans**: TBD

#### Phase 70: Label Correctness

**Goal**: Every metric exported to Prometheus carries the correct source, snmp_type, resolved_name, and device_name labels.
**Depends on**: Phase 69
**Requirements**: MLC-01, MLC-02, MLC-03, MLC-04, MLC-05, MLC-06, MLC-07, MLC-08
**Success Criteria** (what must be TRUE):
  1. A polled OID has `source="poll"`, a trap-originated OID has `source="trap"`, a SET response OID has `source="command"`, and an aggregated metric has `source="synthetic"`.
  2. `snmp_type` label on `snmp_gauge` matches the SNMP type (gauge32, integer32, counter32, counter64, timeticks) and on `snmp_info` matches (octetstring, ipaddress).
  3. `resolved_name` label matches the name defined in oidmaps.json for that OID.
  4. `device_name` label matches the name derived from the device's community string.
**Plans**: TBD

#### Phase 71: Negative Proofs

**Goal**: The system provably suppresses, rejects, or withholds metrics in every defined negative-path scenario.
**Depends on**: Phase 70
**Requirements**: MNP-01, MNP-02, MNP-03, MNP-04, MNP-05
**Success Criteria** (what must be TRUE):
  1. The heartbeat OID never appears as a `snmp_gauge` or `snmp_info` series in Prometheus.
  2. An OID that is not in oidmaps.json produces no `snmp_gauge` or `snmp_info` series in Prometheus.
  3. A trap with a bad community string produces no increment to `snmp.trap.received`.
  4. `snmp.trap.dropped` reads 0 after a complete normal E2E run.
  5. Querying Prometheus on a follower pod's scrape target returns no `snmp_gauge` or `snmp_info` series.
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
| 63. Two Tenant Independence | v2.2 | 2/2 | Complete | 2026-03-20 |
| 64. Advance Gate Logic | v2.2 | 3/3 | Complete | 2026-03-20 |
| 65. E2E Runner Fixes & Flaky Stabilization | v2.2 | 1/1 | Complete | 2026-03-22 |
| 66. Pipeline Event Counters | v2.3 | 3/3 | Complete | 2026-03-22 |
| 67. Poll & Trap Infrastructure Counters | v2.3 | 2/2 | Complete | 2026-03-22 |
| 68. Command Counters | v2.3 | 2/4 | Gap closure | 2026-03-22 |
| 69. Business Metric Value Correctness | v2.3 | 0/? | Not started | - |
| 70. Label Correctness | v2.3 | 0/? | Not started | - |
| 71. Negative Proofs | v2.3 | 0/? | Not started | - |

---
*Roadmap created: 2026-03-10*
*Last updated: 2026-03-22 -- Phase 68 gap closure (2 plans: fix CCV-03 assertion, rewrite CCV-04 timeout path)*
