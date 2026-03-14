# Phase 33: Config Model Additions - Context

**Gathered:** 2026-03-14
**Status:** Ready for planning

<domain>
## Phase Boundary

Add new C# model types/fields for v1.7: DeviceOptions.Name renamed to CommunityString, CommandSlotOptions data model for tenant commands, optional observability fields (IntervalSeconds, tenant Name). All additions are purely additive — validation and behavioral changes are Phase 34+.

</domain>

<decisions>
## Implementation Decisions

### Tenant JSON Shape
- Metrics[] and Commands[] are flat arrays directly on TenantOptions
- Metric entry: `{ Ip, Port, MetricName, TimeSeriesSize }` — unchanged from current shape
- Command entry: `{ Ip, Port, CommandName, Value, ValueType }` — new model
- NO Device field on tenant entries — not needed
- NO CommunityString field on tenant entries — resolved from DeviceRegistry by IP+Port at load time
- Commands[] required — tenant must have at least one command (empty = skip tenant in Phase 34 validation)
- Each metric entry has a required `Role` field: `"Evaluate"` or `"Resolved"` — validated in Phase 34
- Tenant must have at least one Resolved metric AND at least one Evaluate metric (validated in Phase 34)
- Optional `Name` on TenantOptions for log context; absent = auto-generated `tenant-{index}`
- Optional `IntervalSeconds` on MetricSlotOptions; absent = defaults to 0

### DeviceOptions Rename Mechanics
- `DeviceOptions.Name` renamed to `DeviceOptions.CommunityString`
- JSON field holds full community string: `"Simetra.NPB-01"` (not just device name)
- `DeviceInfo.Name` derived from CommunityString via `CommunityStringHelper.TryExtractDeviceName()` at load time
- `DeviceInfo.CommunityString` stored as-is (full value)
- `_byName` dictionary keyed on extracted short name `"NPB-01"` — all downstream consumers unchanged
- `MetricPollJob` uses `DeviceInfo.CommunityString` directly on the wire — no derivation fallback
- Trap listener unchanged — extracts device name from incoming community string

### CommandSlotOptions Value Field
- `Value` is always a JSON string (e.g. `"1"`, `"10.0.0.1"`, `"hostname"`) — never a native JSON number
- `ValueType` tells the system how to interpret Value for future SET execution
- `ValueType` validated at load time against allowed set: `{ "Integer32", "IpAddress", "OctetString" }` — invalid = skip command entry with Error log
- `Value` is required — empty or null Value = skip command entry with Error log

### Backward Compatibility
- Clean break — old configs (with `Name` instead of `CommunityString`, without `Commands[]`) are not supported after v1.7
- All config files (local dev, K8s ConfigMaps, E2E fixtures) updated atomically in the same commit
- No transition period, no dual-field support

### CommunityString Resolution for Tenants
- TenantVectorRegistry keeps IDeviceRegistry dependency for CommunityString lookup by IP+Port
- TenantVectorRegistry removes IOidMapService dependency (IntervalSeconds comes from config)
- If IP+Port in tenant entry has no matching device in DeviceRegistry = skip entry with Error log
- Operator responsible for ensuring devices.json is applied before tenants.json
- Each file has its own independent watcher — no cross-watcher cascading reloads

### Claude's Discretion
- Exact C# property types and nullability annotations on new models
- Whether CommandSlotOptions is a record or class
- Internal helper method organization for CommunityString extraction in DeviceRegistry

</decisions>

<specifics>
## Specific Ideas

- Tenant JSON format confirmed with exact field names:
  ```json
  { "Ip": "...", "Port": 161, "MetricName": "npb_cpu_util", "TimeSeriesSize": 5, "Role": "Resolved" }
  { "Ip": "...", "Port": 161, "CommandName": "npb_set_x", "Value": "1", "ValueType": "Integer32" }
  ```
- Device JSON format confirmed:
  ```json
  { "CommunityString": "Simetra.NPB-01", "IpAddress": "...", "Port": 161, "Polls": [...] }
  ```
- Operator config ordering: oidmaps/commandmaps -> devices -> tenants (documented, not enforced)

</specifics>

<deferred>
## Deferred Ideas

- SNMP SET command execution using the Commands data model — future milestone
- Cross-watcher cascade reload (OID map change triggers device/tenant reload) — explicitly out of scope

</deferred>

---

*Phase: 33-config-model-additions*
*Context gathered: 2026-03-14*
