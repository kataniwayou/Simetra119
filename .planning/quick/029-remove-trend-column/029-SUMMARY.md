# Quick-029 Summary: Remove Trend Column

## What Changed
- Removed Query B (`delta(snmp_gauge{...}[30s])`) from gauge table panel
- Removed `merge` transformation
- Removed Value #B override (Trend column with thresholds, value mappings, cell coloring)
- Restored single-query organize transformation

## Files Modified
- `deploy/grafana/dashboards/simetra-business.json` — 111 lines removed

## Commit
- `060f407`: feat(quick-029): remove trend column from gauge metrics table

## Reason
Grafana standard table panels cannot achieve the desired UX: flash cell color on value change for ~1 second then revert to neutral. The Trend column showed persistent colored arrows which didn't match the intended behavior.
