# Requirements: SNMP Monitoring System — v2.6 E2E Manual Tenant Simulation Suite

**Defined:** 2026-03-24
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.6 Requirements

### Fixture Setup

- [ ] **FIX-01**: 4-tenant ConfigMap fixture with T1_P1 (P1, 2E/2R/1C), T2_P1 (P1, 4E/4R/1C), T1_P2 (P2, 2E/2R/1C), T2_P2 (P2, 4E/4R/1C)
- [ ] **FIX-02**: Each tenant uses unique OID suffixes (no OID collision between tenants)
- [ ] **FIX-03**: OID metric map extended with entries for all 4 tenants
- [ ] **FIX-04**: All tenants start in Healthy state after fixture applied and grace window passes

### Tenant OID Mapping

- [ ] **MAP-01**: Hardcoded mapping file defining per-tenant per-role OID suffixes with healthy/violated values
- [ ] **MAP-02**: Mapping is easy to maintain — add tenant or metric by adding a line

### Command Interpreter

- [ ] **CMD-01**: Accept pattern commands from CLI in format: {Tenant}-{V/S}-{#}E-{#}R
- [ ] **CMD-02**: Parse pattern, validate tenant name, mode (V/S), and metric counts against mapping
- [ ] **CMD-03**: Translate pattern to simulator HTTP API calls (existing /oid/{suffix}/{value} endpoints)
- [ ] **CMD-04**: Error on unknown tenant with list of available tenants
- [ ] **CMD-05**: Error on count exceeding available metrics for that tenant/role
- [ ] **CMD-06**: Error on invalid pattern format with expected format hint
- [ ] **CMD-07**: Set non-violated metrics to healthy value (not just the violated ones)
- [ ] **CMD-08**: Stale mode (S) uses sim_set_oid_stale for the specified metrics

## Out of Scope

| Feature | Reason |
|---------|--------|
| Automated assertions | Manual verification via Grafana — user observes |
| Script files (107-130) | Replaced by interactive command interpreter |
| report.sh changes | No scripts to categorize |
| New simulator code | Reuse existing HTTP API endpoints |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| FIX-01 | TBD | Pending |
| FIX-02 | TBD | Pending |
| FIX-03 | TBD | Pending |
| FIX-04 | TBD | Pending |
| MAP-01 | TBD | Pending |
| MAP-02 | TBD | Pending |
| CMD-01 | TBD | Pending |
| CMD-02 | TBD | Pending |
| CMD-03 | TBD | Pending |
| CMD-04 | TBD | Pending |
| CMD-05 | TBD | Pending |
| CMD-06 | TBD | Pending |
| CMD-07 | TBD | Pending |
| CMD-08 | TBD | Pending |

**Coverage:**
- v2.6 requirements: 14 total
- Mapped to phases: 0 (pending roadmap)
- Unmapped: 14

---
*Requirements defined: 2026-03-24*
*Last updated: 2026-03-24 — revised from script-based to interactive command approach*
