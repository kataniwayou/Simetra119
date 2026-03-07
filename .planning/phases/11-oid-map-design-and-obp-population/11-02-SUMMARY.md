---
phase: 11-oid-map-design-and-obp-population
plan: 02
subsystem: infra
tags: [configuration, kubernetes, configmap, hot-reload, oid-map]

requires:
  - phase: 11-01
    provides: oidmap-obp.json data file and ConfigMap key
provides:
  - Config directory auto-scan in Program.cs for oidmap-*.json files
  - K8s directory mount at /app/config enabling ConfigMap hot-reload
  - CONFIG_DIRECTORY env var in deployments and Dockerfile
affects: [11-03, 12-device-simulation]

tech-stack:
  added: []
  patterns:
    - "Config directory auto-scan with CONFIG_DIRECTORY env var fallback"
    - "Directory mount (no subPath) for K8s ConfigMap hot-reload"

key-files:
  created: []
  modified:
    - src/SnmpCollector/Program.cs
    - src/SnmpCollector/SnmpCollector.csproj
    - deploy/k8s/deployment.yaml
    - deploy/k8s/production/deployment.yaml
    - Dockerfile

key-decisions:
  - "CONFIG_DIRECTORY env var with ContentRootPath/config fallback for local dev"
  - "Alphabetical ordering (OrderBy) for deterministic OID map merge"
  - "Directory mount replaces subPath mount to enable atomic ConfigMap updates"

patterns-established:
  - "Config auto-scan: scan configDir for oidmap-*.json, load alphabetically with reloadOnChange"
  - "K8s config: directory mount at /app/config, CONFIG_DIRECTORY env var"

duration: 5min
completed: 2026-03-07
---

# Phase 11 Plan 02: Program.cs Config Auto-Scan and K8s Directory Mount Summary

**Config directory auto-scan in Program.cs with CONFIG_DIRECTORY env var, K8s directory mount replacing subPath for hot-reload**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-07T12:34:39Z
- **Completed:** 2026-03-07T12:40:04Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments
- Program.cs scans CONFIG_DIRECTORY (or ContentRootPath/config) for appsettings.k8s.json and oidmap-*.json before Build()
- .csproj copies config/**/*.json to output directory for local dev
- Both K8s deployments switched from subPath to directory mount at /app/config
- CONFIG_DIRECTORY env var added to deployments and Dockerfile

## Task Commits

Each task was committed atomically:

1. **Task 1: Add config directory auto-scan to Program.cs** - `dd73400` (feat)
2. **Task 2: Ensure config files are copied to output** - `503624a` (chore)
3. **Task 3: Switch K8s deployments to directory mount** - `2ab9109` (feat)

## Files Created/Modified
- `src/SnmpCollector/Program.cs` - Config directory auto-scan block after CreateBuilder
- `src/SnmpCollector/SnmpCollector.csproj` - Content ItemGroup for config/**/*.json
- `deploy/k8s/deployment.yaml` - Directory mount + CONFIG_DIRECTORY env var
- `deploy/k8s/production/deployment.yaml` - Directory mount + CONFIG_DIRECTORY env var
- `Dockerfile` - CONFIG_DIRECTORY=/app/config default

## Decisions Made
None - followed plan as specified.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added missing using directive for Microsoft.Extensions.Configuration**
- **Found during:** Task 1 (config auto-scan)
- **Issue:** AddJsonFile extension method not found without the using directive
- **Fix:** Added `using Microsoft.Extensions.Configuration;` to Program.cs
- **Files modified:** src/SnmpCollector/Program.cs
- **Verification:** dotnet build succeeds
- **Committed in:** dd73400 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Required for compilation. No scope creep.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Config auto-scan is wired; OidMapService (plan 03) can bind to IOptionsMonitor<OidMapOptions> with reloadOnChange
- K8s directory mount enables hot-reload when ConfigMap is updated
- No blockers for plan 03

---
*Phase: 11-oid-map-design-and-obp-population*
*Completed: 2026-03-07*
