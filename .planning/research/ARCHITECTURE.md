# Architecture Patterns: HTTP-Controllable E2E Simulator

**Domain:** E2E tenant evaluation test infrastructure
**Researched:** 2026-03-17
**Scope:** HTTP scenario control endpoint + test script orchestration for tenant evaluation testing

---

## Existing Architecture (Baseline)

All claims in this section derive from direct reading of source files.

### Simulator process (`simulators/e2e-sim/e2e_simulator.py`)

The simulator is a single Python 3.12 process. Its runtime topology is:

```
Process
  asyncio event loop  (driven by snmpEngine.open_dispatcher())
    SNMP engine          UDP/161, driven by open_dispatcher() — blocking call
    supervised_task: valid_trap_loop           (asyncio task)
    supervised_task: bad_community_trap_loop   (asyncio task)
```

Key facts:
- `snmpEngine.open_dispatcher()` is the last call in `main()`. It is a **blocking
  call** that hands control to pysnmp's asyncio transport and does not return until
  `snmpEngine.close_dispatcher()` is called via the shutdown signal handler.
- OID values are fixed at module load time. Each of the 9 OIDs gets a
  `DynamicInstance` whose `_get_value_fn` is a frozen closure:
  `lambda v=static_value: v`. The SNMP engine calls `getValue()` on every
  GET/GETNEXT/GETBULK request.
- There is no existing HTTP server, no scenario concept, and no runtime-mutable
  state other than the trap interval counters.
- The only Python dependency is `pysnmp==7.1.22`.

### OID surface served

```
Prefix: 1.3.6.1.4.1.47477.999
  .1.1.0  gauge_test       Gauge32       42
  .1.2.0  integer_test     Integer32     100
  .1.3.0  counter32_test   Counter32     5000
  .1.4.0  counter64_test   Counter64     1000000
  .1.5.0  timeticks_test   TimeTicks     360000
  .1.6.0  info_test        OctetString   "E2E-TEST-VALUE"
  .1.7.0  ip_test          IpAddress     "10.0.0.1"
  .2.1.0  unmapped_gauge   Gauge32       99         (not in oid map)
  .2.2.0  unmapped_info    OctetString   "UNMAPPED"  (not in oid map)
```

All 7 mapped OIDs are registered in `simetra-oid-metric-map` ConfigMap
(`e2e_gauge_test` ... `e2e_ip_test`) and the e2e device in `simetra-devices`
ConfigMap uses community `Simetra.E2E-SIM`, polls at 10s, and requests all 7.

### K8s deployment

- Pod: `e2e-simulator`, namespace `simetra`
- Exposes UDP/161 only. No TCP port exposed today.
- Liveness and readiness probes: SNMP GET to `127.0.0.1:161` for OID `.999.1.1.0`.

### Test runner (`tests/e2e/`)

```
run-all.sh
  sources: lib/common.sh  lib/prometheus.sh  lib/kubectl.sh  lib/report.sh
  for each scenarios/NN-*.sh:
    source scenario   (runs in the same bash shell, sharing all variables)
```

All scenario scripts share a single shell session. Each script is responsible
for its own setup and cleanup. Currently there is no HTTP contact with the
simulator — all scenario variation is done by mutating K8s ConfigMaps.

The runner has two live integration points:
1. **Prometheus** — queried via `curl` to `localhost:9090` (port-forwarded by
   `start_port_forward prometheus 9090 9090` in `run-all.sh`)
2. **kubectl** — applied directly against the `simetra` namespace

---

## Recommended Architecture for HTTP Control

### Core integration decision: aiohttp on the shared asyncio event loop

The blocking constraint is `snmpEngine.open_dispatcher()`. Any HTTP server
must share the same asyncio event loop. Use **aiohttp.web** with `AppRunner`.

Do not use:
- Flask, FastAPI, or any WSGI/ASGI framework — they require a separate server
  process or threads, adding synchronization complexity that is not needed here.
- `asyncio.start_server` (raw streams) — aiohttp.web provides cleaner routing
  and JSON handling with no additional conceptual overhead.

Rationale for aiohttp:
- aiohttp.web's `AppRunner` + `TCPSite.start()` are coroutines that integrate
  with an existing event loop before handing it to pysnmp.
- HTTP handlers and `DynamicInstance.getValue()` run on the same event loop
  thread (asyncio is single-threaded). A plain Python module-level variable
  for scenario state is safe — no locking needed because concurrent mutation
  from the HTTP handler and concurrent reads from `getValue()` cannot interleave
  within a single-threaded event loop.
- `aiohttp` is the standard asyncio HTTP library in the Python ecosystem and
  has no transitive dependency conflicts with pysnmp.

---

## Component Map: New vs Modified

| Component | File | Status | Nature of Change |
|-----------|------|--------|-----------------|
| `SCENARIOS` dict | `e2e_simulator.py` | New (inline) | Module-level dict: name → per-OID values |
| `_active_scenario` | `e2e_simulator.py` | New (inline) | Module-level string variable |
| `DynamicInstance` callbacks | `e2e_simulator.py` | Modified | Closures read `_active_scenario` instead of frozen value |
| HTTP server coroutine | `e2e_simulator.py` | New | `start_http_server()` using aiohttp AppRunner |
| HTTP request handlers | `e2e_simulator.py` | New | `handle_set_scenario`, `handle_get_scenario`, `handle_reset_scenario` |
| `requirements.txt` | `simulators/e2e-sim/requirements.txt` | Modified | Add `aiohttp` |
| K8s Deployment (HTTP port) | `deploy/k8s/simulators/e2e-sim-deployment.yaml` | Modified | Add `containerPort: 8080` |
| K8s Service (HTTP port) | `deploy/k8s/simulators/e2e-sim-deployment.yaml` | Modified | Add TCP/8080 port to Service |
| `lib/simulator.sh` | `tests/e2e/lib/simulator.sh` | New | `curl` wrappers: `set_scenario`, `reset_scenario`, `get_active_scenario` |
| `run-all.sh` | `tests/e2e/run-all.sh` | Modified | Source `simulator.sh`; add `start_port_forward e2e-simulator 8080 8080` |
| Tenant fixture files | `tests/e2e/fixtures/tenant-eval-*.yaml` | New | Per-test-scenario `simetra-tenants` ConfigMaps |
| Scenario scripts | `tests/e2e/scenarios/29-*.sh` and higher | New | Tenant evaluation test scenarios |
| OID map ConfigMap | `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml` | Possibly extended | Only if new scenarios need OIDs beyond existing 7 mapped |

---

## Scenario Registry Data Model

The registry lives in `e2e_simulator.py` as a plain Python dict. Every scenario
defines the return value for all 7 mapped OIDs. Unmapped OIDs are excluded
because they are intentionally constant for the "unmapped OID" test
(scenario 15).

```python
# Conceptual structure — lives in e2e_simulator.py

SCENARIOS = {
    "default": {
        "gauge_test":     42,
        "integer_test":   100,
        "counter32_test": 5000,
        "counter64_test": 1_000_000,
        "timeticks_test": 360_000,
        "info_test":      "E2E-TEST-VALUE",
        "ip_test":        "10.0.0.1",
    },
    # Tenant evaluation scenarios define values that exercise specific
    # threshold / routing paths. Example:
    "tenant-eval-threshold-breach": {
        "gauge_test":     0,       # triggers configured threshold
        "integer_test":   100,
        "counter32_test": 5000,
        "counter64_test": 1_000_000,
        "timeticks_test": 360_000,
        "info_test":      "E2E-TEST-VALUE",
        "ip_test":        "10.0.0.1",
    },
}

_active_scenario = "default"
```

Design rules:
1. Every scenario must define all 7 mapped OID labels. A missing key causes a
   `KeyError` in `getValue()` at poll time, which the SNMP engine converts to a
   `noSuchObject` response — indistinguishable from a genuine OID resolution
   failure in collector logs.
2. The `"default"` scenario must reproduce the current static values exactly so
   existing scenarios 01–28 pass without modification.
3. Scenario definitions are code, not config. Adding a new scenario requires a
   Dockerfile rebuild and image push, which is intentional — test scenarios are
   versioned with the code.

### DynamicInstance callback rewiring

Currently frozen:

```python
DynamicInstance(oid_tuple, (0,), syntax_cls(), lambda v=static_value: v)
```

Replace with a factory function that reads the active scenario at call time:

```python
def make_getter(label):
    def getter():
        return SCENARIOS[_active_scenario][label]
    return getter

# In the OID registration loop:
symbols[f"instance_{safe_label}"] = DynamicInstance(
    oid_tuple, (0,), syntax_cls(), make_getter(label)
)
```

`make_getter` captures `label` (a string) by value at registration time. At GET
time, `getter()` reads `_active_scenario` from the module's global namespace and
looks up the current scenario dict. This is safe because the asyncio event loop
is single-threaded: `getter()` (called by the SNMP engine) and the HTTP handler
(which writes `_active_scenario`) cannot execute concurrently.

---

## HTTP Server Integration

### Startup sequence

The critical constraint: `open_dispatcher()` blocks the event loop. The HTTP
server must be started and bound before that call.

```python
async def start_http_server():
    app = aiohttp.web.Application()
    app.router.add_get("/scenario", handle_get_scenario)
    app.router.add_post("/scenario", handle_set_scenario)
    app.router.add_post("/scenario/reset", handle_reset_scenario)
    runner = aiohttp.web.AppRunner(app)
    await runner.setup()
    site = aiohttp.web.TCPSite(runner, "0.0.0.0", 8080)
    await site.start()
    log.info("HTTP control server listening on 0.0.0.0:8080")

def main():
    loop = asyncio.get_event_loop()

    # Start HTTP before handing loop to pysnmp
    loop.run_until_complete(start_http_server())

    tasks = [
        loop.create_task(supervised_task("valid_trap_loop", valid_trap_loop)),
        loop.create_task(supervised_task("bad_community_trap_loop", bad_community_trap_loop)),
    ]

    # ... signal handler setup unchanged ...

    snmpEngine.open_dispatcher()   # blocking; HTTP server is live inside this loop
```

### HTTP API

All endpoints return JSON. Authentication is not needed — the API is
cluster-internal and only reachable via port-forward during tests.

**GET /scenario**
```
Response 200: {"active": "default", "available": ["default", "tenant-eval-..."]}
```

**POST /scenario**
```
Request:  {"name": "tenant-eval-threshold-breach"}
Response 200: {"active": "tenant-eval-threshold-breach"}
Response 404: {"error": "unknown scenario", "available": [...]}
```

**POST /scenario/reset**
```
Response 200: {"active": "default"}
```

`reset` is a dedicated endpoint rather than `POST /scenario {"name":"default"}`
to allow test cleanup scripts to reset without knowing the scenario name and
without risk of a typo in the default name constant.

### Kubernetes changes

Add a TCP port to both the Deployment and the Service in
`deploy/k8s/simulators/e2e-sim-deployment.yaml`:

```yaml
# In container spec:
ports:
- containerPort: 161
  name: snmp
  protocol: UDP
- containerPort: 8080
  name: http
  protocol: TCP

# In Service spec:
ports:
- name: snmp
  port: 161
  targetPort: snmp
  protocol: UDP
- name: http
  port: 8080
  targetPort: http
  protocol: TCP
```

The test runner reaches this via port-forward (same pattern as Prometheus):

```bash
start_port_forward e2e-simulator 8080 8080
```

---

## Test Script Orchestration Flow

### New library: `tests/e2e/lib/simulator.sh`

Follows the same sourced-library pattern as `kubectl.sh` and `prometheus.sh`.
The runner sources it once; all scenario scripts call its functions directly.

```bash
SIM_URL="http://localhost:8080"

set_scenario() {
    local name="$1"
    curl -sf -X POST "${SIM_URL}/scenario" \
        -H "Content-Type: application/json" \
        -d "{\"name\": \"${name}\"}" > /dev/null || {
        log_error "Failed to set scenario: ${name}"
        return 1
    }
    log_info "Simulator scenario: ${name}"
}

reset_scenario() {
    curl -sf -X POST "${SIM_URL}/scenario/reset" > /dev/null || true
    log_info "Simulator scenario reset to default"
}

get_active_scenario() {
    curl -sf "${SIM_URL}/scenario" | jq -r '.active'
}
```

### Five-phase scenario pattern

New tenant evaluation scenario scripts follow this structure:

```
Phase 1: SETUP
  - Snapshot ConfigMaps that will be mutated
    (simetra-tenants, possibly simetra-devices)
  - Apply test-specific fixture YAML
  - Call set_scenario for the simulator value set under test
  - If tenant config change requires deployment restart:
      kubectl rollout restart deployment/snmp-collector -n simetra
      kubectl rollout status deployment/snmp-collector -n simetra --timeout=90s

Phase 2: STABILIZE
  - SnapshotJob fires every 15s by default.
    Wait at minimum 2 × 15s = 30s for the evaluation cycle to run.
  - Prometheus scrapes every 15s; add ~15s for metric propagation.
  - Recommended minimum: poll_until for any detectable signal, plus
    sleep 15 for the scrape propagation gap.
  - Total practical wait without restart: ~45s
  - Total practical wait with deployment restart: ~90s rollout + 45s

Phase 3: ASSERT
  - Query Prometheus for expected metric values and labels
  - Check collector pod logs for expected log lines
  - Use existing assert_delta_gt / assert_exists helpers from common.sh

Phase 4: TEARDOWN
  - reset_scenario (HTTP POST /scenario/reset)
  - restore_configmap for each mutated ConfigMap
  - kubectl rollout restart if the deployment was touched
  - These run unconditionally (not gated on pass/fail)

Phase 5: VERIFY CLEAN (optional but recommended)
  - Confirm Prometheus metrics return to pre-test baseline
  - Prevents scenario state from contaminating subsequent scenarios
  - Skip if the cost of waiting is not justified by isolation risk
```

### Cleanup on early exit

The sourced-script pattern means an exit from a scenario script exits the entire
runner (`set -euo pipefail` propagates). The simulator is left in whatever
scenario was active. Add a `trap` at the top of every scenario that calls
`set_scenario`:

```bash
# At top of scenario file
_sim_cleanup() { reset_scenario || true; }
trap _sim_cleanup EXIT
```

This fires on both normal completion and early exit. ConfigMap restore is
handled by the same pattern already used in scenarios 18–28 (they save on entry
and restore on exit or failure).

### Concrete example: single-tenant evaluation scenario

```bash
# 29-tenant-eval-single.sh

FIXTURES_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fixtures"

# Cleanup trap
_cleanup() {
    reset_scenario || true
    if [ -f "$FIXTURES_DIR/.original-tenants-configmap.yaml" ]; then
        restore_configmap "$FIXTURES_DIR/.original-tenants-configmap.yaml" || true
    fi
}
trap _cleanup EXIT

# Phase 1: Setup
save_configmap "simetra-tenants" "simetra" \
    "$FIXTURES_DIR/.original-tenants-configmap.yaml"
kubectl apply -f "$FIXTURES_DIR/tenant-eval-single.yaml"
set_scenario "tenant-eval-threshold-breach"

# Phase 2: Stabilize — 2 SnapshotJob cycles + scrape window
log_info "Waiting 45s for SnapshotJob cycles and Prometheus scrape..."
sleep 45

# Phase 3: Assert
METRIC="snmp_tenantvector_routed_total"
BEFORE=$(snapshot_counter "$METRIC" "")
poll_until 60 5 "$METRIC" "" "$BEFORE" || true
AFTER=$(query_counter "$METRIC" "")
DELTA=$((AFTER - BEFORE))
assert_delta_gt "$DELTA" 0 \
    "Routing counter increments with single tenant" \
    "$(get_evidence "$METRIC" "")"
```

---

## Config Artifact Organization

### Tenant fixture files

New fixtures use a `tenant-eval-` prefix to distinguish them from the existing
device/OID mutation fixtures:

```
tests/e2e/fixtures/
  tenant-eval-single.yaml        # 1 tenant, minimal metric set
  tenant-eval-two-priority.yaml  # 2 tenants at different priorities
  tenant-eval-high-count.yaml    # 3+ tenants, stress test for group ordering
```

Each fixture is a **complete** `simetra-tenants` ConfigMap with all required
fields. Do not use partial overrides — the watcher replaces the full tenant list
on any ConfigMap change.

The tenants in these fixtures must reference device IPs and metric names that
are resolvable with the existing `simetra-devices` and `simetra-oid-metric-map`
ConfigMaps, or provide matching fixture overrides for those too.

### Simulator scenario definitions

Scenarios are defined inline in `e2e_simulator.py`, not in external files. This
is deliberate: test scenarios are code. The `SCENARIOS` dict is versioned in git
alongside the simulator source. Adding a new scenario requires a rebuild of the
simulator image and a pod restart (same as any other simulator code change).

### OID map extensions

If new tenant evaluation scenarios require OID values beyond the existing 7
mapped e2e OIDs, extend `simetra-oid-metric-map.yaml` with additional entries
under `1.3.6.1.4.1.47477.999.1.x.0` and add corresponding entries to the
device's poll list in `simetra-devices.yaml`. The simulator's `SCENARIOS` dict
must define values for those labels in every scenario entry.

Do not add OIDs outside the `.999.` subtree to e2e scenarios — the e2e device
community string `Simetra.E2E-SIM` routes only to that device, and mixing
with real NPB/OBP OIDs under `.100.` or `.10.` would pollute existing tests.

---

## Suggested Build Order

Dependencies are explicit. Each step must be complete and validated before the
next begins to avoid blocked work.

**Step 1 — Simulator: Add scenario state and callback rewiring**

Modify `simulators/e2e-sim/e2e_simulator.py`:
1. Define `SCENARIOS` dict with a `"default"` entry that matches current static
   values exactly.
2. Add `_active_scenario = "default"` module variable.
3. Replace frozen `lambda v=static_value: v` closures with `make_getter(label)`
   factory function.

This step is zero-risk: the behavior is identical unless `_active_scenario` is
changed. Verify by running scenario 11 (`gauge-labels-e2e-sim`) — it asserts
exact OID values that must still be `42`, `100`, etc.

**Step 2 — Simulator: Add HTTP server**

Continue in `e2e_simulator.py`:
4. Add `aiohttp` to `requirements.txt`.
5. Implement `start_http_server()` coroutine.
6. Implement the three HTTP handlers.
7. Call `loop.run_until_complete(start_http_server())` in `main()` before
   `open_dispatcher()`.

Modify `deploy/k8s/simulators/e2e-sim-deployment.yaml`:
8. Add `containerPort: 8080` and TCP/8080 Service port.

Rebuild the image, push, and restart the e2e-simulator pod. Verify:
- `curl -sf http://localhost:8080/scenario` (via port-forward) returns
  `{"active": "default", ...}`.
- Scenario 11 still passes (default OID values unchanged).

**Step 3 — Test library: `simulator.sh`**

New file: `tests/e2e/lib/simulator.sh`
9. Implement `set_scenario`, `reset_scenario`, `get_active_scenario`.

Modify `tests/e2e/run-all.sh`:
10. Add `source "$SCRIPT_DIR/lib/simulator.sh"` after existing lib sources.
11. Add `start_port_forward e2e-simulator 8080 8080` after the Prometheus
    port-forward.

Verify: `run-all.sh` still passes all 28 existing scenarios unchanged.

**Step 4 — Tenant fixtures**

New YAML files in `tests/e2e/fixtures/`:
12. Create one fixture per distinct tenant topology needed by planned scenarios.
13. Verify each fixture parses correctly with `kubectl apply --dry-run=client`.

This step can be done in parallel with Step 3.

**Step 5 — Scenario scripts**

New files `tests/e2e/scenarios/29-*.sh` and higher:
14. One script per E2E scenario.
15. Each script follows the five-phase pattern (setup, stabilize, assert,
    teardown, verify-clean).
16. Each script that calls `set_scenario` includes a `trap _cleanup EXIT`.

Run `tests/e2e/run-all.sh` end-to-end after each new scenario is added to
catch interference with existing scenarios before accumulation.

---

## Anti-Patterns to Avoid

### Threading the HTTP server

Do not use `threading.Thread` to run Flask or any WSGI framework alongside
pysnmp. The SNMP engine's asyncio event loop is not thread-safe. Writing
`_active_scenario` from a Flask thread while `getValue()` is executing in the
asyncio thread would be a data race (even though CPython's GIL reduces the
practical risk, relying on GIL behavior for correctness is fragile). Use
aiohttp.web on the existing event loop.

### Using `sleep` as the sole wait mechanism

`sleep 30` passes today because the cluster is healthy and timings are
predictable. A slow node or a rescheduled pod can make a fixed sleep produce
false failures. Use `poll_until` for any condition that Prometheus can signal,
and reserve `sleep` for the scrape propagation gap (which has no queryable
signal).

### Leaving the simulator in a non-default scenario on test failure

If a scenario script exits early via `set -e`, the simulator retains the active
scenario. The next scenario runs against wrong OID values and fails for an
unrelated reason. Always add `trap _cleanup EXIT` in every scenario that calls
`set_scenario`.

### Partial scenario definitions

If `SCENARIOS["my-scenario"]` omits a key, `make_getter(label)()` raises
`KeyError`. The SNMP engine converts this to `noSuchObject` in the GET response.
The collector logs an OID resolution failure with no indication that the
simulator is misconfigured. Always define all 7 mapped OID labels in every
scenario entry.

### Modifying ConfigMaps without saving first

Scenarios 18–27 all follow the `save_configmap` / `restore_configmap` pattern.
Tenant evaluation scenarios must do the same for `simetra-tenants`. A missing
restore leaves all subsequent scenarios running against the test tenant topology,
which changes routing behavior and invalidates all later counter assertions.

### Defining test OIDs outside the `.999.` subtree

The e2e device community string `Simetra.E2E-SIM` and its device entry in
`simetra-devices` point to `e2e-simulator.simetra.svc.cluster.local`. Using
OIDs from the NPB (`.100.`) or OBP (`.10.`) prefixes in e2e simulator scenarios
would require that the e2e device poll those OIDs — contaminating the poll log
and metric export for all NPB/OBP assertions.

---

## Sources

All findings are from direct inspection of:
- `simulators/e2e-sim/e2e_simulator.py` (full file read)
- `simulators/e2e-sim/requirements.txt`
- `simulators/e2e-sim/Dockerfile`
- `deploy/k8s/simulators/e2e-sim-deployment.yaml`
- `deploy/k8s/snmp-collector/simetra-devices.yaml`
- `deploy/k8s/snmp-collector/simetra-tenants.yaml`
- `deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml`
- `tests/e2e/run-all.sh`
- `tests/e2e/lib/common.sh`, `kubectl.sh`, `prometheus.sh`
- `tests/e2e/scenarios/01-poll-executed.sh`, `11-gauge-labels-e2e-sim.sh`,
  `28-tenantvector-routing.sh`

Confidence: HIGH for all integration points — derived from code, not inference.

The aiohttp `AppRunner` + `TCPSite` pattern for integrating with an existing
event loop is the documented approach in the aiohttp library. The specific
aiohttp version should be determined at implementation time by checking current
aiohttp release compatibility with Python 3.12 — the API has been stable since
aiohttp 3.x and no breaking changes are expected, but the version pin in
`requirements.txt` should be explicit.
