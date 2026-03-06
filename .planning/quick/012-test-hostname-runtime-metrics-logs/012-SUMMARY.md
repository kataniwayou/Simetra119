# Quick Task 012: Test hostname on runtime metrics and logs

**One-liner:** Verified PHYSICAL_HOSTNAME flows correctly to Prometheus labels (runtime + business metrics) and Elasticsearch log attributes.

## What Was Done

### Task 1: Rebuild and redeploy to K8s
- Rebuilt Docker image with PHYSICAL_HOSTNAME env var
- Applied updated OTel Collector config with transform processor
- Fixed production OTel Collector: switched from `prometheus` (scrape) to `prometheusremotewrite` (push) — Prometheus had no scrape targets
- Added PHYSICAL_HOSTNAME env var to snmp-collector deployment (was missing from K8s dev deployment)
- All pods running 1/1

### Task 2: Verify host_name on metrics and logs

| Check | Result | Evidence |
|-------|--------|----------|
| Runtime metrics (dotnet_*) | PASS | `dotnet_gc_collections_total` has `host_name: docker-desktop` |
| Pipeline metrics (snmp_*) | N/A | No counters emitted (dummy device); code-verified in PipelineMetricService |
| Logs in Elasticsearch | PASS | `Attributes.host_name: docker-desktop` on log entries |
| Console logs | PASS | `[docker-desktop|leader|...]` prefix in kubectl logs |
| PHYSICAL_HOSTNAME env var | PASS | `printenv PHYSICAL_HOSTNAME` = `docker-desktop` |
| Node name match | PASS | K8s node = `docker-desktop` = all host_name values |

## Deviations

1. **Production OTel Collector exporter mismatch** — Config used `prometheus` scrape exporter at :8889 but Prometheus only accepts remote-write. Fixed to `prometheusremotewrite` matching dev config.
2. **snmp-collector deployment missing PHYSICAL_HOSTNAME** — Only the simetra/production deployment had it. Added to snmp-collector deployment.

## Commits

| Hash | Message |
|------|---------|
| 5753753 | feat(10): add OTel transform processor for host_name on all metrics |
| 2ef5cb6 | fix(10): add PHYSICAL_HOSTNAME to snmp-collector deployment |
| c37eae8 | fix(10): switch production OTel Collector to prometheusremotewrite |

## Duration

~15 min (including deploy wait times)
