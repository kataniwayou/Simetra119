# Phase 5: Trap Ingestion - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

The application receives SNMPv2c traps on UDP 162, authenticates via community string, routes each varbind as a separate SnmpOidReceived through the MediatR pipeline, and handles trap storms via per-device BoundedChannel with DropOldest backpressure. The listener is a BackgroundService. Actual SNMP polling is Phase 6. Leader election gating is Phase 7.

</domain>

<decisions>
## Implementation Decisions

### Community string policy
- **Per-device override** with global fallback — device's `CommunityString` (from DeviceOptions, already nullable) takes precedence; if null, fall back to `SnmpListenerOptions.CommunityString`
- **Case-sensitive** comparison (RFC-compliant)
- **Reject if no match** — every trap must match either the device-specific or global community string. No exceptions, even if the device has no community string configured (null device CS + non-matching global CS = reject)
- **On auth failure**: drop the trap, log at **Warning** (source IP + received community string), AND increment `snmp.trap.auth_failed` counter

### Unknown device handling
- Traps from IPs **not in the device registry** are **dropped** — not routed to the pipeline
- Log at **Warning** with source IP + first varbind OID for diagnostic context
- Increment `snmp.trap.unknown_device` counter for Prometheus alerting
- Each trap varbind published as **one SnmpOidReceived per varbind** — consistent with poll path (one OID, one pipeline run)

### Backpressure thresholds
- Per-device **BoundedChannel** with capacity **1,000 traps** and **DropOldest** policy
- **No global rate limit** — per-device channels are sufficient. Scales linearly with device count.
- When traps are dropped: increment `snmp.trap.dropped` counter per device AND log **Warning periodically** (not every drop — Claude decides the interval)

### Trap lifecycle logging
- **No per-trap log** — rely on existing LoggingBehavior in the pipeline (already logs every SnmpOidReceived at Debug)
- **Startup**: Log at **Information** with device count — "Trap listener bound to UDP 162, monitoring {N} devices"
- **First contact**: Log at **Information** when the first trap arrives from each device since startup — "First trap received from {deviceName} ({ip})"
- **Malformed packets**: Log at **Warning** with source IP and error, drop, continue listening

### Claude's Discretion
- Check ordering: whether community string auth happens before or after device registry lookup (security vs performance tradeoff)
- Periodic drop Warning log interval (every N drops)
- ChannelConsumerService design — one Task per device, consumer loop structure
- How the DeviceChannelManager maps device IPs to their BoundedChannel instances
- Whether to use SharpSnmpLib's built-in trap listener or a raw UDP socket

</decisions>

<specifics>
## Specific Ideas

- The trap listener must NEVER publish directly to MediatR — all trap varbinds route through per-device BoundedChannel to ChannelConsumerService before ISender.Send() (verifiable by code structure and log sequence)
- New pipeline counters: `snmp.trap.auth_failed`, `snmp.trap.unknown_device`, `snmp.trap.dropped` — all with `site_name` label, consistent with existing PipelineMetricService pattern
- Phase 5/6 MUST use ISender.Send(snmpOidReceived) not IPublisher.Publish — IPublisher.Publish bypasses the entire behavior pipeline (decision from Phase 3)
- SnmpOidReceived.Source should be "trap" for trap-originated events

</specifics>

<deferred>
## Deferred Ideas

- SNMP Inform acknowledgment — out of scope for v2c trap reception (documented in Out of Scope)
- SNMPv3 trap authentication (USM) — v2 requirement (ADV-02)
- Hot-reloadable device configuration for trap listener — v2 requirement (OPS-01)

</deferred>

---

*Phase: 05-trap-ingestion*
*Context gathered: 2026-03-05*
