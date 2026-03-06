# Phase 10: Metrics - Context

**Gathered:** 2026-03-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Redesign the SNMP trap and poll paths to accept traps from any source (no device registry requirement for traps), use a community string convention (`Simetra.{DeviceName}`) for both auth and device identity, replace `site_name` with `host_name` from the machine, update readiness checks, and ensure consistent metric labeling across traps and polls.

</domain>

<decisions>
## Implementation Decisions

### Community string convention
- Format: `Simetra.{DeviceName}` (e.g., `Simetra.npb-core-01`)
- Validation: prefix must match `Simetra.` — reject anything that doesn't
- Device name extracted from the part after `Simetra.` dot
- Prefix `Simetra.` is hardcoded — no config field needed
- `SnmpListenerOptions.CommunityString` field removed (replaced by convention)
- Invalid community string → drop trap/poll + log at Debug level (not Warning)

### Poll community string
- Derive automatically from `DeviceOptions.Name` — community = `"Simetra." + Name`
- Remove `DeviceOptions.CommunityString` field entirely
- Polls use the same `Simetra.{DeviceName}` convention as traps

### Metric labels (all instruments: snmp_gauge, snmp_info)
- `host_name` — machine hostname (`HOSTNAME` env var or `Environment.MachineName`), replaces `site_name`
- `device_name` — extracted from community string (after `Simetra.`)
- `ip` — sender IP address (trap) or polled device IP (poll)
- `metric_name` — resolved from OID map (unchanged)
- `oid` — raw OID string (unchanged)
- `source` — "poll" or "trap" (unchanged)
- `snmp_type` — raw SNMP type code (added in quick-010, unchanged)

### Remove Site:Name requirement
- `SiteOptions.Name` no longer required in appsettings
- `site_name` label replaced by `host_name` everywhere (metrics, logs, console formatter)
- Log enrichment processor uses hostname instead of site name
- Console formatter: `[hostname|role|correlationId]` instead of `[site|role|correlationId]`

### Trap routing
- Single shared BoundedChannel for all incoming traps (no per-device channels)
- `DropOldest` backpressure policy preserved
- `snmp.trap.dropped` counter retained for visibility
- DeviceChannelManager simplified or removed for trap path
- Trap listener: parse community string → validate `Simetra.*` prefix → extract device name → write to shared channel
- No device registry lookup for traps

### Readiness check
- Remove `DeviceNames.Count > 0` requirement
- Ready when: trap listener UDP socket is bound AND Quartz scheduler is running
- Empty `Devices[]` config is valid — pod accepts traps without any poll configuration

### Consistent trap/poll design
- Both paths produce `SnmpOidReceived` with identical label set
- Both paths validate community string with `Simetra.*` convention
- Both paths extract `device_name` from community string
- Both paths include `ip` label (sender IP for traps, target IP for polls)
- Poll path: community string = `"Simetra." + DeviceOptions.Name` (derived, not configured)
- Trap path: community string parsed from incoming PDU

### Claude's Discretion
- Whether to keep DeviceChannelManager with a single channel or replace with a simpler abstraction
- How to signal trap listener "bound" status to ReadinessHealthCheck (flag, event, or service check)
- CardinalityAuditService updates for new label taxonomy
- Test strategy for community string validation

</decisions>

<specifics>
## Specific Ideas

- Community string `Simetra.{DeviceName}` is a business convention — the NPB and OBP devices will be configured with this pattern on their management interfaces
- The SnmpCollector is an open collector — any valid SNMP trap from any IP is accepted as long as the community string matches the convention
- This is fundamentally different from Simetra where devices must be pre-registered — SnmpCollector discovers device identity from the community string at receive time

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 10-metrics*
*Context gathered: 2026-03-06*
