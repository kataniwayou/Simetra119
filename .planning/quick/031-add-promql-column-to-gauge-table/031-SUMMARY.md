# Quick-031 Summary: Add PromQL Column to Gauge Table

## What Changed
- Wrapped Query A in `label_replace(label_join(...))` to construct a `promql` label per row
- Uses `~` as join delimiter (avoided `|` due to PromQL regex escaping issues)
- Uses PromQL backtick raw strings for the replacement template containing quotes
- Each row now shows a copyable PromQL query like: `snmp_gauge{metric_name="npb_cpu_util", device_name="NPB-01"}`
- Added `promql` column override (renamed to "PromQL") at position 7 in organize transformation

## Files Modified
- `deploy/grafana/dashboards/simetra-business.json`

## Commit
- `212e2cd`: feat(quick-031): add PromQL column to gauge metrics table

## Technical Notes
- `label_join` concatenates metric_name and device_name with `~` delimiter into `__tmp` label
- `label_replace` uses regex `([^~]*)~(.*)` to extract both parts and construct the PromQL string
- `__tmp` label is stripped by Prometheus (double-underscore prefix = internal)
- Backtick strings in PromQL avoid JSON double-escaping issues with quotes
