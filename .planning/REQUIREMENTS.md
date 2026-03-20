# Requirements: SNMP Monitoring System

**Defined:** 2026-03-20
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.2 Requirements

Requirements for Progressive E2E Snapshot Suite milestone. Each maps to roadmap phases.

### Stage 1: Single Tenant Evaluation States

- [ ] **PSS-01**: Not Ready state detected before grace window ends (1 tenant, group 1)
- [ ] **PSS-02**: Stale poll data triggers tier=1 then tier=4 command dispatch
- [ ] **PSS-03**: Stale synthetic source triggers tier=1 then tier=4 command dispatch
- [ ] **PSS-04**: Trap-sourced holder unaffected by stale poll data (immunity)
- [ ] **PSS-05**: Command-sourced holder unaffected by stale poll data (immunity)
- [ ] **PSS-06**: All resolved violated produces tier=2 Resolved, zero commands
- [ ] **PSS-07**: Partial resolved violated continues to tier=3 (not tier=2)
- [ ] **PSS-08**: All evaluate violated produces tier=4 Unresolved, commands dispatched
- [ ] **PSS-09**: All metrics in-range produces tier=3 Healthy, no action
- [ ] **PSS-10**: Unresolved with suppression — tier=4 but commands suppressed within window

### Stage 2: Two Tenant Independence

- [ ] **PSS-11**: T1=Healthy + T2=Unresolved — independent per-tenant results, no interference
- [ ] **PSS-12**: T1=Resolved + T2=Healthy — both evaluated with correct per-tenant tier logs
- [ ] **PSS-13**: Both tenants Unresolved — both dispatch commands independently

### Stage 3: Advance Gate Logic

- [ ] **PSS-14**: All G1 Resolved — gate passes, G2 evaluated
- [ ] **PSS-15**: All G1 Healthy — gate passes, G2 evaluated
- [ ] **PSS-16**: G1 mixed Resolved+Healthy — gate passes, G2 evaluated
- [ ] **PSS-17**: All G1 Unresolved — gate blocks, G2 not evaluated
- [ ] **PSS-18**: G1 mixed Resolved+Unresolved — gate blocks, G2 not evaluated
- [ ] **PSS-19**: G1 mixed Healthy+Unresolved — gate blocks, G2 not evaluated
- [ ] **PSS-20**: All G1 Not Ready — gate blocks, G2 not evaluated

### Infrastructure

- [ ] **PSS-INF-01**: Stage gating — Stage 2 runs only if Stage 1 passes, Stage 3 only if Stage 2 passes
- [ ] **PSS-INF-02**: Three tenant fixtures (1-tenant, 2-tenant, 4-tenant) with clean configmap restore
- [ ] **PSS-INF-03**: Reuses existing OIDs, simulator HTTP endpoints, sim.sh helpers

## Out of Scope

| Feature | Reason |
|---------|--------|
| Modifying simulator or collector code | Testing existing behavior |
| Modifying existing scenarios 1-52 | New suite runs alongside |
| Time series depth > 1 | Covered by ADV-02 (scenario 37) |
| Aggregate as evaluate | Covered by ADV-01 (scenario 36) |
| Suppression window multi-cycle | Covered by STS-04 (scenario 32) |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PSS-01 through PSS-10 | TBD | Pending |
| PSS-11 through PSS-13 | TBD | Pending |
| PSS-14 through PSS-20 | TBD | Pending |
| PSS-INF-01 through PSS-INF-03 | TBD | Pending |

**Coverage:**
- v2.2 requirements: 23 total
- Mapped to phases: 0 (roadmap pending)
- Unmapped: 23

---
*Requirements defined: 2026-03-20*
