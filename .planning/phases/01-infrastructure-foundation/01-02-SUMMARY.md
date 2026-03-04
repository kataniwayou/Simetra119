---
phase: 01-infrastructure-foundation
plan: 02
subsystem: infra
tags: [docker-compose, otel-collector, prometheus, grafana, otlp, prometheusremotewrite, remote_write]

# Dependency graph
requires: []
provides:
  - Docker Compose local dev stack with OTel Collector, Prometheus, Grafana
  - OTLP gRPC push receiver on port 4317
  - Prometheus remote_write ingestion (no scrape endpoints)
  - Grafana auto-provisioned with Prometheus datasource
affects:
  - 01-03 (application OTLP push wiring)
  - 01-04 (metric instruments use this collector endpoint)
  - 01-05 (integration tests target 4317)
  - phase-02 (cardinality locking targets this infrastructure)
  - phase-03 (all metric instruments push through this pipeline)

# Tech tracking
tech-stack:
  added:
    - otel/opentelemetry-collector-contrib:0.120.0
    - prom/prometheus:v3.2.1
    - grafana/grafana:latest
  patterns:
    - "OTLP push pipeline: app -> OTLP gRPC :4317 -> collector -> remote_write -> Prometheus (no scrape anywhere)"
    - "prometheusremotewrite exporter with resource_to_telemetry_conversion for label propagation"
    - "Grafana datasource auto-provisioning via provisioning/datasources/*.yaml"

key-files:
  created:
    - deploy/docker-compose.yml
    - deploy/otel-collector-config.yaml
    - deploy/prometheus.yml
    - deploy/grafana/provisioning/datasources/prometheus.yaml
  modified: []

key-decisions:
  - "prometheusremotewrite exporter used (not prometheus scrape exporter) — PUSH-03 compliance, no scrape endpoints anywhere"
  - "otel/opentelemetry-collector-contrib:0.120.0 required (not core) — prometheusremotewrite is contrib-only"
  - "Prometheus --web.enable-remote-write-receiver flag required — without it Prometheus returns HTTP 405 on all pushes"
  - "resource_to_telemetry_conversion.enabled: true — propagates OTel resource attributes as Prometheus labels"
  - "Grafana anonymous Admin enabled for local dev convenience"

patterns-established:
  - "Push-only pipeline: No scrape_configs in prometheus.yml, no prometheus exporter in collector config"
  - "Debug exporter on logs pipeline for Phase 1 visibility without a log backend"

# Metrics
duration: 2min
completed: 2026-03-05
---

# Phase 1 Plan 2: Docker Compose Local Dev Telemetry Stack Summary

**OTel Collector (contrib 0.120.0) receiving OTLP gRPC on :4317 and pushing metrics via prometheusremotewrite to Prometheus v3.2.1, with Grafana auto-provisioned — full push-only telemetry pipeline for local development**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-04T22:39:56Z
- **Completed:** 2026-03-04T22:41:27Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Created `deploy/otel-collector-config.yaml` with OTLP gRPC receiver, prometheusremotewrite exporter (push-based, PUSH-03 compliant), and debug exporter for log pipeline
- Created `deploy/prometheus.yml` with no scrape_configs — Prometheus configured as pure remote_write ingestion target
- Created `deploy/docker-compose.yml` with three-service stack using correct contrib image and `--web.enable-remote-write-receiver` flag
- Created `deploy/grafana/provisioning/datasources/prometheus.yaml` auto-provisioning Prometheus datasource on first Grafana startup

## Task Commits

Each task was committed atomically:

1. **Task 1: Create OTel Collector config and Prometheus config** - `7932974` (feat)
2. **Task 2: Create Docker Compose and Grafana provisioning** - `2f728f2` (feat)

**Plan metadata:** (pending docs commit)

## Files Created/Modified

- `deploy/otel-collector-config.yaml` - OTel Collector: OTLP gRPC receiver + prometheusremotewrite + debug log exporter
- `deploy/prometheus.yml` - Minimal Prometheus config, no scrape targets, accepts remote_write
- `deploy/docker-compose.yml` - Three-service local stack: otel-collector, prometheus, grafana
- `deploy/grafana/provisioning/datasources/prometheus.yaml` - Auto-provisioned Prometheus datasource for Grafana

## Decisions Made

- **prometheusremotewrite over prometheus exporter:** The `prometheus` exporter creates a scrape endpoint at :8889 which violates PUSH-03 (no scrape endpoints anywhere). The `prometheusremotewrite` exporter pushes data to Prometheus via HTTP remote_write API.
- **contrib image mandatory:** `prometheusremotewrite` exporter is not available in `otel/opentelemetry-collector` (core). Must use `otel/opentelemetry-collector-contrib`.
- **--web.enable-remote-write-receiver critical:** Without this flag, Prometheus returns HTTP 405 on all remote_write POST requests, silently rejecting all metrics.
- **resource_to_telemetry_conversion enabled:** Converts OTel resource attributes (service.name, host.name, etc.) into Prometheus labels, enabling proper filtering in Grafana.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required. Run `docker compose -f deploy/docker-compose.yml up -d` to start the full local telemetry pipeline.

## Next Phase Readiness

- Local telemetry stack is ready for application OTLP push wiring (Plan 01-03)
- OTel Collector endpoint `localhost:4317` is the target for application OTLP exporter configuration
- Grafana accessible at `localhost:3000` (no login required, anonymous Admin)
- Prometheus accessible at `localhost:9090`
- No blockers for subsequent plans in Phase 1

---
*Phase: 01-infrastructure-foundation*
*Completed: 2026-03-05*
