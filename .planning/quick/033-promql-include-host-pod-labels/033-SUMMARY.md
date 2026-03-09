# Quick-033 Summary: Include Host/Pod Labels in PromQL Column

## What Changed
- Extended label_join in both gauge and info tables to include 4 labels: metric_name, device_name, k8s_pod_name, service_instance_id
- PromQL string now produces: `snmp_gauge{metric_name="X", device_name="Y", k8s_pod_name="Z", service_instance_id="W"}`
- Regex updated to capture 4 groups: `([^~]*)~([^~]*)~([^~]*)~([^~]*)`

## Files Modified
- `deploy/grafana/dashboards/simetra-business.json`

## Commit
- `2f74fd6`: feat(quick-033): include host/pod labels in PromQL column
