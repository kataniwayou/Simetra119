# Quick Task 027: Fix Simulator Info/Gauge + Add Static OID Summary

NPB system health metrics converted from OctetString to Gauge32/TimeTicks for correct snmp_gauge classification; 6 static info OIDs added to NPB/OBP simulators and oidmaps.json.

## Completed Tasks

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | NPB simulator -- numeric types + static info OIDs | d918cc7 | simulators/npb/npb_simulator.py |
| 2 | OBP simulator -- static NMU info OIDs | fc398ce | simulators/obp/obp_simulator.py |
| 3 | oidmaps.json + dashboard fixes commit | 3939ba9 | src/SnmpCollector/config/oidmaps.json, deploy/grafana/dashboards/*.json |

## Changes Made

### NPB Simulator
- SYSTEM_METRICS changed from OctetString to Gauge32 (cpu_util, mem_util, sys_temp) and TimeTicks (uptime)
- system_state stores integers: x10 for percentages/temp, x100 centiseconds for uptime
- update_system_health() writes int(float*10) / int(float*100) instead of formatted strings
- Added 3 static info OIDs at .100.1.{5,6,7}.0: npb_model, npb_serial, npb_sw_version
- Total OIDs: 71 (was 68)

### OBP Simulator
- Added 3 static NMU info OIDs at .10.21.60.{1,13,15}.0: obp_device_type, obp_sw_version, obp_serial
- Separate MIB export (__OBP-NMU-MIB) to avoid symbol conflicts
- Total OIDs: 27 (was 24)

### OID Maps
- Added 6 new entries to oidmaps.json (3 NPB + 3 OBP)
- Updated comment totals (OBP: 27, NPB: 71)

### Dashboard Fixes (bundled)
- Operations dashboard: "Host Name" column renamed to "Host"
- Business dashboard: service_name column hidden

## Deviations from Plan

None -- plan executed exactly as written.

## Duration

~3 minutes
