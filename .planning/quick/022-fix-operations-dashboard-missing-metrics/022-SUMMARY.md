# Quick Task 022: Fix Operations Dashboard Missing Metrics Summary

**One-liner:** Fix runtime gauge panel queries (remove _total suffix, remove rate()), add remote-write-receiver flag to production Prometheus

## Changes Made

### Task 1: Fix runtime metric panel queries and names in dashboard JSON
**Commit:** b12d617

Fixed 2 of 6 runtime panels that had incorrect metric names, and removed meaningless thresholds from 3 gauge panels:

- **Thread Pool Threads** (id 20): Removed `_total` suffix from `dotnet_thread_pool_thread_count_total` (UpDownCounter is a gauge, not counter)
- **Thread Pool Queue Length** (id 21): Removed `_total` suffix from `dotnet_thread_pool_queue_length_total` (same reason)
- **Process Working Set** (id 18): Query was already correct (no rate(), correct name, bytes unit) -- only removed meaningless threshold of 80
- **GC Heap Size** (id 19): Already correct, left as-is per plan

Panels NOT changed (correctly using rate() with _total):
- GC Collections (`dotnet_gc_collections_total`)
- GC Pause Time (`dotnet_gc_pause_time_seconds_total`)
- All 11 pipeline counter panels

**Files modified:** `deploy/grafana/dashboards/simetra-operations.json`

### Task 2: Fix production Prometheus missing remote-write-receiver flag
**Commit:** 48647cd

- Added `--web.enable-remote-write-receiver` to production Prometheus container args
- Without this flag, OTel Collector's prometheusremotewrite exporter pushes to `/api/v1/write` but Prometheus rejects writes silently
- Added comment noting `otel-collector:8889` scrape target is dead (OTel Collector uses remote write push, not scrape endpoint)

**Files modified:** `deploy/k8s/production/prometheus.yaml`

## Deviations from Plan

None -- plan executed exactly as written. The "Working Set Memory" panel was already partially fixed (correct query and unit) so only the threshold removal applied.

## Key Technical Details

- .NET 9 `System.Runtime` UpDownCounter instruments map to Prometheus gauges (no `_total` suffix, no rate())
- .NET 9 `System.Runtime` Counter instruments map to Prometheus counters (with `_total` suffix, rate() appropriate)
- OTel metric name dots become underscores in Prometheus
- Production Prometheus needs `--web.enable-remote-write-receiver` because OTel Collector uses `prometheusremotewrite` exporter (push model), not `prometheus` exporter (pull/scrape model)

## Metrics

- **Duration:** ~4 minutes
- **Completed:** 2026-03-08
- **Tasks:** 2/2
