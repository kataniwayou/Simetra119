# Requirements: SNMP Monitoring System — v2.6 E2E Manual Tenant Simulation Suite

**Defined:** 2026-03-24
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.6 Requirements

### Fixture Setup

- [x] **FIX-01**: 4-tenant ConfigMap fixture with T1_P1 (P1, 2E/2R/1C), T2_P1 (P1, 4E/4R/1C), T1_P2 (P2, 2E/2R/1C), T2_P2 (P2, 4E/4R/1C)
- [x] **FIX-02**: Each tenant uses unique OID suffixes (no OID collision between tenants)
- [x] **FIX-03**: OID metric map extended with entries for all 4 tenants
- [x] **FIX-04**: All tenants start in Healthy state after fixture applied and grace window passes

### Tenant OID Mapping

- [x] **MAP-01**: Hardcoded mapping file defining per-tenant per-role OID suffixes with healthy/violated values
- [x] **MAP-02**: Mapping is easy to maintain — add tenant or metric by adding a line

### Command Interpreter

- [x] **CMD-01**: Accept pattern commands from CLI in format: {Tenant}-{V/S}-{#}E-{#}R
- [x] **CMD-02**: Parse pattern, validate tenant name, mode (V/S), and metric counts against mapping
- [x] **CMD-03**: Translate pattern to simulator HTTP API calls (existing /oid/{suffix}/{value} endpoints)
- [x] **CMD-04**: Error on unknown tenant with list of available tenants
- [x] **CMD-05**: Error on count exceeding available metrics for that tenant/role
- [x] **CMD-06**: Error on invalid pattern format with expected format hint
- [x] **CMD-07**: Set non-violated metrics to healthy value (not just the violated ones)
- [x] **CMD-08**: Stale mode (S) uses sim_set_oid_stale for the specified metrics

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
| FIX-01 | Phase 82 | Complete |
| FIX-02 | Phase 82 | Complete |
| FIX-03 | Phase 82 | Complete |
| FIX-04 | Phase 82 | Complete |
| MAP-01 | Phase 82 | Complete |
| MAP-02 | Phase 82 | Complete |
| CMD-01 | Phase 83 | Complete |
| CMD-02 | Phase 83 | Complete |
| CMD-03 | Phase 83 | Complete |
| CMD-04 | Phase 83 | Complete |
| CMD-05 | Phase 83 | Complete |
| CMD-06 | Phase 83 | Complete |
| CMD-07 | Phase 83 | Complete |
| CMD-08 | Phase 83 | Complete |

**Coverage:**
- v2.6 requirements: 14 total
- Mapped to phases: 14 (100%)
- Unmapped: 0

---
*Requirements defined: 2026-03-24*
*Last updated: 2026-03-24 — traceability updated for revised roadmap (phases 82-83)*
