# Phase 29: K8s Deployment and E2E Validation - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Deploy the tenant vector ConfigMap to the K8s cluster, update deployment.yaml with the volume mount, populate simetra-devices with simulator DNS entries, and verify end-to-end that SNMP data flows through the fan-out pipeline to tenant slots with hot-reload working. Includes a scripted e2e scenario.

</domain>

<decisions>
## Implementation Decisions

### Deployment — Volume Mount

- Add `simetra-tenantvector` as a fourth source in the existing `projected` volume in deployment.yaml
- Follows exact same pattern as simetra-oidmaps and simetra-devices entries already there

### Devices ConfigMap — Populate with Simulator DNS

- Replace REPLACE_ME placeholders in simetra-devices ConfigMap with real simulator K8s DNS entries
- NPB-01: `npb-simulator.simetra.svc.cluster.local` port 161
- OBP-01: `obp-simulator.simetra.svc.cluster.local` port 161
- MetricPolls match the local dev devices.json OIDs and intervals

### Test Tenant Configuration — 3 Tenants at Different Priorities

- 3 tenants with different priorities to prove priority ordering and multi-tenant fan-out:
  1. **npb-trap** (priority 1): 4 NPB metrics that arrive via SNMP traps — Claude picks representative metrics
  2. **npb-poll** (priority 2): 4 NPB metrics that arrive via SNMP polls — Claude picks representative metrics
  3. **obp-poll** (priority 3): 4 OBP metrics that arrive via SNMP polls — Claude picks representative metrics
- IPs in MetricSlotOptions must match the device IpAddress in simetra-devices (K8s DNS names)
- IntervalSeconds should match the poll intervals from devices.json

### Validation Evidence — Prometheus Counter + Pod Logs

- Verify `snmp_tenantvector_routed_total` counter > 0 in Prometheus (proves samples are routing to tenant slots)
- Check pod logs for TenantVectorWatcher reload messages (proves watcher loaded config)
- Counter > 0 is sufficient — no need to wait for a specific threshold

### Hot-Reload Verification — kubectl apply with Added Tenant

- Apply a modified simetra-tenantvector ConfigMap that adds a 4th tenant
- Verify diff log appears in pod logs showing "added: [new-tenant]"
- Use `kubectl apply` (not edit) for scriptability

### E2E Test Automation — Scripted Scenario

- Add a new scripted e2e scenario in tests/e2e/scenarios/ (same pattern as existing e2e tests)
- Script deploys, waits, checks Prometheus counter + pod logs, tests hot-reload, reports pass/fail
- Script also verifies simetra-tenantvector appears in the pod's volume mounts via kubectl describe

### Claude's Discretion

- Specific NPB trap and poll metrics to include in the test tenants
- Specific OBP metrics to include
- E2E script structure and helper usage (reuse existing tests/e2e/lib/ utilities)
- Wait/retry timing in the e2e script

</decisions>

<specifics>
## Specific Ideas

- The projected volume already has 3 sources (snmp-collector-config, simetra-oidmaps, simetra-devices) — simetra-tenantvector is just a 4th source entry
- The devices ConfigMap currently has REPLACE_ME placeholders — this phase populates them with real simulator entries to make the full E2E flow work
- The hot-reload test adds a 4th tenant to prove the watcher detects the change and Reload() logs the diff

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 29-k8s-deployment-and-e2e-validation*
*Context gathered: 2026-03-10*
