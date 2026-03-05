---
phase: 09-containerized-integration-testing
plan: 01
subsystem: infra
tags: [otel-collector, prometheus, remote_write, prometheusremotewrite, kubernetes, monitoring]

# Dependency graph
requires:
  - phase: 01-infrastructure-foundation
    provides: "prometheusremotewrite decision [01-02] - push pipeline architecture mandate"
provides:
  - "OTel Collector ConfigMap with prometheusremotewrite exporter pushing to http://prometheus:9090/api/v1/write"
  - "OTel Collector Deployment without prom-exporter port (8889 removed)"
  - "Prometheus ConfigMap with push-only config (no scrape_configs)"
  - "Prometheus Deployment with --web.enable-remote-write-receiver flag"
affects:
  - 09-02-containerized-integration-testing
  - 09-03-containerized-integration-testing

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Push-based OTel metrics pipeline: OTLP gRPC -> OTel Collector -> prometheusremotewrite -> Prometheus /api/v1/write"
    - "resource_to_telemetry_conversion propagates OTel resource attributes as Prometheus labels"

key-files:
  created: []
  modified:
    - deploy/k8s/monitoring/otel-collector-configmap.yaml
    - deploy/k8s/monitoring/otel-collector-deployment.yaml
    - deploy/k8s/monitoring/prometheus-configmap.yaml
    - deploy/k8s/monitoring/prometheus-deployment.yaml

key-decisions:
  - "prometheusremotewrite exporter replaces prometheus scrape exporter (decision [01-02] enforced)"
  - "Prometheus --web.enable-remote-write-receiver mandatory to accept remote_write POSTs (HTTP 405 without it)"
  - "resource_to_telemetry_conversion.enabled: true preserves OTel resource attributes as Prometheus labels"
  - "No scrape_configs in Prometheus config - push-only pipeline eliminates polling"
  - "Port 8889 removed from OTel Collector - no longer an active listener in push model"

patterns-established:
  - "Push pipeline: OTel Collector writes to Prometheus via HTTP POST (not Prometheus pulling from OTel)"
  - "Prometheus global block retained even without scrape_configs - required by Prometheus startup"

# Metrics
duration: 1min
completed: 2026-03-05
---

# Phase 9 Plan 01: Fix Monitoring Stack for Push Pipeline Summary

**OTel Collector switched from scrape-based prometheus exporter (port 8889) to prometheusremotewrite pushing to http://prometheus:9090/api/v1/write, with Prometheus enabled to accept remote_write via --web.enable-remote-write-receiver**

## Performance

- **Duration:** ~1 min
- **Started:** 2026-03-05T19:41:38Z
- **Completed:** 2026-03-05T19:42:42Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- OTel Collector ConfigMap now uses `prometheusremotewrite` exporter with `resource_to_telemetry_conversion.enabled: true`, targeting `http://prometheus:9090/api/v1/write`
- OTel Collector Deployment and Service cleaned of port 8889 (prom-exporter no longer needed in push model)
- Prometheus ConfigMap reduced to push-only config (global block only, no scrape_configs)
- Prometheus Deployment now starts with `--web.enable-remote-write-receiver` and explicit `--config.file` args

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix OTel Collector ConfigMap and Deployment** - `33686c5` (feat)
2. **Task 2: Fix Prometheus ConfigMap and Deployment** - `15c2793` (feat)

**Plan metadata:** (see final commit below)

## Files Created/Modified

- `deploy/k8s/monitoring/otel-collector-configmap.yaml` - prometheusremotewrite exporter replacing prometheus scrape exporter; metrics pipeline updated
- `deploy/k8s/monitoring/otel-collector-deployment.yaml` - port 8889 (prom-exporter) removed from container spec and Service; port 4317 (otlp-grpc) retained
- `deploy/k8s/monitoring/prometheus-configmap.yaml` - scrape_configs section removed entirely; global block retained
- `deploy/k8s/monitoring/prometheus-deployment.yaml` - args added: --config.file and --web.enable-remote-write-receiver

## Decisions Made

- The existing manifests were inherited from the Simetra reference project and used a scrape-based pipeline (OTel exposes :8889, Prometheus polls it). Decision [01-02] mandated prometheusremotewrite (push-based). This plan enforces that decision at the manifest level.
- `resource_to_telemetry_conversion.enabled: true` carried over from the prometheus exporter block to prometheusremotewrite to ensure OTel resource attributes (site_name, pod_identity, service_name) propagate as Prometheus labels.
- Prometheus `global` block kept even with empty scrape_configs removed - Prometheus requires at minimum scrape_interval and evaluation_interval for internal operation.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Monitoring stack is correctly wired for the push pipeline. OTLP metrics sent to OTel Collector :4317 will be forwarded to Prometheus via HTTP POST to :9090/api/v1/write.
- Phase 9 Plan 02 can proceed: the K8s namespace configuration and SnmpCollector deployment manifests can now reference a correctly-configured monitoring stack.
- No blockers or concerns.

---
*Phase: 09-containerized-integration-testing*
*Completed: 2026-03-05*
