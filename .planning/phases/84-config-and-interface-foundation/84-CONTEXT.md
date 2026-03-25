# Phase 84: Config and Interface Foundation - Context

**Gathered:** 2026-03-25
**Status:** Ready for planning

<domain>
## Phase Boundary

Declare the configuration model and interfaces for the two-lease preferred leader mechanism. No behavioral changes — just config fields, interfaces, DI registration, and startup validation. All downstream phases depend on these contracts.

</domain>

<decisions>
## Implementation Decisions

### Config structure
- Extend existing `LeaseOptions` with `PreferredNode` field (no new options class)
- Heartbeat lease name derived automatically: `"{LeaseOptions.Name}-preferred"` (not configurable)
- Heartbeat lease shares `DurationSeconds` and `RenewIntervalSeconds` with leadership lease (no separate timing fields)
- Feature disabled when `PreferredNode` is absent or empty — backward compatible

### Interface design
- `IPreferredStampReader` exposes only `bool IsPreferredStampFresh` — no stamp age, no preferred pod identity
- Consumers (K8sLeaseElection gates) read an in-memory bool, zero network calls in the gate path

### Preferred identity logic
- Exact case-sensitive string match: `NODE_NAME == PreferredNode`
- Determined once at startup, stored as readonly `_isPreferredPod` — not re-evaluated
- When PreferredNode is configured but NODE_NAME env var is empty: log warning, disable feature (treat as non-preferred, no crash)
- When no pod matches PreferredNode: log info, all pods proceed as non-preferred (degrades to standard fair election)

### Validation
- No startup validation for heartbeat lease name collision — derived name always differs (appends `-preferred`)
- No validation that PreferredNode exists as a K8s node — trust the operator
- No node API calls at startup

### Local dev
- PreferredNode config silently ignored when `AlwaysLeaderElection` is active (no heartbeat service in local dev)

### Namespace
- CFG-03 dropped — `LeaseOptions.Namespace` already exists as a config field. Operator sets it to the correct namespace at deployment time. No code change needed.

### Claude's Discretion
- Exact property names on LeaseOptions (PreferredNode vs PreferredNodeName)
- Whether `_isPreferredPod` lives on the heartbeat service or a shared options wrapper
- DI registration order for new types

</decisions>

<specifics>
## Specific Ideas

- Two leases: leadership (`snmp-collector-leader`) + heartbeat (`snmp-collector-leader-preferred`), both in same namespace
- NODE_NAME env var injected via K8s Downward API (`spec.nodeName`) in deployment manifest
- The interface `IPreferredStampReader` is the contract between the heartbeat service (writer) and election gates (reader)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 84-config-and-interface-foundation*
*Context gathered: 2026-03-25*
