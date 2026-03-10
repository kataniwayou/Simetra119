# Phase 27: Pipeline Integration - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Fan-out behavior that routes resolved SNMP samples from the existing MediatR pipeline into matching tenant vector slots, without disrupting existing OTel export. Includes shared value extraction, heartbeat normalization, and a pipeline counter.

</domain>

<decisions>
## Implementation Decisions

### Value Extraction — Shared Extraction Pattern

- New `ValueExtractionBehavior` in MediatR chain does the TypeCode switch ONCE
- Sets pre-extracted `double ExtractedValue` and `string? ExtractedStringValue` properties on `SnmpOidReceived`
- Both fan-out behavior and OtelMetricHandler consume pre-extracted values (OtelMetricHandler refactored to read them instead of its own switch)
- Follows existing pipeline enrichment pattern: each behavior enriches the message, downstream consumers read enriched data
- For numeric types (Integer32, Gauge32, TimeTicks, Counter32, Counter64): `ExtractedValue` = number, `ExtractedStringValue` = null
- For string types (OctetString, IPAddress, ObjectIdentifier): `ExtractedValue` = 0, `ExtractedStringValue` = string representation
- String truncation (128 chars) stays in OtelMetricHandler only — tenant vector gets full string
- `SnmpType TypeCode` added to `MetricSlot` record — set from `SnmpOidReceived.TypeCode` at write time so consumers know which property is relevant

### Heartbeat Normalization — Remove Special-Casing

- Heartbeat flows through the entire pipeline like any other sample — no early returns, no bypasses
- `IsHeartbeat` flag removed from `SnmpOidReceived` (redundant after normalization)
- Remove IsHeartbeat check from: `ChannelConsumerService`, `OidResolutionBehavior`, `OtelMetricHandler`
- Heartbeat OID (`1.3.6.1.4.1.9999.1.1.1.0`) gets resolved normally via OidMapService
- Heartbeat OID mapping is hardcoded internal — seed `"1.3.6.1.4.1.9999.1.1.1.0" → "heartbeat"` in OidMapService at construction time, not in configurable OID map JSON
- Leader-only export applies to heartbeat metric same as all business metrics (MetricRoleGatedExporter)

### Routing Miss Behavior

- Silent skip on no matching route — no logging, no counter on misses
- Silent skip on port resolution failure (DeviceRegistry.TryGetDeviceByName returns false)
- No success logging on successful writes — counter is sufficient evidence
- Normal path for empty registry (TenantCount == 0) — let TryRoute return false naturally, no special case
- Filter first: check IsHeartbeat and MetricName=="Unknown" BEFORE calling TryRoute (note: after heartbeat normalization, only MetricName=="Unknown" filter remains)
- Fan-out caught exceptions logged at Warning level

### Behavior Chain Ordering

- ValueExtractionBehavior runs after OidResolutionBehavior (needs MetricName resolved)
- TenantVectorFanOutBehavior runs after ValueExtractionBehavior (needs extracted values)
- OtelMetricHandler remains the terminal handler
- Full chain: Logging → Exception → Validation → OidResolution → ValueExtraction → FanOut → OtelMetricHandler

### Pipeline Counter

- `snmp.tenantvector.routed` — plain counter with `device_name` label, consistent with all other pipeline metrics (PMET-01 through PMET-10)
- Registered in `PipelineMetricService` alongside existing counters
- Dotted naming convention, no additional labels beyond `device_name`
- Increments once per successful slot write (fan-out to 3 tenants = 3 increments)

</decisions>

<specifics>
## Specific Ideas

- Pipeline follows progressive enrichment pattern: each behavior adds context, downstream consumers read what they need
- OtelMetricHandler switch on TypeCode to be refactored to read pre-extracted values from SnmpOidReceived
- MetricSlotHolder.WriteValue signature becomes `WriteValue(double value, string? stringValue, SnmpType typeCode)` to pass TypeCode through to MetricSlot
- Heartbeat OID seed in OidMapService must happen before configurable entries load (construction time)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 27-pipeline-integration*
*Context gathered: 2026-03-10*
