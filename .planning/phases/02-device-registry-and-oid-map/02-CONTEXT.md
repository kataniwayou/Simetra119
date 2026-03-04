# Phase 2: Device Registry and OID Map - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

All lookup structures (device registry and OID map) are populated from configuration, O(1) lookups work correctly, cardinality is explicitly counted and bounded before any metric instruments are created, and hot-reload of the OID map functions without restart. Device hot-reload is deferred to v2 (OPS-01).

</domain>

<decisions>
## Implementation Decisions

### Device config shape
- Match Simetra's existing device config pattern (Name, IpAddress, MetricPolls array) — copy and modify for SnmpCollector namespace
- Multiple poll groups per device — each device has an array of MetricPolls, each with its own OID list and IntervalSeconds (e.g., fast poll for CPU, slow poll for interface stats). This satisfies COLL-03.
- Community string: global default in SnmpListener config with optional per-device override — supports mixed environments without requiring every device entry to specify it
- Target fleet: small deployment (5-15 NPB/OBP devices) — design for this scale, don't over-engineer

### OID map granularity
- One global OID map shared by all devices (MAP-04) — flat Dictionary<string, string> in appsettings under OidMap section
- Unknown OIDs: log at Warning level AND record the metric with metric_name="Unknown" — operator sees unknown OIDs quickly in logs, and the data still reaches Prometheus for investigation
- OID map entries contain metric name only — no additional metadata fields. TypeCode detection remains runtime-only (METR-06).
- Metric name convention: MIB object names in camelCase (e.g., hrProcessorLoad, ifInOctets, sysUpTime) — these are the standard SNMP identifiers operators already know

### Cardinality bounding
- Agent label uses device name from config (e.g., "npb-core-01") — human-readable, bounded by device count in config
- Source label: just "poll" or "trap" — minimal cardinality (2 values), no poll interval encoding
- Cardinality enforcement: warn but allow — log a warning at startup if estimated total series (devices x OIDs x label combinations) exceeds a threshold, but don't block startup
- Cardinality estimate for target fleet: ~5-15 devices x ~5-20 OIDs x 3 instruments x 2 sources = ~150-1800 series — well within Prometheus capacity

### Hot-reload behavior
- OID map only reloads without restart (MAP-05) — device list changes require restart (device hot-reload is v2 per OPS-01)
- Reload trigger: IOptionsMonitor<T> with OnChange callback that logs what changed at Information level
- Removed OID entries: resolve to "Unknown" going forward — removing an OID from the map means future traps/polls with that OID get metric_name="Unknown"
- Reload logging: log at Information level with summary ("OID map reloaded: N entries added, M changed, K removed")

### Claude's Discretion
- Internal data structure choice for device registry (ConcurrentDictionary, FrozenDictionary, custom class)
- Exact cardinality warning threshold value
- OID map change detection implementation details

</decisions>

<specifics>
## Specific Ideas

- Device registry needs dual-keyed O(1) lookup: by IP address (trap path — incoming trap has source IP) and by name (poll path — Quartz job identity uses device name)
- Quartz job identities follow pattern `metric-poll-{deviceName}-{pollIndex}` — must be derivable from device config at startup (DEVC-04)
- Label taxonomy must be documented with cardinality estimate before any Phase 3 instruments are created — this is the cardinality lock gate

</specifics>

<deferred>
## Deferred Ideas

- Hot-reloadable device configuration (add/remove devices without restart) — v2 requirement OPS-01
- Per-device OID maps for device-type-specific monitoring — not needed for uniform NPB/OBP fleet
- SNMP table walk / GETBULK for dynamic OID discovery — v2 requirement ADV-01

</deferred>

---

*Phase: 02-device-registry-and-oid-map*
*Context gathered: 2026-03-05*
