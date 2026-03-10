---
phase: 29
plan: 01
subsystem: k8s-manifests
tags: [kubernetes, configmap, deployment, tenantvector, devices]
one-liner: "Add simetra-tenantvector projected source to both deployments and populate tenant/device ConfigMaps with real simulator data and PLACEHOLDER_*_IP markers"

dependency-graph:
  requires: [28-01, 28-02]
  provides:
    - simetra-tenantvector mounted in both deployment.yaml projected volumes
    - production simetra-devices with real DNS entries (no REPLACE_ME)
    - simetra-tenantvector with 3 test tenants (npb-trap, npb-poll, obp-poll)
    - standalone dev simetra-tenantvector.yaml for e2e hot-reload tests
  affects: [29-02, 29-03]

tech-stack:
  added: []
  patterns:
    - Projected ConfigMap volumes — single mountPath /app/config exposes all 4 ConfigMaps

key-files:
  created:
    - deploy/k8s/snmp-collector/simetra-tenantvector.yaml
  modified:
    - deploy/k8s/snmp-collector/deployment.yaml
    - deploy/k8s/production/deployment.yaml
    - deploy/k8s/production/configmap.yaml

decisions:
  - id: D29-01
    decision: "Use PLACEHOLDER_NPB_IP / PLACEHOLDER_OBP_IP instead of real IPs in committed ConfigMap"
    rationale: "IP validator requires valid IPv4; ClusterIPs are cluster-specific and assigned at runtime. E2e script (29-02) substitutes real IPs via kubectl get svc before applying."
    alternatives: ["Commit real IPs (wrong — not portable)", "Use DNS hostnames (wrong — validator rejects non-IP strings)"]

metrics:
  duration: "1m 27s"
  completed: "2026-03-10"
---

# Phase 29 Plan 01: K8s Manifests — Tenant Vector and Device ConfigMaps Summary

Add simetra-tenantvector projected source to both deployment manifests, populate production ConfigMaps with real simulator DNS entries and 3 test tenants using PLACEHOLDER_*_IP markers, and create a standalone dev tenantvector file.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Add simetra-tenantvector projected source to both deployments | c15b7e8 | deploy/k8s/snmp-collector/deployment.yaml, deploy/k8s/production/deployment.yaml |
| 2 | Populate configmaps and create dev tenantvector file | c0fd4da | deploy/k8s/production/configmap.yaml, deploy/k8s/snmp-collector/simetra-tenantvector.yaml |

## Decisions Made

### D29-01: PLACEHOLDER_*_IP in committed ConfigMap

Use `PLACEHOLDER_NPB_IP` and `PLACEHOLDER_OBP_IP` as literal string markers in the committed ConfigMap rather than real IPs.

- `IPAddress.TryParse` rejects DNS hostnames — only valid IPv4 passes validation
- ClusterIPs are runtime-assigned and cluster-specific; they cannot be committed
- The e2e script (plan 29-02) resolves real ClusterIPs via `kubectl get svc` and substitutes before `kubectl apply`
- Committed file serves as documentation: shape is correct, IPs are cluster-specific

## What Was Built

**Task 1 — Projected volume sources updated:**
Both `deploy/k8s/snmp-collector/deployment.yaml` and `deploy/k8s/production/deployment.yaml` now list `simetra-tenantvector` as the 4th entry in the `projected.sources` list under the `config` volume. The existing `volumeMount` for `/app/config` already covers all projected sources — no mount changes needed.

**Task 2 — Production ConfigMap data populated:**
- `simetra-devices` in `deploy/k8s/production/configmap.yaml`: replaced the single REPLACE_ME placeholder device with real simulator device entries copied verbatim from the dev file (OBP-01, NPB-01, E2E-SIM with full OID poll lists)
- `simetra-tenantvector` in `deploy/k8s/production/configmap.yaml`: replaced empty `{ "Tenants": [] }` with 3 test tenants covering NPB trap (priority 1), NPB poll (priority 2), and OBP poll (priority 3) with 12 MetricName values all verified present in simetra-oidmaps

**Standalone dev file created:**
`deploy/k8s/snmp-collector/simetra-tenantvector.yaml` mirrors the production ConfigMap content exactly. Used by e2e plan 29-02 as the restore target after hot-reload mutation tests.

## Verification Results

1. Both deployment.yaml files contain `simetra-tenantvector` in projected sources — PASS
2. No data-bearing REPLACE_ME values in production configmap.yaml (only comment on line 11) — PASS
3. 3 tenant Ids (npb-trap, npb-poll, obp-poll) present in production configmap — PASS
4. 3 tenant Ids present in dev simetra-tenantvector.yaml — PASS
5. All 12 MetricName values exist in simetra-oidmaps section — PASS

## Deviations from Plan

None — plan executed exactly as written.

## Next Phase Readiness

Plan 29-02 (e2e script) can proceed. The e2e script needs to:
1. Resolve NPB and OBP ClusterIPs via `kubectl get svc npb-simulator -n simetra` and `kubectl get svc obp-simulator -n simetra`
2. Substitute PLACEHOLDER_NPB_IP and PLACEHOLDER_OBP_IP before `kubectl apply`
3. Use `deploy/k8s/snmp-collector/simetra-tenantvector.yaml` as the restore target for hot-reload tests (after substitution)
