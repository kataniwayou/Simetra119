# Phase 12: NPB OID Population - Context

**Gathered:** 2026-03-07
**Status:** Ready for planning

## Decisions

### NPB OID Coverage
- **68 total OIDs**: 4 system-level + 64 per-port (8 OIDs x 8 ports)
- **System OIDs**: CPU utilization, memory utilization, system temperature, uptime
- **Per-port OIDs**: port_status, rx_octets, tx_octets, rx_packets, tx_packets, rx_errors, tx_errors, rx_drops
- **Port status**: Integer enum matching ifOperStatus convention (1=up, 2=down, 3=testing)

### NPB OID Tree Structure
- **Enterprise prefix**: `1.3.6.1.4.1.47477.100`
- **Subtree separation**: System and per-port OIDs in separate branches (recommended MIB practice)
  - System: `47477.100.1.{metricId}.0` — scalar subtree
  - Per-port: `47477.100.2.{portNum}.{metricId}.0` — tabular subtree
- **Port numbering**: 1-8 (physical ports)

### NPB Metric Naming
- **System metrics**: `npb_{metric}` — no suffix (device-wide). Examples: `npb_cpu_util`, `npb_mem_util`, `npb_sys_temp`, `npb_uptime`
- **Per-port metrics**: `npb_port_{metric}_P{n}` — direction baked into metric name. Examples: `npb_port_rx_octets_P1`, `npb_port_status_P8`
- **Follows Phase 11 convention**: `{device_type_prefix}_{metric_name}_{index_suffix}`

### Documentation Format
- **Same as OBP**: Inline JSONC comments with SNMP type, units, value meaning, and expected range for every OID
- **Consistent across device families**: No lighter treatment for counters

## Claude's Discretion

- Sequential metric suffix IDs (1, 2, 3...) for both system and per-port OIDs
- Exact expected ranges for counter metrics
- JSONC comment wording and level of detail (following OBP precedent)

## Deferred Ideas

None — discussion stayed within phase scope.
