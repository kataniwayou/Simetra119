# Phase 20: Test Simulator - Research

**Researched:** 2026-03-09
**Domain:** pysnmp SNMP simulator, K8s deployment, OID design
**Confidence:** HIGH

## Summary

This phase builds a dedicated E2E test simulator (`e2e-sim`) using the exact same pysnmp 7.1.22 stack and patterns already proven in the OBP and NPB simulators. The simulator serves static OID values across all 6 SNMP types the collector handles (Integer32, Gauge32, Counter32, Counter64, TimeTicks, OctetString, IpAddress), plus deliberately unmapped OIDs for "Unknown" classification testing. It also sends periodic traps -- both valid (community `Simetra.E2E-SIM`) and invalid (community `BadCommunity`) -- to exercise the `trap_auth_failed` counter.

The codebase already contains two working simulators that establish every pattern needed: pysnmp engine setup, DynamicInstance for OID callbacks, trap sending via hlapi, K8s deployment with health probes, and signal handling. The E2E simulator is significantly simpler than either existing simulator because all values are static (no random walk, no state toggling).

**Primary recommendation:** Copy the OBP simulator structure, strip out all dynamic behavior (random walk, state toggling), use static return values for every OID, and add a second trap loop that sends bad-community traps on a separate interval.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| pysnmp | 7.1.22 | SNMP agent + trap sender | Exact version used by OBP + NPB simulators |
| python | 3.12-slim | Base Docker image | Matches existing Dockerfiles |

### Supporting
No additional libraries needed. The OBP simulator runs with pysnmp alone.

### Alternatives Considered
None. All decisions are locked -- reuse existing stack exactly.

**Installation:**
```
# requirements.txt
pysnmp==7.1.22
```

## Architecture Patterns

### Recommended Project Structure
```
simulators/e2e-sim/
    e2e_simulator.py       # Single-file simulator (~180 lines estimated)
    requirements.txt       # pysnmp==7.1.22
    Dockerfile             # python:3.12-slim pattern
deploy/k8s/simulators/
    e2e-sim-deployment.yaml  # Deployment + Service (matches obp-deployment.yaml pattern)
```

### Pattern 1: Static OID Registration (from OBP simulator)

**What:** Register MibScalar + DynamicInstance pairs for each OID, but with constant-return callbacks instead of mutable state lookups.
**When to use:** Always for this simulator -- all values are deterministic.
**Example:**
```python
# Source: simulators/obp/obp_simulator.py lines 123-158
from pysnmp.proto.api import v2c

class DynamicInstance(MibScalarInstance):
    def __init__(self, oid_tuple, index_tuple, syntax, get_value_fn):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._get_value_fn = get_value_fn

    def getValue(self, name, **ctx):
        return self.getSyntax().clone(self._get_value_fn())

# Static registration -- lambda returns constant
symbols[f"scalar_{name}"] = MibScalar(oid_tuple, syntax_cls())
symbols[f"instance_{name}"] = DynamicInstance(
    oid_tuple, (0,), syntax_cls(), lambda v=static_value: v
)
```

### Pattern 2: SNMP Engine Setup (from OBP simulator)

**What:** Low-level engine with community auth, VACM, and command responders.
**When to use:** Every simulator.
**Example:**
```python
# Source: simulators/obp/obp_simulator.py lines 103-114
snmpEngine = engine.SnmpEngine()
config.add_transport(snmpEngine, udp.DOMAIN_NAME,
    udp.UdpTransport().open_server_mode(("0.0.0.0", 161)))
config.add_v1_system(snmpEngine, "my-area", COMMUNITY)
config.add_vacm_user(snmpEngine, 2, "my-area", "noAuthNoPriv", (1, 3, 6, 1, 4, 1))
snmpContext = context.SnmpContext(snmpEngine)
cmdrsp.GetCommandResponder(snmpEngine, snmpContext)
cmdrsp.NextCommandResponder(snmpEngine, snmpContext)
cmdrsp.BulkCommandResponder(snmpEngine, snmpContext)
cmdrsp.SetCommandResponder(snmpEngine, snmpContext)
```

### Pattern 3: Dual Trap Loops (valid + bad community)

**What:** Two supervised_task instances -- one sending valid traps with `Simetra.E2E-SIM` community, one sending bad-community traps with `BadCommunity`.
**When to use:** This simulator specifically, for testing trap_auth_failed counter.
**Example:**
```python
async def valid_trap_loop():
    """Send valid trap with correct community string."""
    await asyncio.sleep(STARTUP_DELAY)
    while True:
        await asyncio.sleep(TRAP_INTERVAL)
        await send_trap(COMMUNITY)  # "Simetra.E2E-SIM"

async def bad_community_trap_loop():
    """Send trap with wrong community to trigger trap_auth_failed counter."""
    await asyncio.sleep(STARTUP_DELAY)
    while True:
        await asyncio.sleep(BAD_TRAP_INTERVAL)
        await send_trap("BadCommunity")
```

### Pattern 4: K8s Deployment (from OBP deployment)

**What:** Single-replica Deployment + ClusterIP Service, pysnmp health probe, env vars.
**When to use:** Every simulator.
**Key differences from OBP:**
- `DEVICE_NAME=E2E-SIM`
- `TRAP_TARGET=snmp-collector.simetra.svc.cluster.local` (per CONTEXT.md -- uses the ClusterIP service, NOT the headless `simetra-pods` service)
- `TRAP_PORT=162` (standard SNMP trap port -- but check: OBP uses 10162 because collector listens on 10162)
- Health probe OID: use one of the E2E-SIM mapped OIDs

**IMPORTANT:** The OBP simulator sends to port 10162 via the headless service. The CONTEXT says to use `snmp-collector.simetra.svc.cluster.local:162`. However, the collector's trap listener binds to port 10162 (container port `snmp` in the production deployment). The trap target port MUST be 10162 to match the collector's actual listening port, OR use port 162 if the ClusterIP service maps 162->10162. Need to verify during planning -- but safest to follow OBP pattern: headless service on port 10162.

### Anti-Patterns to Avoid
- **Dynamic/random values:** The whole point of this simulator is deterministic static values for assertion. No random walk.
- **Complex trap behavior:** No state toggling, no conditional trap logic. Fixed interval, fixed payload.
- **HTTP API for triggering:** CONTEXT explicitly rejected this. Interval-only traps.

## OID Design

### OID Subtree: `1.3.6.1.4.1.47477.999.x.0`

The CONTEXT specifies subtree `1.3.6.1.4.1.47477.999.x` where `.999` marks synthetic test data.

### Recommended OID Layout (6 mapped + 2 unmapped)

#### Mapped OIDs (in oidmaps.json, polled by collector)

| OID | pysnmp Type | Static Value | oidmap Name | Collector snmp_type | Metric Kind |
|-----|-------------|-------------|-------------|---------------------|-------------|
| `.999.1.1.0` | `v2c.Gauge32` | `42` | `e2e_gauge_test` | gauge32 | snmp_gauge |
| `.999.1.2.0` | `v2c.Integer32` | `100` | `e2e_integer_test` | integer32 | snmp_gauge |
| `.999.1.3.0` | `v2c.Counter32` | `5000` | `e2e_counter32_test` | counter32 | snmp_gauge |
| `.999.1.4.0` | `v2c.Counter64` | `1000000` | `e2e_counter64_test` | counter64 | snmp_gauge |
| `.999.1.5.0` | `v2c.TimeTicks` | `360000` | `e2e_timeticks_test` | timeticks | snmp_gauge |
| `.999.1.6.0` | `v2c.OctetString` | `"E2E-TEST-VALUE"` | `e2e_info_test` | octetstring | snmp_info |

#### Unmapped OIDs (NOT in oidmaps.json, resolve to "Unknown")

| OID | pysnmp Type | Static Value | Purpose |
|-----|-------------|-------------|---------|
| `.999.2.1.0` | `v2c.Gauge32` | `99` | Unmapped gauge-type (tests Unknown classification for numeric) |
| `.999.2.2.0` | `v2c.OctetString` | `"UNMAPPED"` | Unmapped info-type (tests Unknown classification for string) |

**Note on IpAddress type:** The CONTEXT lists IpAddress as a type to cover. However, pysnmp `v2c.IpAddress` requires a valid IP string. Could add as a 7th mapped OID (`e2e_ip_test`) or swap one of the above. The collector handles IpAddress in OtelMetricHandler (line 122-132) as an info metric. Recommendation: include it as a 7th mapped OID for completeness since the CONTEXT says "4-6 minimal set" and 7 is close.

### Trap OID

Use a dedicated trap notification OID: `1.3.6.1.4.1.47477.999.3.1` with a varbind from the mapped gauge OID.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SNMP agent | Custom UDP listener | pysnmp engine + MibScalar/DynamicInstance | OBP pattern handles SNMP protocol correctly |
| Trap sending | Raw UDP packets | pysnmp hlapi send_notification | Handles SNMPv2c trap format, community encoding |
| Health probes | HTTP server | Inline python pysnmp GET check | OBP pattern works, no extra dependencies |
| DNS resolution | Hardcoded IPs | socket.getaddrinfo (OBP pattern) | K8s DNS may return multiple IPs |
| Graceful shutdown | os._exit | asyncio task cancellation + signal handlers | OBP pattern handles cleanup properly |

## Common Pitfalls

### Pitfall 1: Trap Port Mismatch
**What goes wrong:** Traps sent to port 162 but collector listens on 10162.
**Why it happens:** CONTEXT says port 162, but the collector deployment exposes containerPort 10162 (named `snmp`). The headless service `simetra-pods` maps port 10162.
**How to avoid:** Use same trap target + port as OBP simulator: `simetra-pods.simetra.svc.cluster.local:10162`. OR if using `snmp-collector` ClusterIP service, verify it maps the correct port. Check simetra-headless.yaml: it maps port 10162->10162.
**Warning signs:** Traps never appear in collector logs, trap counters stay at zero.

### Pitfall 2: OID Trailing `.0` Instance Suffix
**What goes wrong:** OID registered without `.0` suffix, GET requests fail.
**Why it happens:** SNMP scalar instances require `.0` appended. The MibScalar gets the base OID, DynamicInstance gets `(0,)` as index.
**How to avoid:** Follow OBP pattern exactly -- MibScalar takes base OID tuple, DynamicInstance takes `(oid_tuple, (0,), ...)`. The poll OID in devices.json includes `.0`.
**Warning signs:** SNMP GET returns noSuchInstance.

### Pitfall 3: Counter64 pysnmp Syntax
**What goes wrong:** Using wrong v2c class for Counter64.
**Why it happens:** pysnmp v2c module may name it differently.
**How to avoid:** In pysnmp 7.x, use `v2c.Counter64(value)`. The NPB simulator uses Counter64 for octets/packets counters. Verify import works: `from pysnmp.proto.api import v2c; v2c.Counter64(1000000)`.
**Warning signs:** Import error or type mismatch at runtime.

### Pitfall 4: Bad Community Trap Gets Silently Dropped
**What goes wrong:** The trap with `BadCommunity` is dropped by the collector's `CommunityStringHelper.TryExtractDeviceName()` check, but no metrics appear.
**Why it happens:** This is actually the EXPECTED behavior. The collector calls `IncrementTrapAuthFailed("unknown")` (SnmpTrapListenerService.cs line 146) when community validation fails.
**How to avoid:** This is correct behavior. The test should assert that `snmp.trap.auth_failed` counter increments with `device_name="unknown"` label.
**Warning signs:** None -- this is working as designed.

### Pitfall 5: ConfigMap Merge Conflicts
**What goes wrong:** Adding E2E-SIM entries to oidmaps.json/devices.json breaks JSON syntax.
**Why it happens:** Manual JSON editing, trailing commas, duplicate keys.
**How to avoid:** Add E2E-SIM entries at the end of existing JSON structures. Validate JSON after editing.
**Warning signs:** Collector fails to start, OidMapWatcherService logs parse errors.

## Code Examples

### Complete OID Registration Block
```python
# Source: adapted from simulators/obp/obp_simulator.py
E2E_PREFIX = "1.3.6.1.4.1.47477.999"

# Mapped OIDs (will be in oidmaps.json)
MAPPED_OIDS = [
    (f"{E2E_PREFIX}.1.1", "gauge_test",     v2c.Gauge32,      42),
    (f"{E2E_PREFIX}.1.2", "integer_test",   v2c.Integer32,    100),
    (f"{E2E_PREFIX}.1.3", "counter32_test", v2c.Counter32,    5000),
    (f"{E2E_PREFIX}.1.4", "counter64_test", v2c.Counter64,    1000000),
    (f"{E2E_PREFIX}.1.5", "timeticks_test", v2c.TimeTicks,    360000),
    (f"{E2E_PREFIX}.1.6", "info_test",      v2c.OctetString,  "E2E-TEST-VALUE"),
]

# Unmapped OIDs (NOT in oidmaps.json -- resolve to "Unknown")
UNMAPPED_OIDS = [
    (f"{E2E_PREFIX}.2.1", "unmapped_gauge", v2c.Gauge32,      99),
    (f"{E2E_PREFIX}.2.2", "unmapped_info",  v2c.OctetString,  "UNMAPPED"),
]

ALL_OIDS = MAPPED_OIDS + UNMAPPED_OIDS
symbols = {}
registered_oids = []

for oid_str, name, syntax_cls, static_value in ALL_OIDS:
    oid_tuple = tuple(int(x) for x in oid_str.split("."))
    symbols[f"scalar_{name}"] = MibScalar(oid_tuple, syntax_cls())
    symbols[f"instance_{name}"] = DynamicInstance(
        oid_tuple, (0,), syntax_cls(), lambda v=static_value: v
    )
    registered_oids.append(f"{oid_str}.0")

mibBuilder.export_symbols("__E2E-SIM-MIB", **symbols)
```

### Trap Sending with Community String
```python
# Source: adapted from simulators/obp/obp_simulator.py lines 247-274
TRAP_OID = f"{E2E_PREFIX}.3.1"  # notification OID
GAUGE_OID = f"{E2E_PREFIX}.1.1.0"  # varbind: gauge value

async def send_trap_to_targets(community_string):
    target_ips = await resolve_trap_targets(TRAP_TARGET)
    if not target_ips:
        return
    for target_ip in target_ips:
        try:
            target = await UdpTransportTarget.create((target_ip, TRAP_PORT))
            await send_notification(
                hlapi_engine,
                CommunityData(community_string),
                target,
                ContextData(),
                "trap",
                NotificationType(ObjectIdentity(TRAP_OID)).add_varbinds(
                    (GAUGE_OID, v2c.Gauge32(42)),
                ),
            )
            log.info("Trap sent (community=%s) -> %s:%d",
                     community_string, target_ip, TRAP_PORT)
        except Exception as exc:
            log.error("Trap send failed: %s", exc)
```

### devices.json Entry for E2E-SIM
```json
{
    "Name": "E2E-SIM",
    "IpAddress": "e2e-simulator.simetra.svc.cluster.local",
    "Port": 161,
    "MetricPolls": [
        {
            "IntervalSeconds": 10,
            "Oids": [
                "1.3.6.1.4.1.47477.999.1.1.0",
                "1.3.6.1.4.1.47477.999.1.2.0",
                "1.3.6.1.4.1.47477.999.1.3.0",
                "1.3.6.1.4.1.47477.999.1.4.0",
                "1.3.6.1.4.1.47477.999.1.5.0",
                "1.3.6.1.4.1.47477.999.1.6.0"
            ]
        }
    ]
}
```

### oidmaps.json Entries for E2E-SIM
```json
"1.3.6.1.4.1.47477.999.1.1.0": "e2e_gauge_test",
"1.3.6.1.4.1.47477.999.1.2.0": "e2e_integer_test",
"1.3.6.1.4.1.47477.999.1.3.0": "e2e_counter32_test",
"1.3.6.1.4.1.47477.999.1.4.0": "e2e_counter64_test",
"1.3.6.1.4.1.47477.999.1.5.0": "e2e_timeticks_test",
"1.3.6.1.4.1.47477.999.1.6.0": "e2e_info_test"
```

### K8s Health Probe (adapted from OBP)
```yaml
livenessProbe:
  exec:
    command:
    - python
    - -c
    - |
      import sys
      from pysnmp.hlapi.v3arch.asyncio import *
      import asyncio

      async def check():
          engine = SnmpEngine()
          errorIndication, errorStatus, errorIndex, varBinds = await get_cmd(
              engine,
              CommunityData('Simetra.E2E-SIM'),
              await UdpTransportTarget.create(('127.0.0.1', 161), timeout=3, retries=0),
              ContextData(),
              ObjectType(ObjectIdentity('1.3.6.1.4.1.47477.999.1.1.0'))
          )
          engine.close_dispatcher()
          sys.exit(1 if errorIndication else 0)

      asyncio.run(check())
  initialDelaySeconds: 20
  periodSeconds: 30
  timeoutSeconds: 10
  failureThreshold: 3
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| pysnmp 4.x | pysnmp 7.1.22 | 2024 | API changes in engine setup, hlapi is async-first |

No changes needed -- existing simulators already use the current approach.

## Open Questions

1. **Trap target port: 162 vs 10162**
   - What we know: OBP uses `simetra-pods.simetra.svc.cluster.local:10162`. CONTEXT says `snmp-collector.simetra.svc.cluster.local:162`. The collector container listens on port 10162.
   - What's unclear: Whether a ClusterIP service named `snmp-collector` exists that maps 162->10162.
   - Recommendation: Use OBP's proven pattern (`simetra-pods:10162`) unless a ClusterIP service with port mapping is confirmed. During implementation, check existing services with `kubectl get svc -n simetra`.

2. **Whether to include IpAddress as a 7th mapped OID**
   - What we know: CONTEXT says 4-6 OIDs. The collector handles IpAddress type. The CONTEXT lists IpAddress as a type to cover.
   - What's unclear: Whether 7 OIDs exceeds the "4-6 minimal set" intent.
   - Recommendation: Include it. 7 is close to 6, and it covers a real code path in OtelMetricHandler. Use `v2c.IpAddress("10.0.0.1")` with metric name `e2e_ip_test`.

3. **Bad community trap interval relative to valid trap interval**
   - What we know: Both should be env-var configurable. CONTEXT says "periodically" for bad-community traps.
   - Recommendation: Use a separate `BAD_TRAP_INTERVAL` env var defaulting to same as `TRAP_INTERVAL`, or slightly offset (e.g., TRAP_INTERVAL + 5s) to avoid simultaneous sends. Simplest: use the same interval.

## Sources

### Primary (HIGH confidence)
- `simulators/obp/obp_simulator.py` - Complete reference implementation (342 lines)
- `simulators/obp/Dockerfile` - Docker build pattern
- `deploy/k8s/simulators/obp-deployment.yaml` - K8s deployment + service + health probes
- `deploy/k8s/simulators/simetra-headless.yaml` - Headless service for trap targeting
- `deploy/k8s/snmp-collector/simetra-devices.yaml` - Device config structure
- `deploy/k8s/snmp-collector/simetra-oidmaps.yaml` - OID map structure
- `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` - All 7 SNMP type handlers
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` - Trap auth failure handling (line 146)
- `src/SnmpCollector/Pipeline/OidMapService.cs` - Unknown OID resolution

### Secondary (MEDIUM confidence)
- pysnmp 7.1.22 v2c type availability (Counter64, Gauge32, etc.) - verified from existing simulator usage

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - exact same stack as proven OBP/NPB simulators
- Architecture: HIGH - copy-adapt pattern from working 342-line OBP simulator
- OID design: HIGH - subtree .999 confirmed in CONTEXT, types verified against OtelMetricHandler
- Pitfalls: HIGH - derived from actual codebase inspection (port mapping, community validation)
- Trap port: MEDIUM - CONTEXT conflicts with existing patterns, flagged in Open Questions

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable domain, no external dependencies changing)
