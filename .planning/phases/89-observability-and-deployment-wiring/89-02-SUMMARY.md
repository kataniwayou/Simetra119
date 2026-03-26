---
phase: 89-observability-and-deployment-wiring
plan: 02
subsystem: infra
tags: [kubernetes, deployment, anti-affinity, downward-api, pod-topology]

# Dependency graph
requires:
  - phase: 89-01
    provides: Preferred election observability logs (K8sLeaseElection, PreferredHeartbeatJob)
provides:
  - Hard pod anti-affinity rule enforcing one snmp-collector pod per Kubernetes node
  - Verified PHYSICAL_HOSTNAME Downward API env var present from spec.nodeName
affects: [deploy, k8s, cluster-topology, preferred-leader-election]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Pod anti-affinity: requiredDuringSchedulingIgnoredDuringExecution with kubernetes.io/hostname topology key"
    - "Downward API env var: spec.nodeName injected as PHYSICAL_HOSTNAME"

key-files:
  created: []
  modified:
    - deploy/k8s/snmp-collector/deployment.yaml

key-decisions:
  - "Hard anti-affinity (required, not preferred) — scheduler refuses co-location, never merely discourages it"
  - "Label selector uses app: snmp-collector — matches existing deployment selector and pod labels, no new label needed"
  - "PHYSICAL_HOSTNAME verified present, not duplicated — was already injected from spec.nodeName in prior work"
  - "POD_NAMESPACE omitted — operator sets LeaseOptions.Namespace in ConfigMap explicitly"

patterns-established:
  - "Anti-affinity block placed under spec.template.spec before containers, after terminationGracePeriodSeconds"

# Metrics
duration: 1min
completed: 2026-03-26
---

# Phase 89 Plan 02: Deployment Anti-Affinity Wiring Summary

**Hard pod anti-affinity added to snmp-collector deployment — one pod per Kubernetes node enforced by scheduler, PHYSICAL_HOSTNAME Downward API env var confirmed present from spec.nodeName**

## Performance

- **Duration:** 1 min
- **Started:** 2026-03-26T00:30:25Z
- **Completed:** 2026-03-26T00:30:59Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Added `requiredDuringSchedulingIgnoredDuringExecution` pod anti-affinity to `deploy/k8s/snmp-collector/deployment.yaml`
- Enforces one-pod-per-node topology for preferred leader election to work correctly across sites (DEP-01)
- Confirmed PHYSICAL_HOSTNAME env var (from `spec.nodeName`) already present — not duplicated (DEP-02)
- Confirmed POD_NAMESPACE absent — correctly excluded per context decision

## Task Commits

Each task was committed atomically:

1. **Task 1: Add pod anti-affinity and verify PHYSICAL_HOSTNAME** - `3ed7e11` (feat)

**Plan metadata:** _(pending final docs commit)_

## Files Created/Modified

- `deploy/k8s/snmp-collector/deployment.yaml` — Added 7-line affinity block under `spec.template.spec`; no other sections modified

## Decisions Made

- Used `requiredDuringSchedulingIgnoredDuringExecution` (hard constraint) rather than `preferredDuringSchedulingIgnoredDuringExecution` — the preferred election mechanism requires true one-pod-per-node topology; soft affinity leaves room for co-location under pressure which would break the invariant
- Label selector `app: snmp-collector` chosen — already present on `spec.selector.matchLabels` and `spec.template.metadata.labels`, so no new labels required
- PHYSICAL_HOSTNAME confirmed at lines 38-41 of the original manifest; not added again
- POD_NAMESPACE deliberately absent — `LeaseOptions.Namespace` is configured via ConfigMap, not auto-discovered from the Downward API

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 89 is the final phase of v3.0
- Both plans complete: 89-01 (observability logs) and 89-02 (deployment wiring)
- The deployment manifest is ready for cluster apply — `kubectl apply -f deploy/k8s/snmp-collector/deployment.yaml`
- On a 3-node cluster, the anti-affinity rule will schedule exactly one snmp-collector pod per node (matching `replicas: 3`)
- No blockers — v3.0 Preferred Leader Election is feature-complete

---
*Phase: 89-observability-and-deployment-wiring*
*Completed: 2026-03-26*
