# Phase 3: MediatR Pipeline and Instruments - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

The complete MediatR behavior chain and all three OTel metric instruments are built, wired, and unit-testable with synthetic `SnmpOidReceived` notifications — so the pipeline is fully verified before any real network traffic arrives. Counter delta computation is Phase 4. Trap listener is Phase 5. Poll scheduler is Phase 6.

</domain>

<decisions>
## Implementation Decisions

### Notification shape
- SnmpOidReceived is a **mutable class** (not a record) — behaviors enrich it in-place as it flows through the pipeline (standard MediatR notification pattern)
- Carries: OID (string), AgentIp (IPAddress), Value (ISnmpData from SharpSnmpLib — the library's typed wrapper), Source (enum: Poll or Trap), TypeCode (SnmpType from SharpSnmpLib)
- Notification starts with raw OID only — **OidResolutionBehavior** populates `MetricName` (nullable string, null until resolved) mid-pipeline
- Both `AgentIp` and `DeviceName` are present on the notification — poll path sets DeviceName at publish time (it knows the device), trap path sets AgentIp (resolved to DeviceName by a behavior using DeviceRegistry)
- Value represented as SharpSnmpLib's `ISnmpData` — the library already provides typed access (`.ToInt32()`, `.ToString()`, etc.) so no custom wrapper needed

### Validation rules
- OID format: simple dot-notation regex check (digits separated by dots, minimum 2 arcs like `1.3`) — not full RFC 2578 validation. Pragmatic for monitoring context.
- IP validation: `IPAddress.TryParse` — same approach used in DevicesOptionsValidator
- Unknown devices (IP not in registry): **reject with Warning log** — only configured devices get metrics. Traps from unknown IPs are dropped. This prevents cardinality explosion from rogue trap sources.
- Value range validation: **trust SharpSnmpLib** — the library validates during deserialization. Double-checking adds no value.
- Rejection logging: log OID + IP + reason at Warning level — operational debuggability outweighs noise concern (rejected events are already rare edge cases)

### TypeCode-to-instrument dispatch
- **snmp_gauge**: Integer32, Gauge32, TimeTicks — TimeTicks treated as gauge (operators want current uptime value, not deltas)
- **snmp_counter**: Counter32, Counter64 — these go through the delta engine (Phase 4) before recording
- **snmp_info**: OctetString, IpAddress, ObjectIdentifier — gauge=1 with string in `value` label
- Unrecognized TypeCodes (Opaque, NoSuchObject, EndOfMibView, NoSuchInstance): **drop with Warning log** — these are SNMP error/edge conditions, not metric data
- Integer32 and Gauge32 treated identically (both to snmp_gauge) — OTel gauges handle negative values natively
- snmp_info value label: **truncate at 128 characters** with `...` suffix — prevents cardinality explosion from unexpectedly long OctetStrings while keeping most values intact

### Pipeline metrics scope
- Pipeline counters (`snmp.event.published`, `snmp.event.handled`, `snmp.event.errors`, `snmp.event.rejected`): **no source tag** — aggregate only. Source distinction is on business metrics via the existing `source` label.
- `snmp.event.errors`: **flat counter** — no error_type tag. Error details go in structured logs, not metric labels. Keeps cardinality minimal.
- `snmp.event.published`: incremented at the **Publish() call site** (trap consumer and poll job), not inside a behavior. True count of everything entering the pipeline.
- All 6 pipeline counter instruments (`snmp.event.published`, `snmp.event.handled`, `snmp.event.errors`, `snmp.event.rejected`, `snmp.poll.executed`, `snmp.trap.received`) **created in Phase 3** — Phase 5/6 just call `.Add(1)`. Clean separation of instrument definition vs usage.
- All pipeline metrics include `site_name` label only (from SiteOptions)

### Claude's Discretion
- Exact MediatR behavior registration order implementation (IPipelineBehavior<T> registration sequence)
- MetricFactory internal caching strategy for instrument instances
- Test helper design for verifying behavior order and metric recording
- Whether to use a dedicated SnmpSource enum or reuse string constants

</decisions>

<specifics>
## Specific Ideas

- The three business instruments (snmp_gauge, snmp_counter, snmp_info) share the same 5-label taxonomy from Phase 2: `site_name`, `metric_name`, `oid`, `agent`, `source`
- Counter values pass through the pipeline but are NOT recorded to snmp_counter in Phase 3 — the delta engine (Phase 4) must exist first. Phase 3 should either skip counter recording or store a placeholder that Phase 4 replaces.
- TaskWhenAllPublisher from MediatR allows multiple handlers to process the same notification. Error in one handler must not kill others (per-handler error isolation).
- MediatR v12.5.0 specifically — do NOT use v13+ (RPL-1.5 commercial license, locked decision from Init)

</specifics>

<deferred>
## Deferred Ideas

- Counter delta computation for snmp_counter values — Phase 4
- Actual trap reception and parsing — Phase 5
- Actual SNMP GET polling — Phase 6
- Per-handler timeout or circuit breaker — not needed for v1 (handlers are fast, in-process)

</deferred>

---

*Phase: 03-mediatr-pipeline-and-instruments*
*Context gathered: 2026-03-05*
