---
phase: 83-command-interpreter
verified: 2026-03-24T15:38:22Z
status: human_needed
score: 6/7 must-haves verified automatically
human_verification:
  - test: "Run T1_P1-V-2E-1R against a live cluster and confirm the simulator receives the correct OID value HTTP calls"
    expected: "Verbose output shows 4 POST calls (2E violated + 1R violated + 1R healthy). Grafana shows T1_P1 in violation state."
    why_human: "Requires live Kubernetes cluster with e2e-simulator service and registered OIDs in subtrees 8-11"
  - test: "Run T1_P1-S-1E-0R and confirm the stale endpoint is called for E slot 1 while remaining slots receive healthy values"
    expected: "Verbose output shows stale call for 8.1, healthy calls for 8.2, 8.3, 8.4. No violation in Grafana, but stale state visible."
    why_human: "Requires live cluster; sim_stale path in apply_role can only be confirmed end-to-end with a running simulator"
  - test: "Run T1_P1-V-0E-0R and confirm all 4 OIDs return to healthy values in Grafana"
    expected: "All 4 OIDs receive their healthy values. Grafana shows T1_P1 as Healthy."
    why_human: "Grafana state change requires running cluster"
---

# Phase 83: Command Interpreter Verification Report

**Phase Goal:** A command interpreter accepts {Tenant}-{V/S}-{#}E-{#}R patterns from the Claude Code CLI, validates them against the mapping, translates them to simulator HTTP API calls, and produces clear errors for invalid input.
**Verified:** 2026-03-24T15:38:22Z
**Status:** human_needed
**Re-verification:** No - initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Running a valid V-mode pattern causes correct violated/healthy OID calls | ? HUMAN | apply_role() loop verified in code; sim_post calls SIM_URL/oid/{oid}/{value}; live cluster needed |
| 2 | Running a valid S-mode pattern causes stale calls for affected slots | ? HUMAN | sim_stale() defined and called in S-mode branch; live cluster needed for end-to-end proof |
| 3 | Non-violated/non-stale metrics are explicitly set to healthy every invocation | VERIFIED | Lines 149-151: else branch always calls sim_post with .healthy value; comment cites CMD-07 |
| 4 | An unknown tenant name produces a red error listing all valid tenant names | VERIFIED | Line 100: iterates VALID_TENANTS, error message includes T1_P1 T2_P1 T1_P2 T2_P2 |
| 5 | A count exceeding available metrics produces a red error identifying the limit | VERIFIED | Lines 106-107: checks E_COUNT <= e_total and R_COUNT <= r_total; error states count and limit |
| 6 | A malformed pattern produces a red error showing expected format | VERIFIED | Line 88: regex failure triggers error with expected format and T1_P1-V-2E-1R example |
| 7 | Silent on success (no stdout output) | VERIFIED | All echo statements route to stderr; no bare echo on success path |

**Score:** 5/7 truths fully verified automatically; 2 require live cluster confirmation

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `tests/e2e/lib/sim_command.sh` | Standalone command interpreter, min 80 lines | VERIFIED | 163 lines, executable (-rwxr-xr-x), syntax valid (bash -n passes) |
| `tests/e2e/lib/oid_map.sh` | OID mapping with OID_MAP, VALID_TENANTS, counts | VERIFIED | 91 lines, declares OID_MAP, TENANT_EVAL_COUNT, TENANT_RES_COUNT, VALID_TENANTS |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `sim_command.sh` | `oid_map.sh` | BASH_SOURCE-relative source | WIRED | Line 29: SCRIPT_DIR via BASH_SOURCE[0]; line 30: source "${SCRIPT_DIR}/oid_map.sh" |
| `sim_command.sh` | `http://localhost:8080/oid/{suffix}/{value}` | curl POST in sim_post() | WIRED | Lines 117-120: curl -X POST "${SIM_URL}/oid/${oid}/${value}" with http_code check |
| `sim_command.sh` | `http://localhost:8080/oid/{suffix}/stale` | curl POST in sim_stale() | WIRED | Lines 127-130: curl -X POST "${SIM_URL}/oid/${oid}/stale" with http_code check |
| `sim_command.sh` | `kubectl port-forward svc/e2e-simulator` | background process + trap cleanup | WIRED | Lines 55-66: PF_PID captured, trap cleanup EXIT, sleep 2 |
| `apply_role()` | `OID_MAP` entries | bash associative array lookup | WIRED | Line 141: oid="${OID_MAP[${TENANT}.${role}.${i}.oid]}"; .violated and .healthy also resolved |
| `error()` helper | stderr + red color | echo -e RED to stderr | WIRED | Lines 43-49: RED='\033[0;31m', NC='\033[0m', echo -e to stderr |

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| CMD-01 | SATISFIED | Line 80: PATTERN="$1"; lines 82-89: regex parse of {Tenant}-{V/S}-{#}E-{#}R |
| CMD-02 | SATISFIED | Lines 82-107: pattern regex extraction + tenant loop validation + count bounds check against oid_map |
| CMD-03 | SATISFIED | Lines 113-121: sim_post calls curl POST to SIM_URL/oid/{oid}/{value} |
| CMD-04 | SATISFIED | Line 100: error message includes ${VALID_TENANTS} listing all 4 tenants |
| CMD-05 | SATISFIED | Lines 106-107: separate E and R count checks with per-role limit in error message |
| CMD-06 | SATISFIED | Line 88: regex mismatch triggers error showing expected format and example |
| CMD-07 | SATISFIED | Lines 149-151: else branch in apply_role always calls sim_post with .healthy value for non-affected slots |
| CMD-08 | SATISFIED | Lines 143-148: MODE="S" branch calls sim_stale() which posts to /oid/{oid}/stale endpoint |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None | - | - | - | No stub patterns, TODO/FIXME, empty returns, or placeholder text found |

### Human Verification Required

#### 1. V-Mode End-to-End: Violated State Reached in Grafana

**Test:** With cluster running, execute `./tests/e2e/lib/sim_command.sh -v T1_P1-V-2E-1R`
**Expected:** Verbose stderr shows 4 POST calls (OIDs 8.1 and 8.2 with value 0, OID 8.3 with value 0, OID 8.4 with value 1). Exit code 0. No stdout. Grafana shows T1_P1 in violation state.
**Why human:** Requires a live Kubernetes cluster with e2e-simulator deployed and Grafana connected to reflect OID values.

#### 2. S-Mode End-to-End: Stale Endpoint Called for Affected Slots

**Test:** With cluster running, execute `./tests/e2e/lib/sim_command.sh -v T1_P1-S-1E-0R`
**Expected:** Verbose stderr shows stale call for OID 8.1, and healthy-value POST calls for OIDs 8.2, 8.3, 8.4. Exit code 0. No stdout.
**Why human:** sim_stale() path through to a live simulator endpoint cannot be confirmed without a running cluster.

#### 3. Full Healthy Reset

**Test:** With cluster running, execute `./tests/e2e/lib/sim_command.sh -v T1_P1-V-0E-0R`
**Expected:** All 4 OIDs receive their healthy values (8.1=10, 8.2=10, 8.3=1, 8.4=1). Grafana returns T1_P1 to Healthy.
**Why human:** Grafana state change requires the full stack running.

### Gaps Summary

No gaps. All 8 requirements (CMD-01 through CMD-08) map to verified code paths. The script is substantive (163 lines), executable, passes bash syntax check, has no stub patterns, and all key links are wired. Three human verification items remain because they require a live Kubernetes cluster with the e2e-simulator service deployed.

---

_Verified: 2026-03-24T15:38:22Z_
_Verifier: Claude (gsd-verifier)_
