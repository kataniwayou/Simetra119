# Phase 11 Context: OID Map Design and OBP Population

## Decisions

### OID Map File Structure (OIDM-02)
- **Separate files per device type**: `oidmap-obp.json`, `oidmap-npb.json` (one per device family)
- **Auto-scan pattern**: At startup, scan config directory for all `oidmap-*.json` files and register them dynamically with `AddJsonFile(..., reloadOnChange: true)`. Adding a new device type is purely a ConfigMap change — no code touch.
- **Single ConfigMap**: All OID map files are keys in one `simetra-config` ConfigMap
- **Runtime merge**: .NET configuration system merges all `OidMap` sections from multiple files into one `OidMapOptions.Entries` dictionary. Existing `OidMapService` with `IOptionsMonitor` hot-reload works unchanged.

### K8s Volume Mount Strategy
- **Directory mount (no subPath)**: Mount ConfigMap as a directory (e.g., `/app/config/`) instead of individual subPath mounts. This enables K8s to auto-propagate ConfigMap changes to pods (~30-60s), triggering .NET file watcher and `IOptionsMonitor.OnChange` for live OID map hot-reload without pod restart.
- **Affects**: `deploy/k8s/deployment.yaml` volume mount config, `Program.cs` configuration builder setup

### Hot-Reload Scope
- **OID map hot-reload**: Already works via `OidMapService` + `IOptionsMonitor<OidMapOptions>`. The auto-scan and directory mount extend this to multiple files.
- **Poll config hot-reload**: Deferred to v2 (OPS-01). Quartz jobs are static at startup. Adding/removing devices or changing poll OID lists requires pod restart. This is acceptable for v1.1.
- **RELOAD-01 removed from v1.1**: Merged into OPS-01 in v2 requirements.

### OID Documentation Format (DOC-01)
- **JSONC comments inline**: Each OID entry in `oidmap-*.json` gets a comment line above it with: SNMP type, units (if applicable), value range, and description.
- **.NET compatibility**: `AddJsonFile` strips JSONC comments during parsing — no runtime impact.
- **No separate markdown docs**: Documentation lives co-located with the OID entries.

### OID Naming Convention (OIDM-01)
- **Format**: `{device_type_prefix}_{metric_name}_{index_suffix}`
- **Examples**: `obp_link_state_L1`, `obp_r1_power_L3`, `npb_port_rx_octets_P1`, `npb_port_status_P8`
- **Device prefixes**: `obp_` for Optical Bypass, `npb_` for Network Packet Broker
- **Index suffixes**: `L1`-`L4` for OBP links, `P1`-`P8` for NPB ports

## Claude's Discretion

- Implementation details for the `oidmap-*.json` auto-scan (glob pattern, registration order)
- Exact OBP OID strings (based on enterprise OID prefix `1.3.6.1.4.1.47477.10.21`)
- Which OBP OIDs to include for "realistic coverage" (state, channel, optical power R1-R4 per link)
- JSONC comment format and level of detail

## Deferred Ideas

- Poll configuration hot-reload (v2, OPS-01)
- Per-device-type OID map sections (nested JSON structure) — rejected in favor of separate files
- Standalone markdown OID documentation — rejected in favor of inline JSONC comments
