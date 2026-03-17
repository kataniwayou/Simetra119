# Phase 51: Simulator HTTP Control Endpoint - Research

**Researched:** 2026-03-17
**Domain:** Python asyncio / aiohttp / pysnmp agent-side MIB / K8s port exposure
**Confidence:** HIGH

---

## Summary

Phase 51 adds an HTTP scenario control endpoint (aiohttp 3.13.3) to the existing pysnmp 7.1.22 asyncio simulator. The simulator's main loop calls `snmpEngine.open_dispatcher()` which blocks the asyncio event loop. The correct integration pattern is to start the aiohttp AppRunner/TCPSite as a non-blocking awaitable coroutine (`loop.run_until_complete(start_http_server())`) **before** calling `open_dispatcher()`. This is a hard ordering constraint already documented in STATE.md.

The simulator has 9 registered OIDs across two subtrees (`.999.1.x` mapped, `.999.2.x` unmapped). Six new OIDs must be added in a new subtree `.999.4.x` for test-purpose isolation. Scenario switching is implemented by replacing a module-level `_active_scenario` dict that `DynamicInstance.getValue()` callbacks read from. The command target OID uses a writable `DynamicInstance` subclass that overrides `writeCommit` to store the SET value and serves it on subsequent GET.

The collector's `MetricPollJob.DispatchResponseAsync` explicitly skips `NoSuchObject`, `NoSuchInstance`, and `EndOfMibView` varbinds with a Debug-level log — it does NOT throw, does NOT trigger `RecordFailure`, and does NOT increment `snmp_poll_unreachable_total`. This makes `noSuchObject` the ideal staleness error type: the collector silently skips the stale OID while continuing to process other OIDs in the same response, which is exactly the per-OID staleness requirement.

**Primary recommendation:** Use `noSuchObject` for staleness. Use `aiohttp.web.AppRunner` + `TCPSite` started via `loop.run_until_complete()` before `open_dispatcher()`. Place all 6 new OIDs in subtree `.999.4.x`. Use a flat `dict[str, Any]` per scenario keyed by OID string.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| aiohttp | 3.13.3 | HTTP server on port 8080 | Already identified in STATE.md; AppRunner/TCPSite integrates with asyncio without blocking |
| pysnmp | 7.1.22 | SNMP agent | Already in use; `DynamicInstance` pattern already established |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| asyncio (stdlib) | Python 3.12 | Event loop integration | Already used; `loop.run_until_complete()` is the bridge from sync to async startup |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| aiohttp AppRunner | flask + threading | Introduces thread-safety concerns with pysnmp's asyncio loop; ruled out |
| noSuchObject for staleness | genErr | genErr would propagate as an SNMP error-status response (non-zero errorStatus), which causes SharpSnmpLib to raise an exception in the collector — this triggers `RecordFailure`. noSuchObject is a per-varbind sentinel that bypasses the exception path and is silently skipped |

**Installation:**
```bash
pip install aiohttp==3.13.3
```
Add to `simulators/e2e-sim/requirements.txt`:
```
pysnmp==7.1.22
aiohttp==3.13.3
```

---

## Architecture Patterns

### Recommended Project Structure

No new files needed. All changes are in:
```
simulators/e2e-sim/
├── e2e_simulator.py      # all changes here
├── requirements.txt      # add aiohttp==3.13.3
└── Dockerfile            # EXPOSE 8080/tcp alongside existing 161/udp

deploy/k8s/simulators/
└── e2e-sim-deployment.yaml   # add HTTP port 8080
```

### Pattern 1: Scenario Registry (flat dict per scenario)

**What:** A module-level dict `SCENARIOS` maps scenario name to a dict of `{oid_string: value_or_None}`. `None` values signal "return noSuchObject for this OID." The `_active_scenario` module variable is a reference to the currently-active dict. `DynamicInstance.getValue()` callbacks read from it.

**When to use:** Every OID registration; every scenario definition.

```python
# Source: design derived from existing DynamicInstance pattern in e2e_simulator.py

# Sentinel value meaning "return noSuchObject"
STALE = object()

SCENARIOS: dict[str, dict] = {
    "default": {
        f"{E2E_PREFIX}.1.1": 42,            # gauge_test -- existing
        f"{E2E_PREFIX}.1.2": 100,           # integer_test -- existing
        f"{E2E_PREFIX}.1.3": 5000,          # counter32_test -- existing
        f"{E2E_PREFIX}.1.4": 1000000,       # counter64_test -- existing
        f"{E2E_PREFIX}.1.5": 360000,        # timeticks_test -- existing
        f"{E2E_PREFIX}.1.6": "E2E-TEST-VALUE",  # info_test -- existing
        f"{E2E_PREFIX}.1.7": "10.0.0.1",   # ip_test -- existing
        f"{E2E_PREFIX}.2.1": 99,            # unmapped_gauge -- existing
        f"{E2E_PREFIX}.2.2": "UNMAPPED",    # unmapped_info -- existing
        # new test-purpose OIDs (subtree .999.4.x)
        f"{E2E_PREFIX}.4.1": 0,             # e2e_evaluate_metric (Gauge32)
        f"{E2E_PREFIX}.4.2": 0,             # e2e_resolved_metric (Gauge32)
        f"{E2E_PREFIX}.4.3": 0,             # e2e_bypass_status (Gauge32)
        f"{E2E_PREFIX}.4.4": 0,             # e2e_command_response (Integer32) -- also writable
        f"{E2E_PREFIX}.4.5": 0,             # e2e_agg_source_a (Gauge32)
        f"{E2E_PREFIX}.4.6": 0,             # e2e_agg_source_b (Gauge32)
    },
    "threshold_breach": {
        # ... inherits all defaults, overrides evaluate metric above threshold
        f"{E2E_PREFIX}.4.1": 90,
    },
    "stale": {
        # specific OIDs return noSuchObject; others continue normally
        f"{E2E_PREFIX}.4.1": STALE,
        f"{E2E_PREFIX}.4.2": STALE,
    },
}

_active_scenario: str = "default"
```

**Note on full-dict vs modifier design:** Use full-dict-per-scenario (all OIDs defined explicitly). This avoids "falls back to default" merge logic that can mask scenario isolation bugs in tests. Every scenario specifies every OID. A helper `_make_scenario(overrides)` can reduce repetition.

### Pattern 2: DynamicInstance getValue with noSuchObject

**What:** `DynamicInstance.getValue()` checks the active scenario for its OID. If the value is the `STALE` sentinel, it raises `NoSuchInstanceError` which pysnmp encodes as the `noSuchObject` / `noSuchInstance` sentinel in the varbind response.

**When to use:** All new test-purpose OIDs in stale scenario.

```python
# Source: pysnmp SMI internal behavior -- raising NoSuchInstanceError from getValue
# returns noSuchInstance sentinel in the SNMP GET response

from pysnmp.smi.error import NoSuchInstanceError

class DynamicInstance(MibScalarInstance):
    def __init__(self, oid_tuple, index_tuple, syntax, oid_str):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._oid_str = oid_str

    def getValue(self, name, **ctx):
        scenario = SCENARIOS[_active_scenario]
        val = scenario.get(self._oid_str, STALE)
        if val is STALE:
            raise NoSuchInstanceError(name=name, idx=(0,))
        return self.getSyntax().clone(val)
```

**Note on noSuchInstance vs noSuchObject:** `NoSuchInstanceError` is the correct exception for scalar instances (the `.0` suffix). The collector checks for both `NoSuchObject` and `NoSuchInstance` type codes and silently skips both. Either works for the staleness requirement; `NoSuchInstanceError` is more semantically correct for scalar instances.

### Pattern 3: Writable Command Target OID

**What:** The command target OID must accept SNMP SET (to prove the SET round-trip) and return the last-set value on GET. This requires overriding `writeCommit` in a separate subclass. The `SetCommandResponder` is already registered in the existing simulator.

**When to use:** Only for `e2e_command_response` OID (`.999.4.4.0`).

```python
# Source: pysnmp community documentation on writeCommit/cbFun pattern
# CRITICAL: always call cbFun; omitting it stalls the pysnmp state machine

class WritableDynamicInstance(DynamicInstance):
    """DynamicInstance that also accepts SNMP SET, storing the value in the scenario."""

    def writeTest(self, varBind, **ctx):
        cbFun = ctx['cbFun']
        cbFun(varBind, **ctx)  # always call cbFun

    def writeCommit(self, varBind, **ctx):
        cbFun = ctx['cbFun']
        name, value = varBind
        # Update current scenario value so subsequent GET reflects the SET
        SCENARIOS[_active_scenario][self._oid_str] = value.prettyPrint()
        cbFun(varBind, **ctx)  # always call cbFun
```

**Note on MibScalar maxAccess for SET:** The `MibScalar` object (not instance) must be registered with `setMaxAccess("readwrite")` for the VACM to allow SET on that OID. Without this, the VACM returns `notWritable` before the instance's `writeCommit` is ever called.

```python
# For the command target OID, register the scalar as readwrite:
symbols["scalar_e2e_command_response"] = MibScalar(oid_tuple, Integer32()).setMaxAccess("readwrite")
```

### Pattern 4: aiohttp AppRunner Startup (Critical Order)

**What:** aiohttp HTTP server must be started as a non-blocking coroutine **before** `open_dispatcher()`. `open_dispatcher()` calls `loop.run_forever()` internally, so anything started after it never runs until the dispatcher exits.

**When to use:** In `main()`, before `snmpEngine.open_dispatcher()`.

```python
# Source: aiohttp 3.13.3 official docs — Application Runners
# https://docs.aiohttp.org/en/stable/web_advanced.html#application-runners

from aiohttp import web

async def post_scenario(request: web.Request) -> web.Response:
    name = request.match_info["name"]
    global _active_scenario
    if name not in SCENARIOS:
        raise web.HTTPNotFound(
            reason=f"Unknown scenario: {name!r}. Valid: {list(SCENARIOS)}"
        )
    _active_scenario = name
    log.info("Scenario switched to: %s", name)
    return web.json_response({"scenario": name})

async def get_scenario(request: web.Request) -> web.Response:
    return web.json_response({"scenario": _active_scenario})

async def get_scenarios(request: web.Request) -> web.Response:
    return web.json_response({"scenarios": list(SCENARIOS)})

async def start_http_server() -> web.AppRunner:
    app = web.Application()
    app.router.add_post("/scenario/{name}", post_scenario)
    app.router.add_get("/scenario", get_scenario)
    app.router.add_get("/scenarios", get_scenarios)
    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, "0.0.0.0", 8080)
    await site.start()
    log.info("HTTP control endpoint listening on 0.0.0.0:8080")
    return runner

def main():
    # ...existing setup...
    loop = asyncio.get_event_loop()

    # CRITICAL: start HTTP server BEFORE open_dispatcher()
    runner = loop.run_until_complete(start_http_server())

    # ...create trap tasks...
    tasks = [...]

    def _shutdown(sig_name):
        log.info("Received %s -- shutting down gracefully", sig_name)
        for t in tasks:
            t.cancel()
        loop.run_until_complete(runner.cleanup())  # clean aiohttp shutdown
        snmpEngine.close_dispatcher()

    # ...signal handlers...
    snmpEngine.open_dispatcher()  # blocks here until shutdown
```

### Pattern 5: HTTP API Endpoints (recommended)

Include `GET /scenarios` to aid test script debugging (lists available scenario names). Include `404` for unknown scenario in `POST /scenario/{name}`. Use JSON for all responses for jq-parseability.

| Method | Path | Response | Purpose |
|--------|------|----------|---------|
| POST | `/scenario/{name}` | `{"scenario": "<name>"}` | Switch active scenario |
| GET | `/scenario` | `{"scenario": "<name>"}` | Query current scenario |
| GET | `/scenarios` | `{"scenarios": ["default", ...]}` | List available scenarios |

### Anti-Patterns to Avoid

- **Starting aiohttp after `open_dispatcher()`:** `open_dispatcher()` calls `loop.run_forever()` internally; any awaitable started after it will not execute until the dispatcher stops. This would make the HTTP endpoint permanently unreachable.
- **Using `web.run_app()`:** `run_app()` calls `loop.run_forever()` itself, blocking before `open_dispatcher()`. Never use it.
- **Using `genErr` for staleness:** `genErr` produces a non-zero SNMP `errorStatus` in the response PDU. SharpSnmpLib raises an exception on non-zero errorStatus, which causes the collector to call `RecordFailure` and potentially mark the device unreachable. This would break existing E2E tests.
- **Omitting `cbFun` call in `writeCommit`/`writeTest`:** pysnmp's internal SET state machine stalls without the callback — the SNMP SET request will time out on the sender side.
- **Modifying `SCENARIOS["default"]` for scenario switching:** Mutating the default dict means the simulator cannot return to a clean default without a restart. Keep `SCENARIOS["default"]` immutable; switching scenarios only changes `_active_scenario`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| HTTP routing with path params | Custom regex URL router | `aiohttp.web.RouteTableDef` or `app.router.add_*` | Path parameter extraction, error handling, already in aiohttp |
| JSON serialization | Manual `json.dumps` in responses | `web.json_response(dict)` | Sets Content-Type header, handles encoding correctly |
| SNMP error sentinels | Custom exception type | `pysnmp.smi.error.NoSuchInstanceError` | This is what pysnmp encodes into the SNMP protocol NoSuchInstance sentinel |
| asyncio+aiohttp integration | Thread-based HTTP server | AppRunner/TCPSite pattern | Thread-based approach introduces mutex requirements around all pysnmp MIB state |

---

## Common Pitfalls

### Pitfall 1: HTTP Server Started After open_dispatcher()

**What goes wrong:** HTTP endpoint never accepts connections; kubectl port-forward to 8080 hangs; `POST /scenario/{name}` times out permanently.
**Why it happens:** `open_dispatcher()` calls `loop.run_forever()` which blocks until the dispatcher is closed. Any code after it is dead until shutdown.
**How to avoid:** `loop.run_until_complete(start_http_server())` must be called before `snmpEngine.open_dispatcher()`.
**Warning signs:** `ConnectionRefusedError` on `curl http://localhost:8080/scenario`; HTTP port shows no listener in `ss -tlnp`.

### Pitfall 2: genErr Instead of noSuchObject/noSuchInstance for Staleness

**What goes wrong:** Collector marks E2E-SIM unreachable after the stale scenario is activated. The existing E2E scenarios that run concurrently (e.g., scenario 01 `snmp_poll_executed`) fail or produce incorrect results.
**Why it happens:** `genErr` is an SNMP PDU-level `errorStatus` (non-zero integer) not a per-varbind sentinel. SharpSnmpLib raises an exception when errorStatus != 0; this exception is caught by `MetricPollJob`'s outer catch block which calls `RecordFailure`.
**How to avoid:** Raise `NoSuchInstanceError` from `DynamicInstance.getValue()`. The collector's `DispatchResponseAsync` explicitly checks `SnmpType.NoSuchObject or NoSuchInstance or EndOfMibView` and issues a `LogDebug` + `continue` — no failure recording.
**Warning signs:** `snmp_poll_unreachable_total` counter increments after scenario switch to `stale`.

### Pitfall 3: Missing maxAccess("readwrite") on MibScalar for Command OID

**What goes wrong:** SNMP SET to the command target OID returns `notWritable` error; `writeCommit` is never called; `CommandWorkerService.SetAsync` receives an error response.
**Why it happens:** The VACM checks `maxAccess` on the `MibScalar` object before dispatching to the instance's write methods. Default is `read-only`.
**How to avoid:** Register `MibScalar(...).setMaxAccess("readwrite")` for the command target OID.
**Warning signs:** `Lextm.SharpSnmpLib.ErrorException: notWritable` in collector logs.

### Pitfall 4: Omitting cbFun Call in writeCommit/writeTest

**What goes wrong:** pysnmp SET state machine stalls; SNMP SET request times out at the client (collector's `CommandWorkerService`); timeout CTS fires; `snmp_command_failed_total` increments.
**Why it happens:** pysnmp uses a callback continuation pattern internally; the state machine only advances when `cbFun` is called.
**How to avoid:** Always call `cbFun(varBind, **ctx)` at the end of `writeTest` and `writeCommit`, even when indicating an error.
**Warning signs:** SET operations always time out; no exceptions thrown by the simulator.

### Pitfall 5: OID Subtree Collision with Existing Registered OIDs

**What goes wrong:** pysnmp raises a MIB tree conflict during startup; simulator fails to start.
**Why it happens:** OIDs `999.1.x` (mapped), `999.2.x` (unmapped), and `999.3.x` (trap OID) are already in use.
**How to avoid:** Place all 6 new test OIDs in subtree `.999.4.x`.
**Warning signs:** `SmiError` or `MibBuilder` exception on startup.

### Pitfall 6: Port 8080 K8s Service Missing

**What goes wrong:** `kubectl port-forward service/e2e-simulator 8080:8080` fails with "no port mapping found."
**Why it happens:** The Service manifest only has the SNMP UDP port defined.
**How to avoid:** Add TCP port 8080 to both the Deployment `containerPort` list and the Service `ports` list.
**Warning signs:** `kubectl port-forward` error; `GET /scenario` unreachable from test scripts.

---

## Code Examples

### Complete aiohttp startup integration

```python
# Source: aiohttp 3.13.3 official docs
# https://docs.aiohttp.org/en/stable/web_advanced.html#application-runners

async def start_http_server() -> web.AppRunner:
    app = web.Application()
    app.router.add_post("/scenario/{name}", post_scenario)
    app.router.add_get("/scenario", get_scenario)
    app.router.add_get("/scenarios", get_scenarios)
    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, "0.0.0.0", 8080)
    await site.start()
    log.info("HTTP control endpoint listening on 0.0.0.0:8080")
    return runner
```

### POST handler with 404 for unknown scenario

```python
# Source: aiohttp 3.13.3 official docs
# https://docs.aiohttp.org/en/stable/web_quickstart.html

async def post_scenario(request: web.Request) -> web.Response:
    name = request.match_info["name"]
    global _active_scenario
    if name not in SCENARIOS:
        raise web.HTTPNotFound(
            reason=f"Unknown scenario: {name!r}. Valid: {sorted(SCENARIOS)}"
        )
    _active_scenario = name
    log.info("Scenario switched to: %s", name)
    return web.json_response({"scenario": name})
```

### DynamicInstance with noSuchInstance staleness

```python
# Source: pysnmp smi.error module; collector MetricPollJob.cs line 163 confirms
# NoSuchInstance is silently skipped (LogDebug + continue, no RecordFailure)

from pysnmp.smi.error import NoSuchInstanceError

class DynamicInstance(MibScalarInstance):
    def __init__(self, oid_tuple, index_tuple, syntax, oid_str):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._oid_str = oid_str

    def getValue(self, name, **ctx):
        val = SCENARIOS[_active_scenario].get(self._oid_str, STALE)
        if val is STALE:
            raise NoSuchInstanceError(name=name, idx=(0,))
        return self.getSyntax().clone(val)
```

### WritableDynamicInstance for command target OID

```python
# Source: pysnmp community docs on writeCommit/writeTest pattern
# CRITICAL: cbFun must always be called to avoid state machine stall

class WritableDynamicInstance(DynamicInstance):
    def writeTest(self, varBind, **ctx):
        ctx['cbFun'](varBind, **ctx)

    def writeCommit(self, varBind, **ctx):
        name, value = varBind
        SCENARIOS[_active_scenario][self._oid_str] = value
        ctx['cbFun'](varBind, **ctx)
```

### K8s Deployment port addition

```yaml
# Add alongside existing SNMP UDP port in e2e-sim-deployment.yaml
ports:
- containerPort: 161
  name: snmp
  protocol: UDP
- containerPort: 8080
  name: http-control
  protocol: TCP
```

### K8s Service port addition

```yaml
# Add alongside existing SNMP port in e2e-sim-deployment.yaml Service section
ports:
- name: snmp
  port: 161
  targetPort: snmp
  protocol: UDP
- name: http-control
  port: 8080
  targetPort: http-control
  protocol: TCP
```

### Dockerfile EXPOSE addition

```dockerfile
# Add alongside existing EXPOSE 161/udp
EXPOSE 161/udp
EXPOSE 8080/tcp
```

### curl verification pattern (for test scripts)

```bash
# Switch scenario
curl -s -X POST http://localhost:8080/scenario/threshold_breach | jq .
# {"scenario": "threshold_breach"}

# Verify current scenario
curl -s http://localhost:8080/scenario | jq .
# {"scenario": "threshold_breach"}

# List available scenarios
curl -s http://localhost:8080/scenarios | jq .
# {"scenarios": ["default", "threshold_breach", "stale"]}
```

---

## OID Inventory (Authoritative)

Existing OID subtrees that must not be touched:
- `.999.1.x` — 7 mapped OIDs (existing E2E tests depend on exact values)
- `.999.2.x` — 2 unmapped OIDs
- `.999.3.x` — trap OID (`TRAP_OID = f"{E2E_PREFIX}.3.1"`)

New test-purpose OIDs in subtree `.999.4.x`:

| Suffix | OID | Metric Name | SNMP Type | Purpose |
|--------|-----|-------------|-----------|---------|
| .999.4.1 | `1.3.6.1.4.1.47477.999.4.1` | `e2e_evaluate_metric` | Gauge32 | Threshold evaluation input |
| .999.4.2 | `1.3.6.1.4.1.47477.999.4.2` | `e2e_resolved_metric` | Gauge32 | General resolved metric |
| .999.4.3 | `1.3.6.1.4.1.47477.999.4.3` | `e2e_bypass_status` | Gauge32 | Bypass status (0=inline, 1=bypass) |
| .999.4.4 | `1.3.6.1.4.1.47477.999.4.4` | `e2e_command_response` | Integer32 | Command target — SET+GET round-trip |
| .999.4.5 | `1.3.6.1.4.1.47477.999.4.5` | `e2e_agg_source_a` | Gauge32 | ADV-01 aggregate source A |
| .999.4.6 | `1.3.6.1.4.1.47477.999.4.6` | `e2e_agg_source_b` | Gauge32 | ADV-01 aggregate source B |

**Rationale for Gauge32:** Gauge32 is the natural SNMP type for threshold testing because it represents a current level (not a monotonically increasing counter). The collector's `SelectTypeCode` uses `Gauge32` for `Sum` and `Mean` aggregates — matching the aggregate source OID types avoids type-coercion surprises.

**Rationale for Integer32 on command response:** The command map infrastructure uses `Integer32` for bypass commands (OBP `obp_set_bypass_Lx`). Using the same type confirms the E2E SET path handles Integer32 correctly end-to-end.

**Rationale for dedicated aggregate source OIDs:** Reusing existing OIDs (e.g., `.1.1` gauge_test) would couple aggregate test scenarios to existing E2E test values. Dedicated `.999.4.5` and `.999.4.6` OIDs let each scenario control aggregate source values independently without affecting scenario 11.

---

## Scenario Inventory (Recommended)

| Scenario Name | Purpose | Key Differences from Default |
|---------------|---------|------------------------------|
| `default` | Reproduces pre-HTTP simulator exactly | All 9 existing OIDs at their original values; 6 new OIDs at 0 |
| `threshold_breach` | Evaluate metric above threshold | `.4.1` set to value exceeding configured threshold |
| `threshold_clear` | Evaluate metric below threshold | `.4.1` set to safe value |
| `bypass_active` | Bypass status set | `.4.3 = 1` (bypass), `.4.2` reflects resolved state |
| `stale` | Specific OIDs return noSuchInstance | `.4.1` and `.4.2` return `STALE`; all other OIDs respond normally |

Note: Scenario names are implementation choices left to Claude's discretion per CONTEXT.md. The above names are recommended for clarity in test scripts.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Static lambda closures for OID values | `DynamicInstance` callbacks reading `_active_scenario` | Phase 51 | Enables runtime scenario switching without restart |
| No HTTP control endpoint | aiohttp AppRunner on port 8080 | Phase 51 | Test scripts can switch scenarios mid-test via curl |
| `web.run_app()` (blocking) | `AppRunner` + `TCPSite` (non-blocking) | aiohttp design | Essential for coexistence with pysnmp's `open_dispatcher()` |

**Deprecated/outdated patterns:**
- `loop.create_task()` for starting HTTP server: task scheduling only works after the loop is running; `run_until_complete()` is required for startup sequencing before `open_dispatcher()`.

---

## Port Collision Analysis

The collector's K8s deployments (`snmp-collector/deployment.yaml`, `production/deployment.yaml`) expose port 8080 for their health endpoints. However:

- These are separate Deployments in the `simetra` namespace
- K8s pod-level ports are per-pod (not cluster-wide)
- `kubectl port-forward pod/e2e-simulator-xxx 8080:8080` forwards to the simulator pod exclusively
- No port 8080 is currently defined in `deploy/k8s/simulators/e2e-sim-deployment.yaml`

**Conclusion:** No collision. Port 8080 on the e2e-simulator pod is free to use.

---

## Open Questions

1. **`writeCommit` value type for command response OID**
   - What we know: `varBind[1]` in `writeCommit` is a pysnmp `Integer32` object; `prettyPrint()` returns a string; raw object stored and returned as-is via `getSyntax().clone(val)` should work.
   - What's unclear: Whether storing the raw pysnmp `Integer32` object vs an int vs string in the scenario dict causes issues on subsequent `getValue()` calls.
   - Recommendation: Store the raw pysnmp type object (not converted to Python int) since `getSyntax().clone(val)` accepts same-type objects directly.

2. **`_active_scenario` thread-safety**
   - What we know: aiohttp runs on the same asyncio event loop as pysnmp; Python's GIL and asyncio's single-threaded cooperative scheduling mean simple assignment `_active_scenario = name` is atomic from the perspective of other coroutines.
   - What's unclear: Whether pysnmp's SNMP GET processing can be interrupted between scenario name read and OID value lookup (it cannot, due to asyncio cooperative scheduling).
   - Recommendation: No lock needed. A module-level string variable assignment is safe in single-threaded asyncio.

---

## Sources

### Primary (HIGH confidence)
- `simulators/e2e-sim/e2e_simulator.py` — existing DynamicInstance pattern, OID inventory, asyncio loop structure
- `src/SnmpCollector/Jobs/MetricPollJob.cs` lines 163-170 — confirms `NoSuchObject/NoSuchInstance/EndOfMibView` are silently skipped (LogDebug + continue), NOT RecordFailure
- `src/SnmpCollector/Pipeline/ISnmpClient.cs` — confirms `SetAsync` exists
- `src/SnmpCollector/Services/CommandWorkerService.cs` — confirms SET dispatch path and error handling
- `deploy/k8s/simulators/e2e-sim-deployment.yaml` — confirms no existing HTTP port
- [aiohttp 3.13.3 Application Runners docs](https://docs.aiohttp.org/en/stable/web_advanced.html#application-runners) — AppRunner/TCPSite startup pattern
- [aiohttp 3.13.3 Web Quickstart](https://docs.aiohttp.org/en/stable/web_quickstart.html) — route registration, path params, json_response, HTTPNotFound

### Secondary (MEDIUM confidence)
- [pysnmp agent MIB implementations (PySNMP 7.1 docs)](https://docs.lextudio.com/pysnmp/v7.1/examples/v3arch/asyncio/agent/cmdrsp/agent-side-mib-implementations) — getValue override pattern
- [pysnmp writeCommit/writeTest pattern (snmpresponder issue #2)](https://github.com/etingof/snmpresponder/issues/2) — cbFun must always be called; `context['error']` for validation failures

### Tertiary (LOW confidence — training data, not independently verified against pysnmp 7.1 source)
- `NoSuchInstanceError` import path: `from pysnmp.smi.error import NoSuchInstanceError` — assumed stable; verify at implementation time
- `MibScalar.setMaxAccess("readwrite")` API — consistent across pysnmp versions in search results but not verified against 7.1.22 source

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — aiohttp 3.13.3 confirmed in STATE.md; pysnmp 7.1.22 in requirements.txt
- Architecture: HIGH — startup order critical constraint confirmed in STATE.md; collector skip behavior confirmed in MetricPollJob.cs source
- OID inventory: HIGH — existing OIDs read directly from simulator source; new subtree `.999.4.x` derived from gap analysis
- Pitfalls: HIGH for pitfalls 1-3; MEDIUM for pitfalls 4-6 (pysnmp internals from secondary sources)
- Code examples: HIGH for aiohttp patterns (from official docs); MEDIUM for pysnmp write patterns (from community sources)

**Research date:** 2026-03-17
**Valid until:** 2026-04-17 (aiohttp and pysnmp are stable; K8s YAML format is stable)
