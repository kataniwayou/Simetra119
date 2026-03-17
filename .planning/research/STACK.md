# Technology Stack — E2E Simulator HTTP Control + Bash Test Scripts Milestone

**Project:** Simetra119 SNMP Collector
**Researched:** 2026-03-17
**Milestone scope:** HTTP scenario control endpoint for pysnmp E2E simulator + bash E2E test
scripts for SnapshotJob tenant evaluation
**Out of scope:** Re-researching pysnmp 7.1.22, Prometheus/PromQL querying, kubectl ConfigMap
management, SharpSnmpLib SET execution, Quartz SnapshotJob — all validated in prior milestones

---

## Executive Decision

**One new Python package: `aiohttp==3.13.3`.**

The HTTP control endpoint must coexist with pysnmp's asyncio event loop. aiohttp's
`AppRunner`/`TCPSite` API starts the HTTP server as a non-blocking background task inside
the same event loop before `snmpEngine.open_dispatcher()` takes over the loop. No threading,
no subprocess, no second process.

For bash test scripts: zero new tools. `curl`, `kubectl`, and `jq` already present in the
test environment cover all needed operations. A `poll_until_log` function is the only
structural addition required to `lib/`.

---

## The Central Constraint: open_dispatcher Is a Blocking Loop Entry

`snmpEngine.open_dispatcher()` is the existing simulator's final call. It calls
`loop.run_forever()` on the asyncio event loop internally. All asyncio tasks registered
before this call — including the trap loops via `loop.create_task()` — execute inside that
loop run. The SNMP agent itself is a transport registered with the same loop.

**Consequence:** The HTTP server must be started as an asyncio coroutine task before
`open_dispatcher()` is called. Once the loop is running, the HTTP server serves requests
concurrently with SNMP GET responses and trap loops, all on the same single-threaded loop.

**What does NOT work:**
- `web.run_app(app)` — this would call `loop.run_forever()` itself, conflicting with
  `open_dispatcher()`
- Running HTTP in a thread with `run_in_executor` — pysnmp's MibScalarInstance callbacks
  (`getValue`) execute on the asyncio loop thread; mutating the OID value dict from a thread
  requires a lock or thread-safe handoff that adds complexity for no gain
- A second process with `subprocess` — adds inter-process communication overhead and
  container complexity for an internal test tool

---

## Python Stack Additions

### New Package

| Package | Version | Purpose | Why |
|---------|---------|---------|-----|
| aiohttp | 3.13.3 | HTTP server for scenario control endpoint | Only asyncio-native Python HTTP server library with a non-blocking startup API (`AppRunner`/`TCPSite`) that integrates cleanly into an existing asyncio loop without `run_forever()` |

**aiohttp version verified:** 3.13.3 released 2026-01-03. Requires Python >=3.9. Supports
Python 3.12 (the simulator's runtime). Source: [pypi.org/project/aiohttp](https://pypi.org/project/aiohttp/)

**pysnmp version confirmed unchanged:** 7.1.22, released 2025-10-26, still current. No
version change required. Source: [pypi.org/project/pysnmp](https://pypi.org/project/pysnmp/)

### Alternatives Considered and Rejected

| Option | Why Rejected |
|--------|--------------|
| FastAPI (+ uvicorn) | Two packages (FastAPI + uvicorn ASGI server) for a two-endpoint internal test tool; uvicorn runs its own event loop which conflicts with `open_dispatcher()` |
| Flask (+ threading) | Blocking WSGI server; thread-safety of `getValue` callbacks requires locks; inconsistent with existing asyncio-only architecture |
| `http.server.ThreadingHTTPServer` in executor | stdlib `http.server` is explicitly not for production; requires `loop.run_in_executor()`; thread-to-asyncio value mutation needs explicit thread-safe coordination |
| Sanic | Also manages its own event loop; same conflict as uvicorn |
| Tornado | Heavy; tornado's loop management conflicts with pysnmp's dispatcher owning the loop |

aiohttp's `AppRunner`/`TCPSite` is the only mainstream option that hands loop control back
to the caller after setup. All others either block on their own `run_forever()` call or
require threading.

### Integration Pattern

The integration requires a single async setup function called before `snmpEngine.open_dispatcher()`:

```python
import aiohttp.web as web

# Scenario state — mutated by HTTP handler, read by DynamicInstance.getValue callbacks
_current_scenario: dict[str, Any] = {}   # keyed by OID string, value is the Python value

async def start_http_control(port: int = 8080) -> web.AppRunner:
    app = web.Application()
    app.router.add_post("/scenario", handle_set_scenario)
    app.router.add_get("/scenario", handle_get_scenario)

    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, "0.0.0.0", port)
    await site.start()
    log.info("HTTP control endpoint listening on :%d", port)
    return runner  # caller holds reference for cleanup
```

`AppRunner.setup()` and `TCPSite.start()` are coroutines that complete without blocking.
The site is registered as a socket on the existing event loop. Because `open_dispatcher()`
calls `loop.run_forever()`, the HTTP server will process requests in the same loop
concurrently with SNMP traffic.

Source: [aiohttp web_advanced docs](https://docs.aiohttp.org/en/stable/web_advanced.html) —
AppRunner described as: "for starting the application asynchronously... AppRunner exists."

### Scenario State Safety

The `DynamicInstance.getValue` callback and the aiohttp HTTP handler both execute on
the asyncio event loop thread (single-threaded). There is no concurrent mutation. A plain
`dict` is safe as scenario state — no locks needed.

This is the core correctness argument for same-loop over threading.

### requirements.txt After Change

```
pysnmp==7.1.22
aiohttp==3.13.3
```

### Dockerfile Change

`EXPOSE 161/udp` remains. Add `EXPOSE 8080` for the HTTP control port:

```dockerfile
EXPOSE 161/udp
EXPOSE 8080
```

No base image change. `python:3.12-slim` ships all aiohttp C extension build dependencies
via pip wheels (pre-built; no compiler needed).

---

## Scenario Registry Pattern (Python)

The scenario registry defines named scenarios as dicts mapping OID strings to return values.
This is pure Python — no new library needed.

```python
# Scenario definitions: name -> {oid_str: value, ...}
SCENARIOS: dict[str, dict[str, Any]] = {
    "baseline": {
        f"{E2E_PREFIX}.1.1": 42,      # gauge_test — below threshold
        f"{E2E_PREFIX}.1.2": 100,     # integer_test — nominal
        # ... all 9 OIDs
    },
    "tier2_trigger": {
        f"{E2E_PREFIX}.1.1": 999,     # gauge_test — violates threshold
        # remaining OIDs unchanged from baseline
        # ...
    },
    # further scenarios as needed
}
```

The `DynamicInstance` callbacks already use `lambda v=static_value: v`. To make them
scenario-aware, change to `lambda oid=oid_str: _current_scenario.get(oid, SCENARIOS["baseline"][oid])`.

The HTTP handler validates the scenario name against `SCENARIOS.keys()` and returns HTTP 400
for unknown names. No external schema validation library needed.

---

## Bash Test Script Additions

### New Library Function: poll_until_log

The existing `lib/prometheus.sh` has `poll_until` for metric counters. Log-based assertions
(tier flow, scenario transitions) need an analogous function in `lib/kubectl.sh`:

```bash
# Poll until a pattern appears in pod logs, or timeout.
# Usage: poll_until_log <timeout_s> <interval_s> <pod_label> <namespace> <since> <pattern>
# Returns 0 on match, 1 on timeout.
poll_until_log() {
    local timeout="$1" interval="$2" label="$3" ns="$4" since="$5" pattern="$6"
    local deadline
    deadline=$(( $(date +%s) + timeout ))

    while [ "$(date +%s)" -lt "$deadline" ]; do
        local pods
        pods=$(kubectl get pods -n "$ns" -l "$label" \
            -o jsonpath='{.items[*].metadata.name}' 2>/dev/null) || true
        for pod in $pods; do
            if kubectl logs "$pod" -n "$ns" --since="${since}s" 2>/dev/null \
                | grep -F "$pattern" > /dev/null 2>&1; then
                return 0
            fi
        done
        sleep "$interval"
    done
    return 1
}
```

Key detail: `grep -F` (fixed string, not regex) avoids escaping issues with the bracket
characters in log lines like `"Tenant {TenantId} priority=1 tier=2 — all resolved violated"`.
Use `-F` for exact log message fragments, regex only when needed.

### New Helper: sim_set_scenario

HTTP call to the simulator's control endpoint, used at the start of each tenant evaluation
scenario script:

```bash
SIM_HTTP_URL="${SIM_HTTP_URL:-http://localhost:8080}"

sim_set_scenario() {
    local scenario_name="$1"
    local response http_code
    response=$(curl -sf -w '\n%{http_code}' -X POST \
        -H "Content-Type: application/json" \
        -d "{\"scenario\": \"${scenario_name}\"}" \
        "${SIM_HTTP_URL}/scenario" 2>&1) || true
    http_code=$(echo "$response" | tail -1)
    if [ "$http_code" != "200" ]; then
        log_error "sim_set_scenario failed: scenario=${scenario_name} http=${http_code}"
        return 1
    fi
    log_info "Simulator scenario set: ${scenario_name}"
}
```

`SIM_HTTP_URL` is an environment variable so `run-all.sh` can set it via port-forward
(`kubectl port-forward svc/e2e-simulator 8080:8080`) alongside the existing Prometheus
port-forward.

### Bash Tools Used (no additions to install)

| Tool | Already Used | New Usage |
|------|-------------|-----------|
| `curl` | Prometheus queries | Simulator HTTP control (`sim_set_scenario`) |
| `kubectl` | Pod management, ConfigMaps, logs | Same; `poll_until_log` uses `kubectl logs` |
| `jq` | Prometheus response parsing | Scenario response body parsing (optional) |
| `grep -F` | Existing log grep patterns | Log polling in `poll_until_log` |
| `date +%s` | Timeout loops in `poll_until` | Same pattern in `poll_until_log` |

No `timeout`, `watch`, `stern`, or `kail` needed. Plain `kubectl logs --since` with a bash
polling loop is sufficient and consistent with the existing test framework.

### Scenario Script Structure for Tenant Evaluation Tests

Each new scenario script (e.g. `29-snapshot-tier2.sh`) follows this four-step structure:

```bash
# Step 1: Set simulator to trigger state
sim_set_scenario "tier2_trigger"
sleep 2   # allow one SnapshotJob cycle (15s interval) + small buffer

# Step 2: Wait for tier log evidence
SCENARIO_NAME="SnapshotJob evaluates tier-2 when resolved violated"
if poll_until_log 60 5 "app=snmp-collector" "simetra" 60 \
    "tier=2 — all resolved violated"; then
    record_pass "$SCENARIO_NAME" "tier=2 log found within 60s"
else
    record_fail "$SCENARIO_NAME" "tier=2 log not found within 60s"
fi

# Step 3: Validate counter via Prometheus
METRIC="snmp_snapshot_tier2_evaluated_total"
BEFORE=$(snapshot_counter "$METRIC" "")
# (counter increment already validated in step 2 via log; Prometheus confirms metric path)
assert_delta_gt "$(($(query_counter "$METRIC" "") - BEFORE))" 0 \
    "tier-2 counter increments" "$(get_evidence "$METRIC" "")"

# Step 4: Reset simulator to baseline
sim_set_scenario "baseline"
```

The `sleep 2` after `sim_set_scenario` is intentional: the SnapshotJob polls every 15s, so
the test must account for up to one full cycle before logs appear. `poll_until_log 60 5`
covers this with a 60s outer timeout.

---

## K8s Deployment Change

The `e2e-sim` Kubernetes Service currently exposes only UDP 161. A ClusterIP port for TCP
8080 must be added so test scripts can reach the HTTP endpoint via port-forward:

```yaml
# Add to e2e-simulator Service spec.ports:
- name: http-control
  port: 8080
  targetPort: 8080
  protocol: TCP
```

This is a manifest change only — no image rebuild beyond the `requirements.txt` and Python
code changes.

---

## What NOT to Add

| Omission | Rationale |
|----------|-----------|
| FastAPI / uvicorn | Two packages; uvicorn owns event loop, conflicts with `open_dispatcher()` |
| Flask | WSGI (blocking); thread-safety of MIB callbacks requires extra coordination |
| `pytest` / `unittest` for scenario tests | Bash scripts already established as the test framework; adding Python test runner for E2E orchestration creates dual-framework complexity |
| `httpx` or `requests` in simulator | Not needed; aiohttp.web is both server and (if needed) client |
| `asyncio.run_in_executor` for HTTP | Same-loop via aiohttp avoids all thread-safety questions for OID state mutation |
| Log scraping via `stern` or `kail` | Not available in all CI environments; plain `kubectl logs` is portable |
| `timeout` command wrapper | `deadline=$(( $(date +%s) + N ))` loop already established in codebase |
| `pydantic` for scenario schema validation | Dict key lookup against `SCENARIOS.keys()` is sufficient for a controlled test tool |

---

## Confidence Assessment

| Area | Confidence | Source |
|------|------------|--------|
| aiohttp 3.13.3 current version | HIGH | pypi.org/project/aiohttp verified 2026-03-17 |
| AppRunner/TCPSite non-blocking integration | HIGH | docs.aiohttp.org/en/stable/web_advanced.html |
| pysnmp open_dispatcher calls loop.run_forever() | MEDIUM | Observed behavior in existing simulator (tasks run inside open_dispatcher call); pysnmp asyncio dispatch.py confirms asyncio loop integration; direct source read blocked by 429 rate limit |
| Same-loop OID state safety (no locks needed) | HIGH | asyncio single-threaded execution guarantee; Python asyncio documentation |
| bash poll_until_log pattern correctness | HIGH | Mirrors poll_until in existing lib/prometheus.sh; kubectl logs --since is documented |
| grep -F for log pattern matching | HIGH | POSIX standard; avoids regex escaping for tier log messages |
| K8s Service TCP 8080 port addition | HIGH | Standard K8s Service manifest pattern |

---

## Sources

- [aiohttp PyPI page](https://pypi.org/project/aiohttp/) — version 3.13.3, Python >=3.9
- [aiohttp Web Server Advanced docs](https://docs.aiohttp.org/en/stable/web_advanced.html) — AppRunner/TCPSite pattern
- [pysnmp PyPI page](https://pypi.org/project/pysnmp/) — version 7.1.22 confirmed current
- [Python http.server docs](https://docs.python.org/3/library/http.server.html) — confirmed no async support
- [Python asyncio event loop docs](https://docs.python.org/3/library/asyncio-eventloop.html) — run_in_executor, single-threaded guarantee
- Existing codebase read directly:
  - `simulators/e2e-sim/e2e_simulator.py` — open_dispatcher call site, create_task pattern, DynamicInstance callbacks
  - `simulators/e2e-sim/requirements.txt` — current deps
  - `simulators/e2e-sim/Dockerfile` — base image
  - `tests/e2e/lib/common.sh`, `prometheus.sh`, `kubectl.sh` — existing utility patterns
  - `tests/e2e/scenarios/07-poll-recovered.sh`, `28-tenantvector-routing.sh` — established scenario structure

---

*Stack research for: E2E simulator HTTP control + SnapshotJob tenant evaluation test scripts*
*Researched: 2026-03-17*
