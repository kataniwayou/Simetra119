# Phase 17: Dashboard Provisioning - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Set up Grafana's file-based provisioning so the Prometheus datasource and dashboard JSON files load on Grafana startup. Manual deployment via kubectl apply — no automated ConfigMap watchers or controllers. Dashboard JSON files created in Phases 18-19; this phase only builds the provisioning infrastructure.

</domain>

<decisions>
## Implementation Decisions

### Existing files
- All files under `deploy/grafana/` are stale reference project artifacts — delete and replace entirely
- Old dashboards: `npb-device.json`, `obp-device.json`, `simetra-operations.json` — all invalid
- Old datasource: `simetra-prometheus.yaml` — will be recreated

### Provisioning approach
- Manual deployment only — kubectl apply, not automated ConfigMap watchers
- Grafana file-based provisioning (provisioning YAMLs point to dashboard directory)
- Datasource and dashboard provider configs mounted into Grafana container

### Dashboard lifecycle
- Dashboards deployed as files, loaded by Grafana on startup
- Updates require kubectl apply + Grafana pod restart (or rolling restart)
- No UI editing — provisioned dashboards are read-only in Grafana

### Claude's Discretion
- Exact Grafana volume mount paths and provisioning directory structure
- Whether to use ConfigMaps or direct file mounts for the provisioning configs
- Grafana deployment YAML modifications

</decisions>

<specifics>
## Specific Ideas

- Two dashboards will be created in later phases: simetra-operations (Phase 18) and simetra-business (Phase 19)
- This phase should leave placeholder or empty dashboard directory ready for those phases

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 17-dashboard-provisioning*
*Context gathered: 2026-03-08*
