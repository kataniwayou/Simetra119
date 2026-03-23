# Requirements: SNMP Monitoring System — v2.6 E2E Manual Tenant Simulation Suite

**Defined:** 2026-03-24
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.6 Requirements

### Fixture Setup

- [ ] **FIX-01**: 4-tenant ConfigMap fixture with T1_P1 (P1, 2E/2R/1C), T2_P1 (P1, 4E/4R/1C), T1_P2 (P2, 2E/2R/1C), T2_P2 (P2, 4E/4R/1C)
- [ ] **FIX-02**: Each tenant uses unique OID suffixes (no OID collision between tenants)
- [ ] **FIX-03**: All tenants start in Healthy state after fixture applied and grace window passes
- [ ] **FIX-04**: OID metric map extended with entries for all 4 tenants (16 evaluate + 16 resolved OIDs minimum)

### Script Runner

- [ ] **RUN-01**: Sequential script execution with user approval prompt between each script
- [ ] **RUN-02**: Scripts set OID violations and leave states as-is (no cleanup between scripts)
- [ ] **RUN-03**: Script 01 restarts pods to reset all states (NotReady → Healthy)

### P1 Tenant Scenarios

- [ ] **P1S-01**: Scripts 02-04 progressively violate T2_P1 evaluate (25% → 75% → Unresolved 100%)
- [ ] **P1S-02**: Scripts 07-09 progressively resolve T2_P1 (50% res → 75% res → Resolved 100%/100%)
- [ ] **P1S-03**: Scripts 10-12 progressively violate T1_P1 (50% → Unresolved → Resolved)

### P2 Tenant Scenarios

- [ ] **P2S-01**: Scripts 05-06 violate T1_P2 while P1 tenant Unresolved (advance gate blocks — no dispatch)
- [ ] **P2S-02**: Scripts 13-17 progressively cycle T2_P2 (Unresolved → Healthy → Unresolved → Resolved)

### Advance Gate Verification

- [ ] **AGT-01**: Script 06 proves P2 tenant does NOT dispatch when P1 tenant is Unresolved (advance gate blocks)
- [ ] **AGT-02**: Scripts 13+ prove P2 tenants resume evaluation after all P1 tenants reach Resolved

### Report Integration

- [ ] **RPT-01**: Scripts numbered 114-130 (continuing from 113), report.sh category updated

## Out of Scope

| Feature | Reason |
|---------|--------|
| Automated assertions in scripts | This suite is manual verification — user observes Grafana |
| Stale path scenarios | Staleness testing covered in v2.4/v2.5 E2E suites |
| Multi-group (3+ priorities) | 2 priority groups sufficient for advance gate verification |
| Command suppression window testing | Covered in v2.4 E2E suite |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| FIX-01 | Phase 82 | Pending |
| FIX-02 | Phase 82 | Pending |
| FIX-03 | Phase 82 | Pending |
| FIX-04 | Phase 82 | Pending |
| RUN-01 | Phase 82 | Pending |
| RUN-02 | Phase 82 | Pending |
| RUN-03 | Phase 82 | Pending |
| RPT-01 | Phase 82 | Pending |
| P1S-01 | Phase 83 | Pending |
| P1S-02 | Phase 83 | Pending |
| P1S-03 | Phase 83 | Pending |
| P2S-01 | Phase 83 | Pending |
| AGT-01 | Phase 83 | Pending |
| P2S-02 | Phase 84 | Pending |
| AGT-02 | Phase 84 | Pending |

**Coverage:**
- v2.6 requirements: 15 total
- Mapped to phases: 15
- Unmapped: 0

---
*Requirements defined: 2026-03-24*
*Last updated: 2026-03-24 — traceability populated (phases 82-84)*
