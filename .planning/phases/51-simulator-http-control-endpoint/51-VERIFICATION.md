---
phase: 51-simulator-http-control-endpoint
verified: 2026-03-17T11:00:53Z
status: passed
score: 9/9 must-haves verified
re_verification: false
---

# Phase 51: Simulator HTTP Control Endpoint Verification Report

**Phase Goal:** The E2E simulator exposes an HTTP control endpoint so test scripts can switch OID return values mid-test without restarting the pod, while all existing E2E scenarios continue to pass unchanged
**Verified:** 2026-03-17T11:00:53Z
**Status:** passed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | POST /scenario/{name} switches active scenario; next poll reflects new OID values | VERIFIED | post_scenario sets global _active_scenario; DynamicInstance.getValue reads SCENARIOS[_active_scenario] on every call with no caching |
| 2 | GET /scenario returns current scenario name as JSON | VERIFIED | get_scenario returns web.json_response with _active_scenario value |
| 3 | GET /scenarios returns sorted list of all scenario names | VERIFIED | get_scenarios returns sorted(SCENARIOS.keys()) as JSON |
| 4 | POST /scenario/{unknown} returns 404 with error message | VERIFIED | raises web.HTTPNotFound with reason listing valid scenario names |
| 5 | Simulator starts in default scenario and logs active scenario on startup | VERIFIED | _active_scenario=default at module level; log.info Active scenario in main() at line 394 |
| 6 | Default scenario identical to pre-HTTP simulator OID values | VERIFIED | Baseline: .1.1=42 .1.2=100 .1.3=5000 .1.4=1000000 .1.5=360000 .1.6=E2E-TEST-VALUE .1.7=10.0.0.1 .2.1=99 .2.2=UNMAPPED; all match MAPPED_OIDS/UNMAPPED_OIDS |
| 7 | 6 new test-purpose OIDs in .999.4.x subtree respond to SNMP GET | VERIFIED | TEST_OIDS registration loop; 15 total OIDs registered (7+2+6) |
| 8 | Command response OID (.999.4.4) accepts SNMP SET and returns set value on GET | VERIFIED | WritableDynamicInstance.writeCommit stores value in SCENARIOS dict; MibScalar.setMaxAccess readwrite applied |
| 9 | STALE OIDs return noSuchInstance not genErr | VERIFIED | getValue raises NoSuchInstanceError when val is STALE; stale scenario sets .4.1 and .4.2 to STALE |

**Score:** 9/9 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| \`simulators/e2e-sim/e2e_simulator.py\` | HTTP-controlled SNMP simulator with scenario registry | VERIFIED | 429 lines; syntax-valid; all patterns confirmed |
| \`simulators/e2e-sim/requirements.txt\` | aiohttp==3.13.3 alongside pysnmp==7.1.22 | VERIFIED | Both dependencies present; 2-line file |
| \`simulators/e2e-sim/Dockerfile\` | EXPOSE 161/udp and EXPOSE 8080/tcp | VERIFIED | Both EXPOSE lines present; CMD unchanged |
| \`deploy/k8s/simulators/e2e-sim-deployment.yaml\` | containerPort 8080 in Deployment; port 8080 in Service | VERIFIED | Deployment: containerPort 8080 name http-control protocol TCP; Service: port 8080 targetPort http-control protocol TCP |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| post_scenario handler | _active_scenario module var | global _active_scenario | WIRED | Lines 349-354 confirmed |
| DynamicInstance.getValue | SCENARIOS[_active_scenario] | dict lookup by oid_str | WIRED | Line 197 confirmed |
| start_http_server() | snmpEngine.open_dispatcher() | run_until_complete called before open_dispatcher | WIRED | Line 404 before line 425 confirmed |
| WritableDynamicInstance.writeCommit | SCENARIOS dict | stores SET value in active scenario | WIRED | Line 211 confirmed |
| Dockerfile EXPOSE 8080/tcp | Deployment containerPort 8080 | named port http-control | WIRED | Both present; name consistent |
| Deployment containerPort http-control | Service targetPort http-control | named port reference | WIRED | Service targetPort references Deployment port name |

### Requirements Coverage

| Requirement | Description | Status | Evidence |
|-------------|-------------|--------|----------|
| SIM-01 | POST /scenario/{name} switches active scenario at runtime | SATISFIED | post_scenario handler wired to global _active_scenario and DynamicInstance |
| SIM-02 | GET /scenario returns current active scenario name | SATISFIED | get_scenario returns JSON with _active_scenario |
| SIM-03 | Scenario registry with named scenarios and per-OID values | SATISFIED | SCENARIOS dict with 5 named scenarios; each a complete 15-OID dict via _make_scenario() |
| SIM-04 | Default scenario reproduces static OID values -- zero regression | SATISFIED | Baseline values in _make_scenario({}) match pre-HTTP static values exactly |
| SIM-05 | Test-purpose OIDs for evaluate metric, resolved metric, command response | SATISFIED | 6 OIDs .999.4.1 through .999.4.6; .4.4 writable; superset of requirement |
| SIM-06 | K8s deployment updated with HTTP port 8080/tcp | SATISFIED | Deployment + Service both updated; Dockerfile EXPOSE 8080/tcp present |

Note: REQUIREMENTS.md shows all SIM-01 through SIM-06 as unchecked checkboxes. This is a documentation tracking gap only -- the code satisfies every requirement.

### Anti-Patterns Found

No TODO, FIXME, placeholder, or stub patterns found in any modified file.

### Human Verification Required

#### 1. Runtime scenario switching end-to-end

**Test:** Deploy updated image; POST http://localhost:8080/scenario/threshold_breach; SNMP GET OID 1.3.6.1.4.1.47477.999.4.1.0
**Expected:** Gauge32 value 90 returned (not 0)
**Why human:** Requires live K8s pod with aiohttp and SNMP agent both running simultaneously

#### 2. Scenario 11 regression check

**Test:** Run existing E2E scenario 11 against pod in default scenario
**Expected:** All assertions pass unchanged
**Why human:** Requires E2E test harness against live pod

#### 3. kubectl port-forward to 8080

**Test:** kubectl port-forward svc/e2e-simulator 8080:8080 -n simetra; curl http://localhost:8080/scenario
**Expected:** HTTP 200 with body {"scenario":"default"}
**Why human:** Requires live K8s cluster with deployed pod

### Gaps Summary

No gaps found. All automated checks pass. Phase goal is achieved in the codebase.

---

## Verification Detail

### Python Syntax

AST parse of e2e_simulator.py: Syntax OK (verified live during verification run).

### Startup Ordering (Critical Constraint)

Line 404: runner = loop.run_until_complete(start_http_server())
Line 425: snmpEngine.open_dispatcher()
HTTP server starts before open_dispatcher() blocks the event loop. Critical constraint met.

### Scenario Registry Completeness

5 scenario keys confirmed: default, threshold_breach, threshold_clear, bypass_active, stale.
Each built via _make_scenario(overrides) -- every scenario carries all 15 OIDs.

### OID Registration Count

- MAPPED_OIDS: 7 (subtree .999.1.x)
- UNMAPPED_OIDS: 2 (subtree .999.2.x)
- TEST_OIDS: 6 (subtree .999.4.x)
- Total: 15 OIDs registered

### STALE Sentinel

STALE = object() -- identity-compared (is STALE). NoSuchInstanceError raised from getValue (not genErr).
Only the stale scenario sets .4.1 and .4.2 to STALE. All other OIDs in all scenarios have concrete values.

### WritableDynamicInstance and VACM

Both writeTest and writeCommit call cbFun -- pysnmp state machine will not stall.
MibScalar.setMaxAccess(readwrite) applied to .4.4 scalar -- VACM permits SET before dispatching to instance.

### No web.run_app()

web.run_app is absent. web.AppRunner + web.TCPSite pattern used throughout.

---

_Verified: 2026-03-17T11:00:53Z_
_Verifier: Claude (gsd-verifier)_
