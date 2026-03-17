---
phase: 51-simulator-http-control-endpoint
plan: "02"
subsystem: infra
tags: [aiohttp, docker, kubernetes, snmp-simulator, port-8080]

# Dependency graph
requires:
  - phase: 51-simulator-http-control-endpoint
    provides: Plan 51-01 simulator HTTP control foundation context
provides:
  - aiohttp==3.13.3 in simulator requirements.txt (pip install ready)
  - Dockerfile EXPOSE 8080/tcp alongside existing EXPOSE 161/udp
  - K8s Deployment containerPort 8080 named http-control (TCP)
  - K8s Service port 8080 targeting named port http-control (TCP)
affects:
  - 51-simulator-http-control-endpoint (plan 03+)
  - 52-e2e-test-scenarios
  - Any phase using kubectl port-forward to simulator HTTP endpoint

# Tech tracking
tech-stack:
  added: [aiohttp==3.13.3]
  patterns: [named-port-reference (containerPort name -> targetPort name in Service)]

key-files:
  created: []
  modified:
    - simulators/e2e-sim/requirements.txt
    - simulators/e2e-sim/Dockerfile
    - deploy/k8s/simulators/e2e-sim-deployment.yaml

key-decisions:
  - "Named port 'http-control' used in both Deployment and Service for stable targetPort reference"

patterns-established:
  - "Named port pattern: Deployment containerPort name matches Service targetPort string — avoids breakage if port number changes"

# Metrics
duration: 2min
completed: 2026-03-17
---

# Phase 51 Plan 02: Simulator Infrastructure for HTTP Control Endpoint Summary

**aiohttp==3.13.3 added to simulator image and TCP 8080 exposed in Dockerfile, K8s Deployment, and Service via named port 'http-control'**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-17T10:54:14Z
- **Completed:** 2026-03-17T10:55:07Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- requirements.txt updated with aiohttp==3.13.3 so pip install picks it up at image build time
- Dockerfile gains EXPOSE 8080/tcp alongside the existing EXPOSE 161/udp
- K8s Deployment containerPort and Service port both reference the named port http-control; kubectl apply --dry-run=client validates cleanly

## Task Commits

Each task was committed atomically:

1. **Task 1: Update requirements.txt and Dockerfile** - `74d40cc` (chore)
2. **Task 2: Add HTTP port to K8s Deployment and Service** - `a74ef1e` (chore)

## Files Created/Modified

- `simulators/e2e-sim/requirements.txt` - Added aiohttp==3.13.3 dependency
- `simulators/e2e-sim/Dockerfile` - Added EXPOSE 8080/tcp after EXPOSE 161/udp
- `deploy/k8s/simulators/e2e-sim-deployment.yaml` - Deployment containerPort 8080 (http-control/TCP) and Service port 8080 (targetPort: http-control/TCP)

## Decisions Made

- Used named port reference (`targetPort: http-control`) in the Service rather than a numeric targetPort — consistent with the existing SNMP port pattern and resilient to port number changes.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Image build infrastructure ready: next plan can implement the aiohttp server logic in e2e_simulator.py
- Port-forward path complete: `kubectl port-forward svc/e2e-simulator 8080:8080` will succeed once the pod is deployed with the updated manifest
- No blockers

---
*Phase: 51-simulator-http-control-endpoint*
*Completed: 2026-03-17*
