# Phase 89: Observability and Deployment Wiring - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Add structured INFO logs at every preferred-election decision point (upgrading existing Debug logs where they exist). Update the primary K8s deployment manifest with pod anti-affinity and PHYSICAL_HOSTNAME Downward API env var. This is the final phase of v3.0.

</domain>

<decisions>
## Implementation Decisions

### Log message content
- Upgrade existing Debug-level logs to Info where they cover decision points — don't add duplicate lines
- Follow existing codebase pattern: plain descriptive messages with structured `{PropertyName}` fields
- No prefix, no event category, no event IDs — consistent with K8sLeaseElection's existing style (e.g. "Acquired leadership for lease {LeaseName}")
- Decision points to cover (SC-1):
  1. Backing off (stamp fresh, not competing)
  2. Competing normally (stamp stale or feature off)
  3. Yielding to preferred pod (stamp became fresh while leading)
  4. Heartbeat stamping started (preferred pod post-readiness)

### Deployment manifest location
- Only `deploy/k8s/snmp-collector/deployment.yaml` gets changes — other manifests are copies/variants
- Add pod anti-affinity rule: `requiredDuringSchedulingIgnoredDuringExecution` with `kubernetes.io/hostname` topology key
- PHYSICAL_HOSTNAME already exists in the manifest (from spec.nodeName) — verify, don't duplicate

### POD_NAMESPACE env var
- Dropped — not needed. Operator sets `LeaseOptions.Namespace` in ConfigMap explicitly. Pod doesn't auto-discover its namespace.
- Only PHYSICAL_HOSTNAME Downward API env var is required (and already exists)

### Claude's Discretion
- Exact log message wording at each decision point
- Whether to add anti-affinity as `matchLabels` on `app: snmp-collector` or a different label selector
- Whether any existing log lines need level changes beyond the 4 decision points

</decisions>

<specifics>
## Specific Ideas

- The 4 decision points span 3 files: K8sLeaseElection.cs (backoff + competing), PreferredHeartbeatJob.cs (yielding + stamping started)
- Existing transition logs in PreferredLeaderService.UpdateStampFreshness may already cover some decision points — check before adding
- Anti-affinity uses the same `app: snmp-collector` label already on the deployment

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 89-observability-and-deployment-wiring*
*Context gathered: 2026-03-26*
