"""
E2E Test SNMP Simulator

Provides a controllable, deterministic SNMP device for E2E pipeline testing.
All values are driven by a named scenario registry — switch scenarios at runtime
via the HTTP control endpoint on port 8080.

Serves 48 OIDs total:
  7 mapped      (.999.1.x) -- Gauge32, Integer32, Counter32, Counter64, TimeTicks,
                              OctetString, IpAddress
  2 unmapped    (.999.2.x) -- Gauge32, OctetString (outside oidmaps.json)
  6 test-purpose(.999.4.x) -- Gauge32 x5, Integer32 x1 (writable command target)
  9 tenant-OIDs (.999.5.x, .999.6.x, .999.7.x) -- Gauge32, per-tenant T2-T4
 24 v2.6 tenant (.999.8.x, .999.9.x, .999.10.x, .999.11.x) -- Gauge32, 4-tenant fixture

Sends two trap streams:
  - Valid traps   with community Simetra.E2E-SIM  every TRAP_INTERVAL seconds
  - Bad-community traps with community BadCommunity every BAD_TRAP_INTERVAL seconds

HTTP control endpoint (aiohttp, port 8080):
  POST /scenario/{name}  -- switch active scenario
  GET  /scenario         -- return current scenario name
  GET  /scenarios        -- return sorted list of all scenario names
  POST /oid/{oid}/stale  -- set OID to return NoSuchInstance (stale)
  POST /oid/{oid}/{value} -- set OID to return a fixed integer value
  DELETE /oid/overrides  -- clear all per-OID overrides, revert to scenario

OID tree: 1.3.6.1.4.1.47477.999.{subtree}.{suffix}.0
Community string: Simetra.{DEVICE_NAME} (default: Simetra.E2E-SIM)

Environment variables:
  DEVICE_NAME        (default: E2E-SIM)
  COMMUNITY          (default: Simetra.{DEVICE_NAME})
  TRAP_TARGET        (default: simetra-pods.simetra.svc.cluster.local)
  TRAP_PORT          (default: 10162)
  TRAP_INTERVAL      (default: 30)   seconds between valid traps
  BAD_TRAP_INTERVAL  (default: 45)   seconds between bad-community traps
"""

import asyncio
import logging
import os
import signal
import socket

from aiohttp import web
from pysnmp.entity import engine, config
from pysnmp.entity.rfc3413 import cmdrsp, context
from pysnmp.carrier.asyncio.dgram import udp
from pysnmp.proto.api import v2c
from pysnmp.smi.error import NoSuchInstanceError
from pysnmp.hlapi.v3arch.asyncio import (
    SnmpEngine as HlapiEngine,
    CommunityData,
    UdpTransportTarget,
    ContextData,
    send_notification,
    NotificationType,
    ObjectIdentity,
)

logging.basicConfig(level=logging.INFO, format="%(asctime)s [E2E-SIM] %(message)s")
log = logging.getLogger("e2e-simulator")

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

DEVICE_NAME = os.environ.get("DEVICE_NAME", "E2E-SIM")
COMMUNITY = os.environ.get("COMMUNITY", f"Simetra.{DEVICE_NAME}")

TRAP_TARGET = os.environ.get("TRAP_TARGET", "simetra-pods.simetra.svc.cluster.local")
TRAP_PORT = int(os.environ.get("TRAP_PORT", "10162"))
TRAP_INTERVAL = int(os.environ.get("TRAP_INTERVAL", "30"))
BAD_TRAP_INTERVAL = int(os.environ.get("BAD_TRAP_INTERVAL", "45"))
STARTUP_DELAY = 12  # seconds before first trap

E2E_PREFIX = "1.3.6.1.4.1.47477.999"

# ---------------------------------------------------------------------------
# Scenario registry
# ---------------------------------------------------------------------------

STALE = object()  # Sentinel: return noSuchInstance for this OID


def _make_scenario(overrides: dict) -> dict:
    """Create a full scenario dict from baseline values with optional overrides."""
    baseline = {
        # Existing 9 OIDs -- exact same values as pre-HTTP simulator
        f"{E2E_PREFIX}.1.1": 42,                # gauge_test      Gauge32
        f"{E2E_PREFIX}.1.2": 100,               # integer_test    Integer32
        f"{E2E_PREFIX}.1.3": 5000,              # counter32_test  Counter32
        f"{E2E_PREFIX}.1.4": 1000000,           # counter64_test  Counter64
        f"{E2E_PREFIX}.1.5": 360000,            # timeticks_test  TimeTicks
        f"{E2E_PREFIX}.1.6": "E2E-TEST-VALUE",  # info_test       OctetString
        f"{E2E_PREFIX}.1.7": "10.0.0.1",        # ip_test         IpAddress
        f"{E2E_PREFIX}.2.1": 99,                # unmapped_gauge  Gauge32
        f"{E2E_PREFIX}.2.2": "UNMAPPED",        # unmapped_info   OctetString
        # New test-purpose OIDs (subtree .999.4.x)
        f"{E2E_PREFIX}.4.1": 0,                 # e2e_evaluate_metric   Gauge32
        f"{E2E_PREFIX}.4.2": 0,                 # e2e_resolved_metric   Gauge32
        f"{E2E_PREFIX}.4.3": 0,                 # e2e_bypass_status     Gauge32
        f"{E2E_PREFIX}.4.4": 0,                 # e2e_command_response  Integer32 (writable)
        f"{E2E_PREFIX}.4.5": 0,                 # e2e_agg_source_a      Gauge32
        f"{E2E_PREFIX}.4.6": 0,                 # e2e_agg_source_b      Gauge32
        # Phase 61: per-tenant OIDs for T2-T4 (subtrees .999.5.x, .999.6.x, .999.7.x)
        f"{E2E_PREFIX}.5.1": 0,                 # e2e_eval_T2   Gauge32
        f"{E2E_PREFIX}.5.2": 0,                 # e2e_res1_T2   Gauge32
        f"{E2E_PREFIX}.5.3": 0,                 # e2e_res2_T2   Gauge32
        f"{E2E_PREFIX}.6.1": 0,                 # e2e_eval_T3   Gauge32
        f"{E2E_PREFIX}.6.2": 0,                 # e2e_res1_T3   Gauge32
        f"{E2E_PREFIX}.6.3": 0,                 # e2e_res2_T3   Gauge32
        f"{E2E_PREFIX}.7.1": 0,                 # e2e_eval_T4   Gauge32
        f"{E2E_PREFIX}.7.2": 0,                 # e2e_res1_T4   Gauge32
        f"{E2E_PREFIX}.7.3": 0,                 # e2e_res2_T4   Gauge32
        # v2.6: T1_P1 (subtree .999.8.x) -- 4 polled OIDs
        f"{E2E_PREFIX}.8.1": 0,                 # e2e_T1P1_eval1  Gauge32
        f"{E2E_PREFIX}.8.2": 0,                 # e2e_T1P1_eval2  Gauge32
        f"{E2E_PREFIX}.8.3": 0,                 # e2e_T1P1_res1   Gauge32
        f"{E2E_PREFIX}.8.4": 0,                 # e2e_T1P1_res2   Gauge32
        # v2.6: T2_P1 (subtree .999.9.x) -- 8 polled OIDs
        f"{E2E_PREFIX}.9.1": 0,                 # e2e_T2P1_eval1  Gauge32
        f"{E2E_PREFIX}.9.2": 0,                 # e2e_T2P1_eval2  Gauge32
        f"{E2E_PREFIX}.9.3": 0,                 # e2e_T2P1_eval3  Gauge32
        f"{E2E_PREFIX}.9.4": 0,                 # e2e_T2P1_eval4  Gauge32
        f"{E2E_PREFIX}.9.5": 0,                 # e2e_T2P1_res1   Gauge32
        f"{E2E_PREFIX}.9.6": 0,                 # e2e_T2P1_res2   Gauge32
        f"{E2E_PREFIX}.9.7": 0,                 # e2e_T2P1_res3   Gauge32
        f"{E2E_PREFIX}.9.8": 0,                 # e2e_T2P1_res4   Gauge32
        # v2.6: T1_P2 (subtree .999.10.x) -- 4 polled OIDs
        f"{E2E_PREFIX}.10.1": 0,                # e2e_T1P2_eval1  Gauge32
        f"{E2E_PREFIX}.10.2": 0,                # e2e_T1P2_eval2  Gauge32
        f"{E2E_PREFIX}.10.3": 0,                # e2e_T1P2_res1   Gauge32
        f"{E2E_PREFIX}.10.4": 0,                # e2e_T1P2_res2   Gauge32
        # v2.6: T2_P2 (subtree .999.11.x) -- 8 polled OIDs
        f"{E2E_PREFIX}.11.1": 0,                # e2e_T2P2_eval1  Gauge32
        f"{E2E_PREFIX}.11.2": 0,                # e2e_T2P2_eval2  Gauge32
        f"{E2E_PREFIX}.11.3": 0,                # e2e_T2P2_eval3  Gauge32
        f"{E2E_PREFIX}.11.4": 0,                # e2e_T2P2_eval4  Gauge32
        f"{E2E_PREFIX}.11.5": 0,                # e2e_T2P2_res1   Gauge32
        f"{E2E_PREFIX}.11.6": 0,                # e2e_T2P2_res2   Gauge32
        f"{E2E_PREFIX}.11.7": 0,                # e2e_T2P2_res3   Gauge32
        f"{E2E_PREFIX}.11.8": 0,                # e2e_T2P2_res4   Gauge32
    }
    baseline.update(overrides)
    return baseline


SCENARIOS: dict[str, dict] = {
    "default":          _make_scenario({}),
    "threshold_breach": _make_scenario({f"{E2E_PREFIX}.4.1": 90}),
    "threshold_clear":  _make_scenario({f"{E2E_PREFIX}.4.1": 5}),
    "bypass_active":    _make_scenario({f"{E2E_PREFIX}.4.3": 1}),
    "stale":            _make_scenario({
                            f"{E2E_PREFIX}.4.1": STALE,
                            f"{E2E_PREFIX}.4.2": STALE,
                        }),
    "command_trigger":  _make_scenario({
                            f"{E2E_PREFIX}.4.1": 90,
                            f"{E2E_PREFIX}.4.2": 2,
                            f"{E2E_PREFIX}.4.3": 2,
                        }),
    "healthy":          _make_scenario({
                            f"{E2E_PREFIX}.4.1": 5,    # e2e_port_utilization = 5 (< Max:80)
                            f"{E2E_PREFIX}.4.2": 2,    # e2e_channel_state = 2 (>= Min:1.0)
                            f"{E2E_PREFIX}.4.3": 2,    # e2e_bypass_status = 2 (>= Min:1.0)
                        }),
    "agg_breach":       _make_scenario({
                            f"{E2E_PREFIX}.4.2": 2,    # e2e_channel_state = 2 (>= Min:1.0 -> in-range)
                            f"{E2E_PREFIX}.4.3": 2,    # e2e_bypass_status = 2 (>= Min:1.0 -> in-range)
                            f"{E2E_PREFIX}.4.5": 50,   # e2e_agg_source_a = 50
                            f"{E2E_PREFIX}.4.6": 50,   # e2e_agg_source_b = 50
                            # sum(50,50) = 100 > Max:80 -> e2e_total_util violated
                            # Resolved metrics in-range -> tier-2 passes -> reaches tier-4
                        }),
}

_active_scenario: str = "default"
_oid_overrides: dict[str, int] = {}   # OID string -> integer value
_stale_oids: set[str] = set()         # OIDs that should return NoSuchInstance

# ---------------------------------------------------------------------------
# OID definitions -- for documentation and registration
# ---------------------------------------------------------------------------

# Mapped OIDs (7 total, subtree .999.1.x) -- covered by oidmaps.json
MAPPED_OIDS = [
    (f"{E2E_PREFIX}.1.1", "gauge_test",     v2c.Gauge32,      42),
    (f"{E2E_PREFIX}.1.2", "integer_test",   v2c.Integer32,    100),
    (f"{E2E_PREFIX}.1.3", "counter32_test", v2c.Counter32,    5000),
    (f"{E2E_PREFIX}.1.4", "counter64_test", v2c.Counter64,    1000000),
    (f"{E2E_PREFIX}.1.5", "timeticks_test", v2c.TimeTicks,    360000),
    (f"{E2E_PREFIX}.1.6", "info_test",      v2c.OctetString,  "E2E-TEST-VALUE"),
    (f"{E2E_PREFIX}.1.7", "ip_test",        v2c.IpAddress,    "10.0.0.1"),
]

# Unmapped OIDs (2 total, subtree .999.2.x) -- NOT in oidmaps.json
UNMAPPED_OIDS = [
    (f"{E2E_PREFIX}.2.1", "unmapped_gauge", v2c.Gauge32,      99),
    (f"{E2E_PREFIX}.2.2", "unmapped_info",  v2c.OctetString,  "UNMAPPED"),
]

# Test-purpose OIDs (6 total, subtree .999.4.x)
# Tuple: (oid_str, label, syntax_cls, writable)
TEST_OIDS = [
    (f"{E2E_PREFIX}.4.1", "e2e_evaluate_metric",  v2c.Gauge32,   False),
    (f"{E2E_PREFIX}.4.2", "e2e_resolved_metric",   v2c.Gauge32,   False),
    (f"{E2E_PREFIX}.4.3", "e2e_bypass_status",     v2c.Gauge32,   False),
    (f"{E2E_PREFIX}.4.4", "e2e_command_response",  v2c.Integer32, True),   # writable
    (f"{E2E_PREFIX}.4.5", "e2e_agg_source_a",      v2c.Gauge32,   False),
    (f"{E2E_PREFIX}.4.6", "e2e_agg_source_b",      v2c.Gauge32,   False),
]

# Phase 61: Per-tenant OIDs for T2-T4 (subtrees .999.5.x, .999.6.x, .999.7.x)
TENANT_OIDS = [
    (f"{E2E_PREFIX}.5.1", "e2e_eval_T2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.5.2", "e2e_res1_T2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.5.3", "e2e_res2_T2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.6.1", "e2e_eval_T3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.6.2", "e2e_res1_T3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.6.3", "e2e_res2_T3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.7.1", "e2e_eval_T4",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.7.2", "e2e_res1_T4",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.7.3", "e2e_res2_T4",  v2c.Gauge32, False),
]

# v2.6: Per-tenant OIDs for T1_P1/T2_P1/T1_P2/T2_P2 (subtrees .999.8.x - .999.11.x)
TENANT_OIDS_V26 = [
    # T1_P1 (subtree .999.8.x) -- P1, 2E + 2R = 4 polled OIDs
    (f"{E2E_PREFIX}.8.1",  "e2e_T1P1_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.2",  "e2e_T1P1_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.3",  "e2e_T1P1_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.8.4",  "e2e_T1P1_res2",  v2c.Gauge32, False),
    # T2_P1 (subtree .999.9.x) -- P1, 4E + 4R = 8 polled OIDs
    (f"{E2E_PREFIX}.9.1",  "e2e_T2P1_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.2",  "e2e_T2P1_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.3",  "e2e_T2P1_eval3", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.4",  "e2e_T2P1_eval4", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.5",  "e2e_T2P1_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.6",  "e2e_T2P1_res2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.7",  "e2e_T2P1_res3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.9.8",  "e2e_T2P1_res4",  v2c.Gauge32, False),
    # T1_P2 (subtree .999.10.x) -- P2, 2E + 2R = 4 polled OIDs
    (f"{E2E_PREFIX}.10.1", "e2e_T1P2_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.10.2", "e2e_T1P2_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.10.3", "e2e_T1P2_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.10.4", "e2e_T1P2_res2",  v2c.Gauge32, False),
    # T2_P2 (subtree .999.11.x) -- P2, 4E + 4R = 8 polled OIDs
    (f"{E2E_PREFIX}.11.1", "e2e_T2P2_eval1", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.2", "e2e_T2P2_eval2", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.3", "e2e_T2P2_eval3", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.4", "e2e_T2P2_eval4", v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.5", "e2e_T2P2_res1",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.6", "e2e_T2P2_res2",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.7", "e2e_T2P2_res3",  v2c.Gauge32, False),
    (f"{E2E_PREFIX}.11.8", "e2e_T2P2_res4",  v2c.Gauge32, False),
]

# Trap configuration
TRAP_OID = f"{E2E_PREFIX}.3.1"
GAUGE_OID = f"{E2E_PREFIX}.1.1.0"

# ---------------------------------------------------------------------------
# SNMP engine setup
# ---------------------------------------------------------------------------

snmpEngine = engine.SnmpEngine()
config.add_transport(
    snmpEngine,
    udp.DOMAIN_NAME,
    udp.UdpTransport().open_server_mode(("0.0.0.0", 161)),
)
config.add_v1_system(snmpEngine, "my-area", COMMUNITY)
config.add_vacm_user(snmpEngine, 2, "my-area", "noAuthNoPriv", (1, 3, 6, 1, 4, 1), (1, 3, 6, 1, 4, 1))

# Additional community strings for E2E test scenarios (device-add, recovery tests)
for extra_community in [
    "Simetra.E2E-SIM-2",
    "Simetra.FAKE-UNREACHABLE",
]:
    area_name = f"area-{extra_community}"
    config.add_v1_system(snmpEngine, area_name, extra_community)
    config.add_vacm_user(snmpEngine, 2, area_name, "noAuthNoPriv", (1, 3, 6, 1, 4, 1), (1, 3, 6, 1, 4, 1))
snmpContext = context.SnmpContext(snmpEngine)
cmdrsp.GetCommandResponder(snmpEngine, snmpContext)
cmdrsp.NextCommandResponder(snmpEngine, snmpContext)
cmdrsp.BulkCommandResponder(snmpEngine, snmpContext)
cmdrsp.SetCommandResponder(snmpEngine, snmpContext)

mibBuilder = snmpContext.get_mib_instrum().get_mib_builder()
MibScalar, MibScalarInstance = mibBuilder.import_symbols(
    "SNMPv2-SMI", "MibScalar", "MibScalarInstance"
)


class DynamicInstance(MibScalarInstance):
    """MibScalarInstance that reads its value from the active scenario dict."""

    def __init__(self, oid_tuple, index_tuple, syntax, oid_str):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._oid_str = oid_str

    def getValue(self, name, **ctx):
        # Per-OID stale override (highest priority)
        if self._oid_str in _stale_oids:
            raise NoSuchInstanceError(name=name, idx=(0,))
        # Per-OID value override
        if self._oid_str in _oid_overrides:
            return self.getSyntax().clone(_oid_overrides[self._oid_str])
        # Fall back to active scenario
        val = SCENARIOS[_active_scenario].get(self._oid_str, STALE)
        if val is STALE:
            raise NoSuchInstanceError(name=name, idx=(0,))
        return self.getSyntax().clone(val)


class WritableDynamicInstance(DynamicInstance):
    """DynamicInstance that accepts SNMP SET, storing value in active scenario."""

    def writeCommit(self, varBind, **context):
        # Store the SET value in the active scenario dict
        name, value = varBind
        SCENARIOS[_active_scenario][self._oid_str] = value
        # Call super to let pysnmp 7.x complete the transaction
        super().writeCommit(varBind, **context)


def oid_str_to_tuple(oid_str):
    """Convert dotted OID string to integer tuple, stripping leading dot."""
    return tuple(int(x) for x in oid_str.strip(".").split("."))


# ---------------------------------------------------------------------------
# OID registration: 48 OIDs (7 mapped + 2 unmapped + 6 test-purpose + 9 tenant + 24 v2.6 tenant)
# ---------------------------------------------------------------------------

symbols = {}
registered_oids = []

# Register existing 9 OIDs (MAPPED + UNMAPPED) using DynamicInstance
for oid_str, label, syntax_cls, _static_value in MAPPED_OIDS + UNMAPPED_OIDS:
    oid_tuple = oid_str_to_tuple(oid_str)
    safe_label = label.replace("-", "_")

    symbols[f"scalar_{safe_label}"] = MibScalar(oid_tuple, syntax_cls())
    symbols[f"instance_{safe_label}"] = DynamicInstance(
        oid_tuple, (0,), syntax_cls(), oid_str
    )
    registered_oids.append(f"{oid_str}.0")

# Register 39 test-purpose and tenant OIDs (including v2.6)
for oid_str, label, syntax_cls, writable in TEST_OIDS + TENANT_OIDS + TENANT_OIDS_V26:
    oid_tuple = oid_str_to_tuple(oid_str)
    safe_label = label.replace("-", "_")

    scalar = MibScalar(oid_tuple, syntax_cls())
    if writable:
        # CRITICAL: MibScalar must be readwrite so VACM permits SET before
        # ever reaching writeCommit on the instance
        scalar = scalar.setMaxAccess("read-write")
        instance_cls = WritableDynamicInstance
    else:
        instance_cls = DynamicInstance

    symbols[f"scalar_{safe_label}"] = scalar
    symbols[f"instance_{safe_label}"] = instance_cls(
        oid_tuple, (0,), syntax_cls(), oid_str
    )
    registered_oids.append(f"{oid_str}.0")

mibBuilder.export_symbols("__E2E-SIM-MIB", **symbols)

# ---------------------------------------------------------------------------
# Helper functions
# ---------------------------------------------------------------------------


async def supervised_task(name, coro_fn):
    """Run an async task forever, restarting on unhandled exceptions."""
    backoff = 5
    max_backoff = 300
    while True:
        try:
            await coro_fn()
        except asyncio.CancelledError:
            log.info("Task '%s' cancelled -- shutting down", name)
            raise
        except Exception:
            log.exception("Task '%s' crashed -- restarting in %ds", name, backoff)
            await asyncio.sleep(backoff)
            backoff = min(backoff * 2, max_backoff)
        else:
            log.warning("Task '%s' exited normally (unexpected) -- restarting", name)


async def resolve_trap_targets(hostname):
    """Resolve hostname via DNS and return deduplicated list of IPv4 addresses."""
    try:
        results = socket.getaddrinfo(hostname, None, socket.AF_INET)
        return list({addr[4][0] for addr in results})
    except socket.gaierror as exc:
        log.warning("DNS resolution failed for %s: %s", hostname, exc)
        return []


# ---------------------------------------------------------------------------
# Trap sending
# ---------------------------------------------------------------------------

hlapi_engine = HlapiEngine()


async def send_trap_to_targets(community_string):
    """Send a trap with the given community string to all resolved targets."""
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
            log.info(
                "Trap sent community=%s -> %s:%d",
                community_string, target_ip, TRAP_PORT,
            )
        except Exception as exc:
            log.error("Trap send failed community=%s: %s", community_string, exc)


async def valid_trap_loop():
    """Send valid traps with correct community on TRAP_INTERVAL."""
    await asyncio.sleep(STARTUP_DELAY)
    while True:
        await asyncio.sleep(TRAP_INTERVAL)
        await send_trap_to_targets(COMMUNITY)


async def bad_community_trap_loop():
    """Send traps with bad community string on BAD_TRAP_INTERVAL."""
    await asyncio.sleep(STARTUP_DELAY)
    while True:
        await asyncio.sleep(BAD_TRAP_INTERVAL)
        await send_trap_to_targets("BadCommunity")


# ---------------------------------------------------------------------------
# HTTP control endpoint (aiohttp)
# ---------------------------------------------------------------------------


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


async def get_scenario(request: web.Request) -> web.Response:
    return web.json_response({"scenario": _active_scenario})


async def get_scenarios(request: web.Request) -> web.Response:
    return web.json_response({"scenarios": sorted(SCENARIOS.keys())})


async def post_oid_value(request: web.Request) -> web.Response:
    """Set a specific OID to return a fixed integer value."""
    oid_suffix = request.match_info["oid"]
    value = int(request.match_info["value"])
    # Normalize: ensure full OID string with E2E_PREFIX
    full_oid = f"{E2E_PREFIX}.{oid_suffix.lstrip('.')}"
    _stale_oids.discard(full_oid)  # clear any stale override
    _oid_overrides[full_oid] = value
    log.info("OID override set: %s = %d", full_oid, value)
    return web.json_response({"oid": full_oid, "value": value})


async def post_oid_stale(request: web.Request) -> web.Response:
    """Set a specific OID to return NoSuchInstance (stale)."""
    oid_suffix = request.match_info["oid"]
    full_oid = f"{E2E_PREFIX}.{oid_suffix.lstrip('.')}"
    _oid_overrides.pop(full_oid, None)  # clear any value override
    _stale_oids.add(full_oid)
    log.info("OID stale override set: %s", full_oid)
    return web.json_response({"oid": full_oid, "stale": True})


async def delete_oid_overrides(request: web.Request) -> web.Response:
    """Clear all per-OID overrides (value and stale), revert to scenario."""
    _oid_overrides.clear()
    _stale_oids.clear()
    log.info("All OID overrides cleared (%d value, %d stale)", 0, 0)
    return web.json_response({"cleared": True})


async def start_http_server() -> web.AppRunner:
    app = web.Application()
    app.router.add_post("/scenario/{name}", post_scenario)
    app.router.add_get("/scenario", get_scenario)
    app.router.add_get("/scenarios", get_scenarios)
    # IMPORTANT: /oid/{oid}/stale must be registered BEFORE /oid/{oid}/{value}
    # so that the literal "stale" segment matches the stale handler first.
    app.router.add_post("/oid/{oid}/stale", post_oid_stale)
    app.router.add_post("/oid/{oid}/{value}", post_oid_value)
    app.router.add_delete("/oid/overrides", delete_oid_overrides)
    runner = web.AppRunner(app)
    await runner.setup()
    site = web.TCPSite(runner, "0.0.0.0", 8080)
    await site.start()
    log.info("HTTP control endpoint listening on 0.0.0.0:8080")
    return runner


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main():
    log.info("E2E Test Simulator starting (24 OIDs, dual trap loops, HTTP control)...")
    log.info("PID: %d", os.getpid())
    log.info("Community string: %s", COMMUNITY)
    log.info(
        "Configuration: TRAP_TARGET=%s TRAP_PORT=%d "
        "TRAP_INTERVAL=%ds BAD_TRAP_INTERVAL=%ds STARTUP_DELAY=%ds",
        TRAP_TARGET, TRAP_PORT, TRAP_INTERVAL, BAD_TRAP_INTERVAL, STARTUP_DELAY,
    )
    log.info("Active scenario: %s", _active_scenario)
    log.info("Registered %d poll OIDs:", len(registered_oids))
    for oid in registered_oids:
        log.info("  %s", oid)
    log.info("Trap OID: %s", TRAP_OID)

    loop = asyncio.get_event_loop()

    # CRITICAL: start HTTP server BEFORE open_dispatcher() -- open_dispatcher() calls
    # loop.run_forever() internally, blocking all subsequent code until shutdown
    runner = loop.run_until_complete(start_http_server())

    tasks = [
        loop.create_task(supervised_task("valid_trap_loop", valid_trap_loop)),
        loop.create_task(supervised_task("bad_community_trap_loop", bad_community_trap_loop)),
    ]

    def _shutdown(sig_name):
        log.info("Received %s -- shutting down gracefully", sig_name)
        for t in tasks:
            t.cancel()
        loop.run_until_complete(runner.cleanup())  # clean aiohttp shutdown
        snmpEngine.close_dispatcher()

    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            loop.add_signal_handler(sig, _shutdown, sig.name)
        except NotImplementedError:
            log.warning("Signal handler for %s not supported on this platform", sig.name)

    log.info("SNMP agent listening on 0.0.0.0:161 (community: %s)", COMMUNITY)
    snmpEngine.open_dispatcher()


main()
