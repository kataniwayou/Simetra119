# Phase 10 Plan 06: Fix host_name Resolution to NODE_NAME Summary

**One-liner:** Replace HOSTNAME env var with NODE_NAME (K8s Downward API spec.nodeName) for persistent physical server identity across pod restarts.

## Metadata

| Field | Value |
|-------|-------|
| Phase | 10-metrics |
| Plan | 06 |
| Type | gap-closure |
| Duration | ~2.5 min |
| Completed | 2026-03-06 |

## What Was Done

### Task 1: Replace HOSTNAME with NODE_NAME in all host resolution points
- Changed `Environment.GetEnvironmentVariable("HOSTNAME")` to `Environment.GetEnvironmentVariable("NODE_NAME")` in 6 files
- SnmpMetricFactory, PipelineMetricService, SnmpConsoleFormatter: direct host_name resolution
- ServiceCollectionExtensions: OTel resource serviceInstanceId (metrics + logging) and log enrichment processor
- SnmpLogEnrichmentProcessor: updated XML doc comment
- CardinalityAuditService: updated label taxonomy documentation (XML doc + log message)
- PodIdentity PostConfigure intentionally left using HOSTNAME (pod name identity)
- **Commit:** d0c86ea

### Task 2: Add NODE_NAME Downward API env var to K8s deployment YAMLs
- Added NODE_NAME env var using Kubernetes Downward API `spec.nodeName` to both:
  - `deploy/k8s/deployment.yaml` (dev)
  - `deploy/k8s/production/deployment.yaml` (production)
- Placed after existing Site__PodIdentity entry
- **Commit:** 2d2cbeb

## Files Modified

| File | Change |
|------|--------|
| src/SnmpCollector/Telemetry/SnmpMetricFactory.cs | HOSTNAME -> NODE_NAME |
| src/SnmpCollector/Telemetry/PipelineMetricService.cs | HOSTNAME -> NODE_NAME |
| src/SnmpCollector/Telemetry/SnmpConsoleFormatter.cs | HOSTNAME -> NODE_NAME |
| src/SnmpCollector/Telemetry/SnmpLogEnrichmentProcessor.cs | Updated doc comment |
| src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs | HOSTNAME -> NODE_NAME (3 locations) |
| src/SnmpCollector/Pipeline/CardinalityAuditService.cs | Updated label taxonomy docs |
| deploy/k8s/deployment.yaml | Added NODE_NAME Downward API env var |
| deploy/k8s/production/deployment.yaml | Added NODE_NAME Downward API env var |

## Verification Results

| Check | Result |
|-------|--------|
| HOSTNAME only in PodIdentity | PASS (1 hit: PostConfigure block) |
| NODE_NAME in all expected files | PASS (5 source files + 2 doc references) |
| spec.nodeName in both YAMLs | PASS (2 hits) |
| dotnet build | PASS (0 warnings, 0 errors) |
| dotnet test | PASS (115/115) |

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| NODE_NAME env var (not HOSTNAME) for host_name label | HOSTNAME in K8s = pod name (changes on restart); NODE_NAME = spec.nodeName = physical K8s node hostname (persistent, unique per server) |
| PodIdentity retains HOSTNAME | Pod identity correctly tracks the pod name; host_name tracks the physical server |

## Deviations from Plan

None -- plan executed exactly as written.
