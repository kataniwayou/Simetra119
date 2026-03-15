"""
E2E Test SNMP Simulator

Provides a controllable, deterministic SNMP device for E2E pipeline testing.
All values are static (no random walk) so tests can assert exact expected values.

Serves 9 OIDs total:
  7 mapped   (.999.1.x) -- Gauge32, Integer32, Counter32, Counter64, TimeTicks,
                            OctetString, IpAddress
  2 unmapped (.999.2.x) -- Gauge32, OctetString (outside oidmaps.json)

Sends two trap streams:
  - Valid traps   with community Simetra.E2E-SIM  every TRAP_INTERVAL seconds
  - Bad-community traps with community BadCommunity every BAD_TRAP_INTERVAL seconds

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

from pysnmp.entity import engine, config
from pysnmp.entity.rfc3413 import cmdrsp, context
from pysnmp.carrier.asyncio.dgram import udp
from pysnmp.proto.api import v2c
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
# OID definitions -- static values for deterministic testing
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

ALL_OIDS = MAPPED_OIDS + UNMAPPED_OIDS

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
config.add_vacm_user(snmpEngine, 2, "my-area", "noAuthNoPriv", (1, 3, 6, 1, 4, 1))

# Additional community strings for E2E test scenarios (device-add, recovery tests)
for extra_community in [
    "Simetra.E2E-SIM-2",
    "Simetra.FAKE-UNREACHABLE",
]:
    area_name = f"area-{extra_community}"
    config.add_v1_system(snmpEngine, area_name, extra_community)
    config.add_vacm_user(snmpEngine, 2, area_name, "noAuthNoPriv", (1, 3, 6, 1, 4, 1))
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
    """MibScalarInstance that returns a live value via a callback function."""

    def __init__(self, oid_tuple, index_tuple, syntax, get_value_fn):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._get_value_fn = get_value_fn

    def getValue(self, name, **ctx):
        return self.getSyntax().clone(self._get_value_fn())


def oid_str_to_tuple(oid_str):
    """Convert dotted OID string to integer tuple, stripping leading dot."""
    return tuple(int(x) for x in oid_str.strip(".").split("."))


# ---------------------------------------------------------------------------
# OID registration: 9 OIDs (7 mapped + 2 unmapped)
# ---------------------------------------------------------------------------

symbols = {}
registered_oids = []

for oid_str, label, syntax_cls, static_value in ALL_OIDS:
    oid_tuple = oid_str_to_tuple(oid_str)
    safe_label = label.replace("-", "_")

    symbols[f"scalar_{safe_label}"] = MibScalar(oid_tuple, syntax_cls())
    symbols[f"instance_{safe_label}"] = DynamicInstance(
        oid_tuple, (0,), syntax_cls(), lambda v=static_value: v
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
# Main
# ---------------------------------------------------------------------------


def main():
    log.info("E2E Test Simulator starting (9 OIDs, dual trap loops)...")
    log.info("PID: %d", os.getpid())
    log.info("Community string: %s", COMMUNITY)
    log.info(
        "Configuration: TRAP_TARGET=%s TRAP_PORT=%d "
        "TRAP_INTERVAL=%ds BAD_TRAP_INTERVAL=%ds STARTUP_DELAY=%ds",
        TRAP_TARGET, TRAP_PORT, TRAP_INTERVAL, BAD_TRAP_INTERVAL, STARTUP_DELAY,
    )
    log.info("Registered %d poll OIDs:", len(registered_oids))
    for oid in registered_oids:
        log.info("  %s", oid)
    log.info("Trap OID: %s", TRAP_OID)

    loop = asyncio.get_event_loop()

    tasks = [
        loop.create_task(supervised_task("valid_trap_loop", valid_trap_loop)),
        loop.create_task(supervised_task("bad_community_trap_loop", bad_community_trap_loop)),
    ]

    def _shutdown(sig_name):
        log.info("Received %s -- shutting down gracefully", sig_name)
        for t in tasks:
            t.cancel()
        snmpEngine.close_dispatcher()

    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            loop.add_signal_handler(sig, _shutdown, sig.name)
        except NotImplementedError:
            log.warning("Signal handler for %s not supported on this platform", sig.name)

    log.info("SNMP agent listening on 0.0.0.0:161 (community: %s)", COMMUNITY)
    snmpEngine.open_dispatcher()


main()
