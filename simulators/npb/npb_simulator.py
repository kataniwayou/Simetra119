"""
NPB (Network Packet Broker) SNMP Simulator

Serves exactly 68 OIDs matching oidmap-npb.json:
  - 4 system metrics at 47477.100.1.{metricId}.0 (OctetString)
  - 64 per-port metrics at 47477.100.2.{port}.{metricId}.0
    (8 ports x 8 metrics: 1 Integer32 status + 7 Counter64 counters)

Traffic profiles per port:
  P1, P2, P7: heavy    P5, P6: medium    P3: light    P4, P8: zero (down)

System health OIDs random-walk as OctetString values.
portLinkChange traps fire for active ports (P1-P3, P5-P7) with status varbind.
P4 and P8 remain permanently down but respond to GET with valid values.

Environment variables:
  DEVICE_NAME       (default: NPB-01)
  COMMUNITY         (default: Simetra.{DEVICE_NAME})
  TRAP_TARGET       (default: host.docker.internal)
  TRAP_PORT         (default: 162)
  TRAP_INTERVAL_MIN (default: 60)   minimum seconds between per-port traps
  TRAP_INTERVAL_MAX (default: 300)  maximum seconds between per-port traps
"""

import asyncio
import logging
import os
import random
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

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [NPB-SIM] %(message)s",
)
log = logging.getLogger("npb-sim")

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
NPB_PREFIX = "1.3.6.1.4.1.47477.100"
COUNTER64_MAX = 18_446_744_073_709_551_615

DEVICE_NAME = os.environ.get("DEVICE_NAME", "NPB-01")
COMMUNITY = os.environ.get("COMMUNITY", f"Simetra.{DEVICE_NAME}")
TRAP_TARGET = os.environ.get("TRAP_TARGET", "host.docker.internal")
TRAP_PORT = int(os.environ.get("TRAP_PORT", "162"))
TRAP_INTERVAL_MIN = int(os.environ.get("TRAP_INTERVAL_MIN", "60"))
TRAP_INTERVAL_MAX = int(os.environ.get("TRAP_INTERVAL_MAX", "300"))
COUNTER_UPDATE_INTERVAL = 10  # seconds between counter increments
HEALTH_UPDATE_INTERVAL = 10   # seconds between system health walks
STARTUP_DELAY = 12            # seconds before first trap

# ---------------------------------------------------------------------------
# Traffic profiles
# ---------------------------------------------------------------------------
TRAFFIC_PROFILES = {
    "heavy":  {"octet_range": (500_000, 2_000_000), "packet_range": (1000, 5000)},
    "medium": {"octet_range": (100_000, 500_000),   "packet_range": (200, 1000)},
    "light":  {"octet_range": (10_000, 50_000),     "packet_range": (50, 200)},
    "zero":   {"octet_range": (0, 0),               "packet_range": (0, 0)},
}

PORT_TRAFFIC = {
    1: "heavy", 2: "heavy", 3: "light", 4: "zero",
    5: "medium", 6: "medium", 7: "heavy", 8: "zero",
}

# Ports that are permanently down (no traps, no counter increments)
DOWN_PORTS = {4, 8}

# Active ports that get trap loops
TRAP_PORTS = [1, 2, 3, 5, 6, 7]

# ---------------------------------------------------------------------------
# OID metric definitions
# ---------------------------------------------------------------------------
# System metrics: 47477.100.1.{metricId}.0 -- all OctetString per OID map
SYSTEM_METRICS = [
    (1, "cpu_util",  v2c.OctetString),
    (2, "mem_util",  v2c.OctetString),
    (3, "sys_temp",  v2c.OctetString),
    (4, "uptime",    v2c.OctetString),
]

# Per-port metrics: 47477.100.2.{port}.{metricId}.0
PORT_METRICS = [
    (1, "port_status",  v2c.Integer32),
    (2, "rx_octets",    v2c.Counter64),
    (3, "tx_octets",    v2c.Counter64),
    (4, "rx_packets",   v2c.Counter64),
    (5, "tx_packets",   v2c.Counter64),
    (6, "rx_errors",    v2c.Counter64),
    (7, "tx_errors",    v2c.Counter64),
    (8, "rx_drops",     v2c.Counter64),
]

# ---------------------------------------------------------------------------
# State
# ---------------------------------------------------------------------------
system_state = {
    "cpu_util": "15.0",
    "mem_util": "45.0",
    "sys_temp": "42.0",
    "uptime": "0",
}

port_states = {}
for p in range(1, 9):
    port_states[p] = {
        "port_status": 1 if p not in DOWN_PORTS else 2,
        "rx_octets": 0,
        "tx_octets": 0,
        "rx_packets": 0,
        "tx_packets": 0,
        "rx_errors": 0,
        "tx_errors": 0,
        "rx_drops": 0,
    }

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

snmpContext = context.SnmpContext(snmpEngine)
cmdrsp.GetCommandResponder(snmpEngine, snmpContext)
cmdrsp.NextCommandResponder(snmpEngine, snmpContext)
cmdrsp.BulkCommandResponder(snmpEngine, snmpContext)
cmdrsp.SetCommandResponder(snmpEngine, snmpContext)

mibBuilder = snmpContext.get_mib_instrum().get_mib_builder()
MibScalar, MibScalarInstance = mibBuilder.import_symbols(
    "SNMPv2-SMI", "MibScalar", "MibScalarInstance"
)

# ---------------------------------------------------------------------------
# DynamicInstance -- callback-based MibScalarInstance
# ---------------------------------------------------------------------------


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
# OID registration
# ---------------------------------------------------------------------------
symbols = {}
oid_count = 0

# System metrics: 4 OIDs
for metric_id, state_key, syntax_cls in SYSTEM_METRICS:
    oid_str = f"{NPB_PREFIX}.1.{metric_id}"
    oid_tuple = oid_str_to_tuple(oid_str)

    def make_sys_getter(k=state_key):
        return lambda: system_state[k]

    symbols[f"sys_scalar_{metric_id}"] = MibScalar(oid_tuple, syntax_cls())
    symbols[f"sys_instance_{metric_id}"] = DynamicInstance(
        oid_tuple, (0,), syntax_cls(), make_sys_getter()
    )
    oid_count += 1

# Per-port metrics: 8 ports x 8 metrics = 64 OIDs
for port in range(1, 9):
    for metric_id, state_key, syntax_cls in PORT_METRICS:
        oid_str = f"{NPB_PREFIX}.2.{port}.{metric_id}"
        oid_tuple = oid_str_to_tuple(oid_str)

        def make_port_getter(p=port, k=state_key):
            return lambda: port_states[p][k]

        symbols[f"port_scalar_{port}_{metric_id}"] = MibScalar(oid_tuple, syntax_cls())
        symbols[f"port_instance_{port}_{metric_id}"] = DynamicInstance(
            oid_tuple, (0,), syntax_cls(), make_port_getter()
        )
        oid_count += 1

mibBuilder.export_symbols("__NPB-SIM-MIB", **symbols)
log.info("Registered %d poll OIDs (4 system + 8 ports x 8 metrics)", oid_count)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def random_walk_float(current, step, low, high):
    """Float random walk, clamped to [low, high]."""
    delta = random.uniform(-step, step)
    return max(low, min(high, current + delta))


def random_walk_int(current, step, low, high):
    """Integer random walk, clamped to [low, high]."""
    delta = random.randint(-step, step)
    return max(low, min(high, current + delta))


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
# Background task: increment counters
# ---------------------------------------------------------------------------


async def increment_counters():
    """Increment Counter64 values per traffic profile every COUNTER_UPDATE_INTERVAL."""
    while True:
        await asyncio.sleep(COUNTER_UPDATE_INTERVAL)
        for port in range(1, 9):
            state = port_states[port]

            # Skip down ports -- OIDs still respond but counters stay at 0
            if state["port_status"] == 2:
                continue

            profile = TRAFFIC_PROFILES[PORT_TRAFFIC[port]]
            octet_lo, octet_hi = profile["octet_range"]
            packet_lo, packet_hi = profile["packet_range"]

            if octet_hi == 0:
                continue

            # RX octets and TX octets (TX = RX * 0.8-0.95)
            rx_octet_inc = random.randint(octet_lo, octet_hi)
            tx_ratio = random.uniform(0.8, 0.95)
            tx_octet_inc = int(rx_octet_inc * tx_ratio)

            state["rx_octets"] = (state["rx_octets"] + rx_octet_inc) % (COUNTER64_MAX + 1)
            state["tx_octets"] = (state["tx_octets"] + tx_octet_inc) % (COUNTER64_MAX + 1)

            # RX packets and TX packets
            rx_pkt_inc = random.randint(packet_lo, packet_hi)
            tx_pkt_inc = int(rx_pkt_inc * random.uniform(0.8, 0.95))

            state["rx_packets"] = (state["rx_packets"] + rx_pkt_inc) % (COUNTER64_MAX + 1)
            state["tx_packets"] = (state["tx_packets"] + tx_pkt_inc) % (COUNTER64_MAX + 1)

            # Error/drop injection: ~1% chance per cycle
            if random.random() < 0.01:
                error_type = random.choice(["rx_errors", "tx_errors", "rx_drops"])
                state[error_type] = (state[error_type] + random.randint(1, 3)) % (COUNTER64_MAX + 1)


# ---------------------------------------------------------------------------
# Background task: update system health
# ---------------------------------------------------------------------------

# Track float values internally for smooth random walk
_health_floats = {
    "cpu_util": 15.0,
    "mem_util": 45.0,
    "sys_temp": 42.0,
    "uptime_int": 0,
}


async def update_system_health():
    """Random-walk system health OIDs every HEALTH_UPDATE_INTERVAL."""
    while True:
        await asyncio.sleep(HEALTH_UPDATE_INTERVAL)

        _health_floats["cpu_util"] = random_walk_float(
            _health_floats["cpu_util"], 2.0, 5.0, 40.0
        )
        _health_floats["mem_util"] = random_walk_float(
            _health_floats["mem_util"], 1.5, 30.0, 70.0
        )
        _health_floats["sys_temp"] = random_walk_float(
            _health_floats["sys_temp"], 1.0, 35.0, 55.0
        )
        _health_floats["uptime_int"] += HEALTH_UPDATE_INTERVAL

        system_state["cpu_util"] = f"{_health_floats['cpu_util']:.1f}"
        system_state["mem_util"] = f"{_health_floats['mem_util']:.1f}"
        system_state["sys_temp"] = f"{_health_floats['sys_temp']:.1f}"
        system_state["uptime"] = str(_health_floats["uptime_int"])


# ---------------------------------------------------------------------------
# Background task: per-port trap loops
# ---------------------------------------------------------------------------

hlapi_engine = HlapiEngine()


async def send_port_link_trap(port, new_status):
    """Send portLinkChange trap for the given port with status varbind."""
    trap_oid = f"{NPB_PREFIX}.3.{port}.0"
    port_status_oid = f"{NPB_PREFIX}.2.{port}.1.0"

    target_ips = await resolve_trap_targets(TRAP_TARGET)
    if not target_ips:
        return

    for target_ip in target_ips:
        try:
            target = await UdpTransportTarget.create((target_ip, TRAP_PORT))
            await send_notification(
                hlapi_engine,
                CommunityData(COMMUNITY),
                target,
                ContextData(),
                "trap",
                NotificationType(ObjectIdentity(trap_oid)).add_varbinds(
                    (port_status_oid, v2c.Integer32(new_status)),
                ),
            )
            log.info(
                "portLinkChange trap port=%d status=%d (%s) -> %s:%d",
                port, new_status,
                "up" if new_status == 1 else "down",
                target_ip, TRAP_PORT,
            )
        except Exception as exc:
            log.error("Trap send failed port=%d: %s", port, exc)


async def per_port_trap_loop(port):
    """Independent trap loop for one port. Toggles port_status and sends portLinkChange trap."""
    await asyncio.sleep(STARTUP_DELAY)
    while True:
        await asyncio.sleep(random.uniform(TRAP_INTERVAL_MIN, TRAP_INTERVAL_MAX))

        state = port_states[port]
        old_status = state["port_status"]
        new_status = 2 if old_status == 1 else 1

        # STATE FIRST: mutate poll state before sending trap
        state["port_status"] = new_status
        log.info("Port %d status toggle: %d -> %d", port, old_status, new_status)

        await send_port_link_trap(port, new_status)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main():
    log.info("NPB Simulator starting...")
    log.info("PID: %d", os.getpid())
    log.info("DEVICE_NAME=%s  COMMUNITY=%s", DEVICE_NAME, COMMUNITY)
    log.info(
        "TRAP_TARGET=%s  TRAP_PORT=%d  "
        "TRAP_INTERVAL_MIN=%ds  TRAP_INTERVAL_MAX=%ds  STARTUP_DELAY=%ds",
        TRAP_TARGET, TRAP_PORT, TRAP_INTERVAL_MIN, TRAP_INTERVAL_MAX, STARTUP_DELAY,
    )
    log.info(
        "COUNTER_UPDATE_INTERVAL=%ds  HEALTH_UPDATE_INTERVAL=%ds",
        COUNTER_UPDATE_INTERVAL, HEALTH_UPDATE_INTERVAL,
    )
    log.info("Traffic profiles: %s", {p: PORT_TRAFFIC[p] for p in range(1, 9)})
    log.info("Down ports (permanent): %s", sorted(DOWN_PORTS))
    log.info("Trap ports (active): %s", TRAP_PORTS)

    loop = asyncio.get_event_loop()

    # Background tasks
    tasks = []

    # Counter increment task
    tasks.append(loop.create_task(
        supervised_task("increment_counters", increment_counters)
    ))

    # System health update task
    tasks.append(loop.create_task(
        supervised_task("update_system_health", update_system_health)
    ))

    # Per-port trap loops for active ports only
    for port in TRAP_PORTS:
        tasks.append(loop.create_task(
            supervised_task(
                f"per_port_trap_loop_{port}",
                lambda p=port: per_port_trap_loop(p),
            )
        ))

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
    log.info("Serving %d OIDs (4 system + 64 per-port)", oid_count)
    snmpEngine.open_dispatcher()


main()
