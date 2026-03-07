# Phase 13 Plan 03: K8s Deployment YAML Alignment Summary

**One-liner:** Updated K8s deployment YAMLs with pysnmp health probes using per-device community strings, DEVICE_NAME env vars, and correct OID references

## What Was Done

### Task 1: Update K8s health probes with new community strings
- Replaced raw hex SNMP packet probes with pysnmp-based health checks in both obp-deployment.yaml and npb-deployment.yaml
- OBP probes use `Simetra.OBP-01` community, querying `obp_link_state_L1` (1.3.6.1.4.1.47477.10.21.1.3.1.0)
- NPB probes use `Simetra.NPB-01` community, querying `npb_cpu_util` (1.3.6.1.4.1.47477.100.1.1.0)
- Added `DEVICE_NAME` env var to both deployments (OBP-01, NPB-01)
- Updated NPB env vars: replaced `TRAP_INTERVAL`/`STATE_CHANGE_INTERVAL` with `TRAP_INTERVAL_MIN`/`TRAP_INTERVAL_MAX`
- **Commit:** 8eae8a7

### Task 2: Update configmap-devices.yaml with correct OID references
- Replaced old NPB OIDs (`47477.100.4.*`) with correct OID trees (`100.1.*` for system metrics, `100.2.*` for per-port metrics)
- Updated OBP OIDs to match oidmap-obp.json (link_state at .1.0, r1_power at .10.0, channel at .4.0)
- Renamed device entries to `NPB-01`/`OBP-01` following DeviceName convention
- Preserved `PLACEHOLDER_NPB_POD_IP` and `PLACEHOLDER_OBP_POD_IP` template placeholders
- **Commit:** 9246b9a

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Use pysnmp for health probes instead of recalculating raw ASN.1 hex packets | More maintainable and less error-prone than manual ASN.1 encoding with community string length changes |
| 2 | Include representative NPB metrics (cpu_util, port_rx_octets_P1, port_status_P1) as template | Phase 14 builds full MetricPoll config; this provides enough for smoke testing |

## Deviations from Plan

None - plan executed exactly as written.

## Verification Results

- No references to `7075626c6963` (hex for "public") in deployment files
- No references to `public` community string in deployment files
- `DEVICE_NAME` env var present in both deployments
- Community strings verified: `Simetra.OBP-01` and `Simetra.NPB-01`
- No old NPB OID tree (`47477.100.4`) in configmap-devices.yaml
- NPB OIDs reference `47477.100.1` and `47477.100.2` correctly
- OBP OIDs reference `47477.10.21` with correct suffixes
- Template placeholders preserved

## Files Modified

| File | Change |
|------|--------|
| deploy/k8s/simulators/obp-deployment.yaml | Pysnmp probes, DEVICE_NAME env var |
| deploy/k8s/simulators/npb-deployment.yaml | Pysnmp probes, DEVICE_NAME env var, updated env vars |
| deploy/k8s/simulators/configmap-devices.yaml | Correct OID references, device name convention |

## Duration

~3 minutes

## Next Phase Readiness

No blockers. Phase 14 (full MetricPoll configuration) can proceed using the template structure established here.
