# Phase 45: Structural Prerequisites - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Close data propagation gaps — add `MetricSlotHolder.Role`, `Tenant.Commands`, and `SnmpSource.Command` so all SnapshotJob evaluation logic is buildable. Pure model/enum additions with no behavior changes.

</domain>

<decisions>
## Implementation Decisions

### Role representation
- String constant ("Evaluate"/"Resolved"), NOT an enum — matches existing `MetricSlotOptions.Role` field
- Read-only property: `public string Role { get; }` — set once in constructor, immutable like Ip/Port/MetricName
- Always required — TEN-13 validation already skips metrics without valid Role at config load time, so every MetricSlotHolder in the registry has a valid Role
- Carries over during TenantVectorRegistry.Reload — same as other immutable holder properties

### Tenant.Commands shape
- Flat `IReadOnlyList<CommandSlotOptions>` — direct list from TenantOptions.Commands, not grouped by device
- Suppression window is **per-tenant** on `TenantOptions.SuppressionWindowSeconds` (default 60s), NOT global on SnapshotJobOptions
- Commands are **re-read from new config** on reload — operator can add/remove commands via hot-reload (unlike holders which carry over time series)

### SnmpSource.Command pipeline behavior
- `SnmpSource.Command` added as new enum value — SET response acknowledgments flow through full MediatR pipeline
- SET response OIDs resolved via **oid_command_map** (ICommandMapService), NOT oid_metric_map (IOidMapService)
- CommandWorker **pre-sets MetricName** from ICommandMapService before dispatching to pipeline
- OidResolutionBehavior bypass: use **MetricName-already-set guard** (`if MetricName is already set and valid, skip resolution`) — NOT Source-based conditions. This replaces the existing Synthetic Source check too. Data-driven, not Source-coupled.
- SET responses **DO fan out** via TenantVectorFanOutBehavior — a SET acknowledgment value can land in multiple tenant slots as a Resolved metric (e.g., two tenants watching same device)
- `metric_name` label keeps its current name for now — rename to `resolved_name` deferred to Phase 50 (breaking change)

### Claude's Discretion
- Exact constructor parameter ordering for MetricSlotHolder
- Whether to add Role to MetricSlot record or keep it on holder only
- Test organization for the new properties

</decisions>

<specifics>
## Specific Ideas

- The OidResolutionBehavior refactor (MetricName-already-set guard) should also clean up the existing Synthetic bypass to use the same pattern — no Source-specific conditions in OidResolutionBehavior at all
- SET response is an "acknowledgment with value" — not a metric in the domain sense, but gets the same pipeline treatment. Distinguished by `source="Command"` label and resolution through command map instead of metric map.

</specifics>

<deferred>
## Deferred Ideas

- **Phase 50: Rename metric_name label to resolved_name** — breaking change affecting all dashboards and PromQL queries. Captures that the label serves both metric names (from oid_metric_map) and command names (from oid_command_map).

</deferred>

---

*Phase: 45-structural-prerequisites*
*Context gathered: 2026-03-16*
