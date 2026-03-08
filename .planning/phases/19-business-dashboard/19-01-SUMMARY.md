---
phase: 19-business-dashboard
plan: 01
subsystem: ui
tags: [grafana, dashboard, prometheus, snmp, table-panel]

# Dependency graph
requires:
  - phase: 18-operations-dashboard
    provides: "Dashboard JSON patterns (__inputs, DS_PROMETHEUS, table panels, field overrides, template variables)"
provides:
  - "Simetra Business dashboard JSON with gauge and info metric tables"
  - "Device filter dropdown for dynamic multi-device selection"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Info metric table pattern: hide numeric Value #A, show value label column"
    - "Organize transformation for column ordering in table panels"

key-files:
  created:
    - deploy/grafana/dashboards/simetra-business.json
  modified: []

key-decisions:
  - "Used organize transformation for column ordering instead of relying on Prometheus label order"
  - "Hid snmp_type column in info table since info types are less meaningful to business users"

patterns-established:
  - "Business dashboard pattern: device-centric tables with device_name filter variable"

# Metrics
duration: 3min
completed: 2026-03-08
---

# Phase 19 Plan 01: Business Dashboard Summary

**Grafana business dashboard with gauge and info SNMP metric tables, device filter dropdown, and auto-refresh**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-08T13:29:57Z
- **Completed:** 2026-03-08T13:33:00Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Created simetra-business.json with 4 panels (2 row headers + 2 table panels)
- Gauge table shows service_instance_id, device_name, metric_name, oid, snmp_type, and plain Value
- Info table shows service_instance_id, device_name, metric_name, oid, and value label (hides numeric 1.0)
- Device filter dropdown populated from label_values(snmp_gauge, device_name) with multi-select and "All" default
- Both tables use instant queries with format "table" and device_name=~"$device" filter
- Auto-refresh 5s, default range 15m, all datasources via ${DS_PROMETHEUS}

## Task Commits

Each task was committed atomically:

1. **Task 1: Create business dashboard JSON file** - `91d58c3` (feat)

## Files Created/Modified
- `deploy/grafana/dashboards/simetra-business.json` - Complete Grafana business dashboard with gauge and info metric tables

## Decisions Made
- Used "organize" transformation (indexByName) for explicit column ordering in both tables, consistent with Grafana best practices
- Hid snmp_type column in info table since info type labels (octetstring, ipaddress, objectidentifier) are less relevant for business view
- Renamed "Value #A" to "Value" in gauge table for cleaner display

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - dashboard is a static JSON file imported manually into Grafana UI.

## Next Phase Readiness
- Business dashboard ready for import into Grafana
- Both dashboards (operations + business) complete for v1.3 milestone

---
*Phase: 19-business-dashboard*
*Completed: 2026-03-08*
