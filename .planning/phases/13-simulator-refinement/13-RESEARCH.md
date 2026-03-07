# Phase 13: Simulator Refinement - Research

**Researched:** 2026-03-07
**Domain:** Python SNMP agent simulators (pysnmp 7.1.22), OID tree design, realistic value generation
**Confidence:** HIGH

## Summary

This phase rewrites both OBP and NPB SNMP simulators to serve exactly the OIDs defined in their respective OID maps, replacing the old OID trees entirely. The existing codebase already uses pysnmp 7.1.22 with the `MibScalar`/`MibScalarInstance`/`DynamicInstance` pattern -- this is proven and should be reused verbatim. The OBP simulator shrinks from 8 OIDs to 24 OIDs (adding 16 power OIDs), while the NPB simulator shrinks from ~560 OIDs to exactly 68 (removing the entire `47477.100.4.*` tree in favor of the new `47477.100.1.*` system and `47477.100.2.*` per-port tree).

Key challenges are: (1) correctly matching every OID from the JSON OID maps, (2) implementing the new community string `Simetra.{DeviceName}` and updating K8s health probes that hardcode `public`, (3) implementing realistic value behaviors per the CONTEXT.md decisions, and (4) resolving a type conflict between the NPB OID map documentation (OctetString for system metrics) and the CONTEXT.md decisions (Integer32 for cpu_util, mem_util, sys_temp).

**Primary recommendation:** Clean rewrite both simulators using the existing `DynamicInstance` pattern, deriving OID registrations directly from the OID map JSON files where possible, with one background async task per behavior domain.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| pysnmp | 7.1.22 | SNMP agent engine, trap sender | Already pinned in requirements.txt; proven in both simulators |
| Python | 3.12 | Runtime | Already specified in Dockerfile |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| asyncio | stdlib | Event loop, background tasks | All background value updates, trap loops |
| random | stdlib | Random walk, traffic profiles | Value simulation |
| signal | stdlib | Graceful shutdown | SIGTERM/SIGINT handling |
| socket | stdlib | DNS resolution for trap targets | Trap delivery |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| pysnmp MibScalar/DynamicInstance | snmpsim (recording playback) | snmpsim is read-only playback, cannot do dynamic value walks or traps |

**Installation:**
```bash
pip install pysnmp==7.1.22
```

## Architecture Patterns

### Recommended Project Structure
```
simulators/
  obp/
    obp_simulator.py    # Single-file, ~250-300 lines (clean rewrite)
    Dockerfile           # Unchanged
    requirements.txt     # Unchanged (pysnmp==7.1.22)
  npb/
    npb_simulator.py    # Single-file, ~400-500 lines (clean rewrite)
    Dockerfile           # Unchanged
    requirements.txt     # Unchanged (pysnmp==7.1.22)
```

### Pattern 1: DynamicInstance with Closure-Based Getters
**What:** Subclass `MibScalarInstance` with a callback that reads from a mutable state dict on every GET request.
**When to use:** Every OID registration -- this is the only pattern needed.
**Example:**
```python
# Source: existing obp_simulator.py and npb_simulator.py (proven pattern)
class DynamicInstance(MibScalarInstance):
    def __init__(self, oid_tuple, index_tuple, syntax, get_value_fn):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._get_value_fn = get_value_fn

    def getValue(self, name, **ctx):
        return self.getSyntax().clone(self._get_value_fn())
```

### Pattern 2: Data-Driven OID Registration
**What:** Define OID-to-state mappings as data tables, loop over them to register MibScalar + DynamicInstance pairs.
**When to use:** All OID registration -- avoids copy-paste for 24/68 OIDs.
**Example:**
```python
# OBP: 4 links x 6 metrics = 24 OIDs
OBP_PREFIX = "1.3.6.1.4.1.47477.10.21"
LINK_METRICS = [
    (1, "link_state", v2c.Integer32),   # suffix .1.0
    (4, "channel",    v2c.Integer32),   # suffix .4.0
    (10, "r1_power",  v2c.Integer32),   # suffix .10.0
    (11, "r2_power",  v2c.Integer32),   # suffix .11.0
    (12, "r3_power",  v2c.Integer32),   # suffix .12.0
    (13, "r4_power",  v2c.Integer32),   # suffix .13.0
]

for link in range(1, 5):
    for suffix, state_key, syntax_cls in LINK_METRICS:
        oid_str = f"{OBP_PREFIX}.{link}.3.{suffix}"
        oid_tuple = tuple(int(x) for x in oid_str.split("."))
        # register MibScalar + DynamicInstance...
```

### Pattern 3: Background Task Organization
**What:** One `supervised_task` wrapper per background behavior domain, all scheduled before `open_dispatcher()`.
**When to use:** All value update and trap loops.
**Recommended task breakdown:**

**OBP (3 tasks):**
1. `update_power_values` -- random walk all 16 power OIDs every poll cycle
2. `per_link_trap_loop` x4 -- independent per-link StateChange trap with random interval (reuse existing pattern)

**NPB (3-4 tasks):**
1. `increment_counters` -- increment Counter64 values per traffic profile
2. `update_system_health` -- random walk cpu/mem/temp/uptime
3. `per_port_trap_loop` x6 -- independent per-port link toggle with random interval (P1-P3, P5-P7)

### Pattern 4: Community String Configuration
**What:** Change the `add_v1_system` community parameter from `"public"` to `Simetra.{DeviceName}`, configurable via `COMMUNITY` env var.
**When to use:** Both simulators.
**Example:**
```python
DEVICE_NAME = os.environ.get("DEVICE_NAME", "OBP-01")
COMMUNITY = os.environ.get("COMMUNITY", f"Simetra.{DEVICE_NAME}")

config.add_v1_system(snmpEngine, "my-area", COMMUNITY)
```

### Pattern 5: Trap Community String
**What:** Traps sent via HLAPI must also use the correct community string.
**When to use:** All `send_notification` calls.
**Example:**
```python
await send_notification(
    hlapi_engine,
    CommunityData(COMMUNITY),  # NOT "public"
    target,
    ContextData(),
    "trap",
    ...
)
```

### Anti-Patterns to Avoid
- **Registering OIDs not in the OID map:** The entire point of this phase is 1:1 alignment. Every registered OID must appear in the JSON OID map; no extras.
- **Hardcoding community "public" anywhere:** Both poll agent and trap sender must use `Simetra.{DeviceName}`.
- **One big state change task for all ports:** Use independent per-port/per-link loops with random intervals for realistic staggering, matching the existing OBP pattern.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SNMP agent framework | Custom UDP socket handler | pysnmp `engine`/`config`/`cmdrsp` | Protocol compliance, GETNEXT/GETBULK handling |
| Trap delivery | Raw UDP packet construction | pysnmp `send_notification` HLAPI | Correct PDU encoding, varbind formatting |
| Community string auth | Custom packet inspection | pysnmp `add_v1_system` + `add_vacm_user` | Built-in auth rejection for wrong community |
| MIB object serving | Custom OID tree walk | `MibScalar` + `MibScalarInstance` | Correct lexicographic ordering for GETNEXT |

**Key insight:** pysnmp already handles community string rejection -- if a request comes in with a community string not matching any `add_v1_system` call, pysnmp returns an SNMP error automatically. No custom rejection code needed.

## Common Pitfalls

### Pitfall 1: K8s Liveness/Readiness Probes Hardcode "public" Community
**What goes wrong:** Both K8s deployment YAMLs contain inline Python health probe scripts that send a raw SNMP packet with `public` hardcoded as hex bytes (`7075626c6963`). After changing the community string, probes will fail, pods will restart-loop.
**Why it happens:** The probe sends a raw SNMP GET packet and checks for any UDP response. The community string is embedded in the hex packet.
**How to avoid:** Update the hex packet in both `obp-deployment.yaml` and `npb-deployment.yaml` to encode the new community string, OR rewrite the probe to use pysnmp with the correct community.
**Warning signs:** Pod CrashLoopBackOff after deploying new simulator images.

### Pitfall 2: SNMP Type Conflict for NPB System Metrics
**What goes wrong:** The OID map comments say `npb_cpu_util`, `npb_mem_util`, `npb_sys_temp` are `OctetString` (e.g., "45.2"), but CONTEXT.md decisions say `Integer32` (e.g., 45). If the simulator uses Integer32 but the collector expects OctetString (or vice versa), parsing will fail silently or produce wrong values.
**Why it happens:** The OID map was written with one type convention; the discussion phase decided differently.
**How to avoid:** The planner MUST resolve this conflict before implementation. Options:
  - (A) Follow CONTEXT.md decisions (Integer32) and update the OID map comments to match
  - (B) Follow the OID map (OctetString) and amend the CONTEXT.md behavior descriptions
  - (C) Use OctetString containing integer strings ("45") as a compromise
**Warning signs:** Collector logs showing parse errors for system metric OIDs.

### Pitfall 3: Counter64 Not Supported in SNMPv1
**What goes wrong:** Counter64 type only exists in SNMPv2c. If `add_v1_system` is misunderstood as SNMPv1-only, Counter64 OIDs will error.
**Why it happens:** The function name `add_v1_system` is misleading -- it configures community-based security (used by both v1 and v2c). The `add_vacm_user` call with security model `2` (SNMPv2c) is what matters.
**How to avoid:** Keep the existing pattern: `add_v1_system` + `add_vacm_user(snmpEngine, 2, ...)` which correctly enables SNMPv2c with community auth.
**Warning signs:** `noSuchObject` or encoding errors when querying Counter64 OIDs.

### Pitfall 4: OID Tuple vs String Confusion
**What goes wrong:** pysnmp's `MibScalar` takes an OID tuple, but trap `ObjectIdentity` takes an OID string. Mixing these up causes registration failures or trap delivery failures.
**Why it happens:** pysnmp has inconsistent APIs between low-level engine and high-level HLAPI.
**How to avoid:** Use a consistent `oid_str_to_tuple()` helper for registration and plain strings for HLAPI trap calls.

### Pitfall 5: Counter64 Wrapping
**What goes wrong:** Counter64 values that exceed 2^64 cause overflow errors.
**Why it happens:** Forgetting to apply modular arithmetic.
**How to avoid:** Always wrap: `state[key] = (state[key] + increment) % (COUNTER64_MAX + 1)`. The existing NPB simulator already does this correctly.

### Pitfall 6: NPB Port P4 and P8 Must Still Respond to GET
**What goes wrong:** Down ports still need to respond to SNMP GET with valid values (status=2, counters=0). Skipping OID registration for "zero" ports means `noSuchObject`.
**Why it happens:** Confusing "port is down" with "OID doesn't exist."
**How to avoid:** Register all 68 OIDs for all 8 ports. P4 and P8 just return static/zero values.

## Code Examples

### OBP Power Random Walk
```python
# Source: CONTEXT.md decisions + existing random_walk pattern from NPB
# Each receiver has independent baseline and walk
POWER_BASELINES = {
    # (link, receiver): baseline in tenths of dBm
    (1, "r1"): -85,  (1, "r2"): -92,  (1, "r3"): -88,  (1, "r4"): -95,
    (2, "r1"): -78,  (2, "r2"): -82,  (2, "r3"): -90,  (2, "r4"): -87,
    (3, "r1"): -110, (3, "r2"): -105, (3, "r3"): -115, (3, "r4"): -108,
    (4, "r1"): -130, (4, "r2"): -140, (4, "r3"): -125, (4, "r4"): -135,
}

def random_walk_int(current: int, step: int, low: int, high: int) -> int:
    """Integer random walk, clamped to [low, high]."""
    delta = random.randint(-step, step)
    return max(low, min(high, current + delta))

# In update loop:
for (link, rx), baseline in POWER_BASELINES.items():
    key = f"{rx}_power"
    link_state[link][key] = random_walk_int(link_state[link][key], 2, -200, -50)
```

### NPB Counter Increment Profiles
```python
# Source: CONTEXT.md decisions
# Traffic profiles: (octet_increment_range, packet_increment_range)
TRAFFIC_PROFILES = {
    "heavy":  (500_000, 2_000_000, 1000, 5000),   # P1, P2, P7
    "medium": (100_000, 500_000,   200,  1000),    # P5, P6
    "light":  (10_000,  50_000,    50,   200),     # P3
    "zero":   (0, 0, 0, 0),                        # P4, P8
}

PORT_TRAFFIC = {
    1: "heavy", 2: "heavy", 3: "light", 4: "zero",
    5: "medium", 6: "medium", 7: "heavy", 8: "zero",
}

# Error/drop injection: ~1 per 100 cycles
for port in range(1, 9):
    if PORT_TRAFFIC[port] != "zero" and random.random() < 0.01:
        state[port]["rx_errors"] = (state[port]["rx_errors"] + random.randint(1, 3)) % wrap
```

### Community String Setup
```python
# Source: existing codebase pattern + CONTEXT.md decisions
DEVICE_NAME = os.environ.get("DEVICE_NAME", "NPB-01")
COMMUNITY = os.environ.get("COMMUNITY", f"Simetra.{DEVICE_NAME}")

snmpEngine = engine.SnmpEngine()
config.add_transport(snmpEngine, udp.DOMAIN_NAME,
    udp.UdpTransport().open_server_mode(("0.0.0.0", 161)))
config.add_v1_system(snmpEngine, "my-area", COMMUNITY)
config.add_vacm_user(snmpEngine, 2, "my-area", "noAuthNoPriv", (1, 3, 6, 1, 4, 1))
```

### NPB Trap with Correct Varbind
```python
# Source: CONTEXT.md decisions
# Trap OID: 47477.100.3.{port}.0
# Varbind: port_status poll OID + new value
NPB_PREFIX = "1.3.6.1.4.1.47477.100"

async def send_port_link_trap(port, new_status):
    trap_oid = f"{NPB_PREFIX}.3.{port}.0"
    # Varbind includes the polled OID for port_status and its new value
    port_status_oid = f"{NPB_PREFIX}.2.{port}.1.0"
    varbinds = (
        (port_status_oid, v2c.Integer32(new_status)),  # 1=up, 2=down
    )
    await send_trap_to_targets(trap_oid, varbinds, f"portLink port={port} status={new_status}")
```

### OBP Trap with Correct Varbind
```python
# Source: CONTEXT.md decisions
# Trap OID: 47477.10.21.{link}.3.50.2 (existing)
# Varbind: channel poll OID + new channel value
OBP_PREFIX = "1.3.6.1.4.1.47477.10.21"

async def send_state_change_trap(link, new_channel):
    trap_oid = f"{OBP_PREFIX}.{link}.3.50.2"
    channel_oid = f"{OBP_PREFIX}.{link}.3.4.0"
    varbinds = (
        (channel_oid, v2c.Integer32(new_channel)),
    )
    await send_trap_to_targets(trap_oid, varbinds, f"StateChange link={link} channel={new_channel}")
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| OBP: 8 OIDs (link_state + channel only) | 24 OIDs (+ r1-r4 power per link) | This phase | 16 new power OIDs with random walk behavior |
| NPB: ~560 OIDs at `47477.100.4.*` | 68 OIDs at `47477.100.1.*` + `47477.100.2.*` | This phase | Complete OID tree replacement |
| Community: `public` | `Simetra.{DeviceName}` | This phase | Auth alignment with collector convention |
| NPB traps: `portLinkUp`/`portLinkDown` at `100.4.10.2.*` | `portLinkChange` at `100.3.{port}.0` | This phase | Simplified trap OID scheme |

## Open Questions

1. **NPB System Metric SNMP Type: Integer32 vs OctetString**
   - What we know: OID map comments say OctetString (e.g., "45.2"), CONTEXT.md says Integer32 (e.g., 45 for cpu, 30-70 for mem, 35-55 for temp)
   - What's unclear: Which is the intended source of truth for the SNMP wire type
   - Recommendation: **The planner MUST resolve this before implementation.** The safest approach is to follow the OID map type (OctetString) since that is what the collector will parse, and adjust the CONTEXT.md behavior to use OctetString values (e.g., "45" or "45.2"). Integer32 would require updating the OID map type annotations AND potentially the collector's parsing logic.

2. **K8s Health Probe Update Strategy**
   - What we know: Probes hardcode `public` community in raw hex SNMP packets
   - What's unclear: Whether to update the hex encoding inline or switch to a simpler TCP/process-based probe
   - Recommendation: Encode the new community string as hex and update the probe packets. This is safer than changing probe strategy. The hex for `Simetra.OBP-01` is `53696d657472612e4f42502d3031` (14 bytes) vs `public` which is `7075626c6963` (6 bytes). The packet length fields in the raw SNMP PDU will also need adjusting.

3. **configmap-devices.yaml OID References**
   - What we know: The template configmap at `deploy/k8s/simulators/configmap-devices.yaml` references old NPB OIDs from the `47477.100.4.*` tree
   - What's unclear: Whether updating this configmap is in scope for this phase
   - Recommendation: Flag for the planner to include a task updating the configmap OID references to the new `47477.100.1.*` / `47477.100.2.*` tree

## Sources

### Primary (HIGH confidence)
- Existing codebase: `simulators/obp/obp_simulator.py` (237 lines) -- proven pysnmp patterns
- Existing codebase: `simulators/npb/npb_simulator.py` (1200 lines) -- proven pysnmp patterns, DynamicInstance, supervised_task, Counter64 wrapping, random_walk
- `src/SnmpCollector/config/oidmap-obp.json` -- 24 OIDs, authoritative source
- `src/SnmpCollector/config/oidmap-npb.json` -- 68 OIDs, authoritative source
- `deploy/k8s/simulators/obp-deployment.yaml` -- health probes with hardcoded community
- `deploy/k8s/simulators/npb-deployment.yaml` -- health probes with hardcoded community

### Secondary (MEDIUM confidence)
- [PySNMP 7.1 agent examples](https://docs.lextudio.com/pysnmp/v7.1/examples/v3arch/asyncio/agent/cmdrsp/agent-side-mib-implementations) -- confirms `add_v1_system` + `add_vacm_user` pattern for community-based auth
- [PySNMP GitHub - multiple communities example](https://github.com/etingof/pysnmp/blob/master/examples/v3arch/asyncore/agent/cmdrsp/multiple-snmp-communities.py) -- confirms community string rejection behavior

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- pysnmp 7.1.22 is already proven in both simulators, no version change needed
- Architecture: HIGH -- reusing existing patterns (DynamicInstance, supervised_task, random_walk) that are proven in the codebase
- Pitfalls: HIGH -- identified from direct code inspection (hardcoded probes, type conflict, Counter64)
- Value behaviors: HIGH for OBP power (CONTEXT.md is specific); HIGH for NPB counters (CONTEXT.md specifies profiles)

**Research date:** 2026-03-07
**Valid until:** 2026-04-07 (stable -- pysnmp version pinned, no external API changes)
