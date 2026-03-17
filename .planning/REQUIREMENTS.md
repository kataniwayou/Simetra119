# Requirements: SNMP Monitoring System

**Defined:** 2026-03-17
**Core Value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.

## v2.1 Requirements

Requirements for E2E Tenant Evaluation Tests milestone. Each maps to roadmap phases.

### Simulator Infrastructure

- [ ] **SIM-01**: E2E simulator exposes HTTP endpoint (`POST /scenario/{name}`) to switch active scenario at runtime
- [ ] **SIM-02**: E2E simulator exposes HTTP endpoint (`GET /scenario`) returning the current active scenario name
- [ ] **SIM-03**: Scenario registry supports named scenarios, each defining return values for all registered OIDs
- [ ] **SIM-04**: Default scenario reproduces current static OID values (zero regression for existing E2E tests)
- [ ] **SIM-05**: Test-purpose OIDs registered for evaluate metric, 2 resolved metrics, and command response OID
- [ ] **SIM-06**: K8s deployment updated with HTTP port exposure (8080/tcp) for test script access

### Test Config Artifacts

- [ ] **CFG-01**: OID metric map entries for E2E simulator test OIDs (evaluate, resolved, command response)
- [ ] **CFG-02**: OID command map entry for E2E bypass command targeting simulator
- [ ] **CFG-03**: Device config entry for E2E simulator with test poll OIDs
- [ ] **CFG-04**: Tenant config — single tenant (priority 1) with 1 evaluate, 2 resolved, 1 command
- [ ] **CFG-05**: Tenant config — two tenants same priority
- [ ] **CFG-06**: Tenant config — two tenants different priority
- [ ] **CFG-07**: Tenant config variants with aggregate metric as evaluate

### Single-Tenant Scenarios

- [ ] **STS-01**: Healthy baseline — no violations, tier flow reaches Healthy, zero commands dispatched
- [ ] **STS-02**: Evaluate violated — all evaluate samples violated, tier flow reaches Commanded, command counter increments
- [ ] **STS-03**: Resolved gate — all resolved metrics violated, tier flow stops at ConfirmedBad, zero commands
- [ ] **STS-04**: Suppression window — second cycle within window shows command suppressed
- [ ] **STS-05**: Staleness — simulator stops/delays response, tier flow stops at Stale

### Multi-Tenant Scenarios

- [ ] **MTS-01**: Two tenants same priority — both evaluated in parallel, correct tier results per tenant
- [ ] **MTS-02**: Two tenants different priority — sequential groups, advance gate blocks on Commanded/Stale tenant

### Advanced Scenarios

- [ ] **ADV-01**: Aggregate metric as evaluate — synthetic pipeline feeds threshold check, correct tier flow
- [ ] **ADV-02**: Time series depth > 1 — series must fill before violation fires, validates all-samples check E2E

### Test Infrastructure

- [ ] **INF-01**: Bash test library with `sim_set_scenario` helper and `poll_until_log` function
- [ ] **INF-02**: Test runner orchestration with port-forward setup, per-scenario cleanup (`trap _cleanup EXIT`)
- [ ] **INF-03**: Validation via pod logs (tier debug lines) and Prometheus metrics (command counters with `sum()`)

## Future Requirements

- **D-SC-01**: Cycle duration histogram sanity check
- **D-SC-02**: Partial suppression (some commands suppressed, some not within single cycle)
- **D-SC-03**: Liveness vector stamp verification after snapshot cycle

## Out of Scope

| Feature | Reason |
|---------|--------|
| Modifying OBP/NPB simulators | User constraint — E2E simulator only |
| Unit test additions | This milestone is E2E only; unit tests are comprehensive (424 passing) |
| Collector code changes | Testing existing behavior, not adding features |
| Grafana dashboard test panels | Validation is via raw Prometheus queries and logs |
| Automated CI pipeline | Manual test execution for now |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| SIM-01 | — | Pending |
| SIM-02 | — | Pending |
| SIM-03 | — | Pending |
| SIM-04 | — | Pending |
| SIM-05 | — | Pending |
| SIM-06 | — | Pending |
| CFG-01 | — | Pending |
| CFG-02 | — | Pending |
| CFG-03 | — | Pending |
| CFG-04 | — | Pending |
| CFG-05 | — | Pending |
| CFG-06 | — | Pending |
| CFG-07 | — | Pending |
| STS-01 | — | Pending |
| STS-02 | — | Pending |
| STS-03 | — | Pending |
| STS-04 | — | Pending |
| STS-05 | — | Pending |
| MTS-01 | — | Pending |
| MTS-02 | — | Pending |
| ADV-01 | — | Pending |
| ADV-02 | — | Pending |
| INF-01 | — | Pending |
| INF-02 | — | Pending |
| INF-03 | — | Pending |

**Coverage:**
- v2.1 requirements: 25 total
- Mapped to phases: 0
- Unmapped: 25 ⚠️

---
*Requirements defined: 2026-03-17*
*Last updated: 2026-03-17 after initial definition*
