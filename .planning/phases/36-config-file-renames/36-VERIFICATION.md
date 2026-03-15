---
phase: 36-config-file-renames
verified: 2026-03-15T08:00:00Z
status: gaps_found
score: 10/14 must-haves verified
gaps:
  - truth: "No remaining references to simetra-oidmaps, oidmaps.json, simetra-commandmaps, commandmaps.json in source/config/manifest/scripts"
    status: partial
    reason: "Stale file .original-oidmaps-configmap.yaml exists on disk (untracked) containing name: simetra-oidmaps and key oidmaps.json. Comment text in ServiceCollectionExtensions.cs line 362 says 'oidmaps, commandmaps'. Inline comments in OidMapAutoScanTests.cs lines 177 and 196 say 'oidmaps.json'. E2E scenarios 18, 19, 20, 24 have comment/log_info text saying 'oidmaps' (functional kubectl calls use correct new fixture names)."
    artifacts:
      - path: "tests/e2e/fixtures/.original-oidmaps-configmap.yaml"
        issue: "File exists on disk (untracked by git). Contains name: simetra-oidmaps and key oidmaps.json. Not cleaned up as part of rename. Plan 36-02 required renaming to .original-oid-metric-map-configmap.yaml."
      - path: "src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs"
        issue: "Line 362 comment: 'Each config file (oidmaps, commandmaps, devices, tenants)' uses old short names"
      - path: "tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs"
        issue: "Lines 177 and 196 inline comments say 'load OBP entries from oidmaps.json'. The path constant (line 24) was correctly updated to oid_metric_map.json but the prose comments were missed."
      - path: "tests/e2e/scenarios/18-oid-rename.sh"
        issue: "Lines 2, 9, 10: comment and log_info text reference 'oidmaps' (comments only — functional kubectl apply uses oid-renamed-configmap.yaml which internally has simetra-oid-metric-map)"
      - path: "tests/e2e/scenarios/19-oid-remove.sh"
        issue: "Lines 2, 9, 10: comment and log_info text reference 'oidmaps' (comments only)"
      - path: "tests/e2e/scenarios/20-oid-add.sh"
        issue: "Lines 3, 39, 40, 75: comment and log_info text reference 'oidmaps' (comments only)"
      - path: "tests/e2e/scenarios/24-oidmap-watcher-log.sh"
        issue: "Lines 8, 9: comment and log_info text reference 'oidmaps' (comments only)"
    missing:
      - "Remove or rename tests/e2e/fixtures/.original-oidmaps-configmap.yaml from disk (it is untracked but still present with old content)"
      - "Update ServiceCollectionExtensions.cs line 362 comment to 'oid_metric_map, oid_command_map, devices, tenants'"
      - "Update OidMapAutoScanTests.cs lines 177 and 196 inline comments to say 'oid_metric_map.json'"
      - "Update comment/log_info text in scenarios 18, 19, 20, 24 to use 'oid_metric_map' or 'oid-metric-map' instead of 'oidmaps' (low severity)"
  - truth: "E2E fixtures .original-oid-metric-map-configmap.yaml exists and old .original-oidmaps-configmap.yaml is gone"
    status: failed
    reason: ".original-oid-metric-map-configmap.yaml does not exist on disk (it is runtime-generated so this is partly expected). However .original-oidmaps-configmap.yaml still exists on disk as a stale untracked working-tree file with old content (name: simetra-oidmaps, key: oidmaps.json)."
    artifacts:
      - path: "tests/e2e/fixtures/.original-oidmaps-configmap.yaml"
        issue: "Stale old file still on disk. Should have been deleted."
    missing:
      - "Delete tests/e2e/fixtures/.original-oidmaps-configmap.yaml from the working tree"
# Phase 36: Config File Renames Verification Report

**Phase Goal:** All config file names, ConfigMap names, config keys, C# constants, K8s manifests, local dev files, and E2E test scripts are updated atomically to the new naming convention. No artifact retains the old name after this phase.
**Verified:** 2026-03-15T08:00:00Z
**Status:** gaps_found
**Re-verification:** No -- initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | TenantVectorWatcherService.ConfigMapName = simetra-tenants; ConfigKey = tenants.json | VERIFIED | Line 31/36 confirmed |
| 2 | TenantVectorOptions.SectionName = Tenants | VERIFIED | Line 10 confirmed |
| 3 | Program.cs TryGetProperty uses Tenants | VERIFIED | Line 119 confirmed |
| 4 | config/tenants.json with root key Tenants | VERIFIED | File in git; root key Tenants |
| 5 | simetra-tenants.yaml correct name and key | VERIFIED | name: simetra-tenants; tenants.json |
| 6 | Both deployment.yamls reference simetra-tenants | VERIFIED | snmp-collector line 86; production line 87 |
| 7 | Production configmap.yaml name simetra-tenants; key tenants.json | VERIFIED | Line 415 confirmed |
| 8 | E2E kubectl.sh uses simetra-tenants + .original-tenants-configmap.yaml | VERIFIED | Lines 113, 123, 124 confirmed |
| 9 | E2E scenario 28 uses simetra-tenants and tenants.json | VERIFIED | Functional calls and heredoc confirmed |
| 10 | OidMapWatcherService constants: simetra-oid-metric-map; oid_metric_map.json | VERIFIED | Lines 29, 34 confirmed |
| 11 | CommandMapWatcherService constants: simetra-oid-command-map; oid_command_map.json | VERIFIED | Lines 29, 34 confirmed |
| 12 | Program.cs paths: oid_metric_map.json; oid_command_map.json | VERIFIED | Lines 77, 137 confirmed |
| 13 | OidMapAutoScanTests GetOidMapsPath returns oid_metric_map.json | VERIFIED | Line 24 confirmed; Assert line 155 correct |
| 14 | Zero old-name references in source/config/manifest/scripts | FAILED | Stale file on disk + stale comment text -- see gaps |

**Score:** 10/14 truths verified

---

## Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| src/SnmpCollector/config/tenants.json | VERIFIED | Git-tracked; root key Tenants confirmed |
| src/SnmpCollector/config/oid_metric_map.json | VERIFIED | Git-tracked |
| src/SnmpCollector/config/oid_command_map.json | VERIFIED | Git-tracked |
| deploy/k8s/snmp-collector/simetra-tenants.yaml | VERIFIED | name: simetra-tenants; key: tenants.json |
| deploy/k8s/snmp-collector/simetra-oid-metric-map.yaml | VERIFIED | name: simetra-oid-metric-map; key: oid_metric_map.json |
| deploy/k8s/snmp-collector/simetra-oid-command-map.yaml | VERIFIED | name: simetra-oid-command-map; key: oid_command_map.json |
| tests/e2e/fixtures/.original-oidmaps-configmap.yaml | STALE | Untracked file still on disk; contains old name/key |
| src/SnmpCollector/config/tenantvector.json | ABSENT (correct) | Not in git index |
| src/SnmpCollector/config/oidmaps.json | ABSENT (correct) | Not in git index |
| src/SnmpCollector/config/commandmaps.json | ABSENT (correct) | Not in git index |
| deploy/k8s/snmp-collector/simetra-tenantvector.yaml | ABSENT (correct) | Not in git index |
| deploy/k8s/snmp-collector/simetra-oidmaps.yaml | ABSENT (correct) | Not in git index |
| deploy/k8s/snmp-collector/simetra-commandmaps.yaml | ABSENT (correct) | Not in git index |

---

## Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| TenantVectorOptions.SectionName Tenants | Program.cs TryGetProperty(Tenants) | config/tenants.json root key | WIRED |
| TenantVectorWatcherService.ConfigMapName simetra-tenants | K8s ConfigMap | deployment.yaml volume projection | WIRED |
| TenantVectorWatcherService.ConfigKey tenants.json | K8s data key | simetra-tenants.yaml | WIRED |
| OidMapWatcherService.ConfigMapName simetra-oid-metric-map | K8s ConfigMap | deployment.yaml lines 82/83 | WIRED |
| OidMapWatcherService.ConfigKey oid_metric_map.json | K8s data key | simetra-oid-metric-map.yaml | WIRED |
| CommandMapWatcherService.ConfigMapName simetra-oid-command-map | K8s ConfigMap (no projection; K8s watch only) | by design | WIRED |
| CommandMapWatcherService.ConfigKey oid_command_map.json | K8s data key | simetra-oid-command-map.yaml | WIRED |

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| tests/e2e/fixtures/.original-oidmaps-configmap.yaml | entire file | Old file on disk: name simetra-oidmaps; key oidmaps.json | BLOCKER | Contradicts goal; runtime save_configmap creates new file correctly |
| src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs | 362 | Comment text: oidmaps, commandmaps | WARNING | Documentation only; no runtime impact |
| tests/SnmpCollector.Tests/Configuration/OidMapAutoScanTests.cs | 177, 196 | Inline comments: oidmaps.json | WARNING | Path constant line 24 is correct; only prose comments are stale |
| tests/e2e/scenarios/18-oid-rename.sh | 2, 9, 10 | Comment/log_info text: oidmaps | INFO | Comments only; functional apply uses correct fixture |
| tests/e2e/scenarios/19-oid-remove.sh | 2, 9, 10 | Comment/log_info text: oidmaps | INFO | Comments only |
| tests/e2e/scenarios/20-oid-add.sh | 3, 39, 40, 75 | Comment/log_info text: oidmaps | INFO | Comments only |
| tests/e2e/scenarios/24-oidmap-watcher-log.sh | 8, 9 | Comment/log_info text: oidmaps | INFO | Comments only |
| deploy/k8s/production/configmap.yaml | 248 | Comment: via oidmaps | INFO | Devices schema doc comment; no runtime impact |
| tests/e2e/reports/*.md | multiple | Historical run output contains simetra-oidmaps | INFO | Read-only historical reports |

---

## Confirmed Zero-Match Grep Results

The following old names return zero matches in source, config, manifest, and script files (excluding build artifacts and historical e2e reports):

- simetra-tenantvector -- ZERO matches in src/, deploy/, tests/e2e/scenarios, tests/SnmpCollector.Tests
- tenantvector.json (as quoted string literal) -- ZERO matches
- simetra-oidmaps -- ZERO matches in src/, deploy/, tests/e2e/scenarios, tests/SnmpCollector.Tests (only in untracked .original-oidmaps-configmap.yaml and historical e2e reports)
- simetra-commandmaps -- ZERO matches in src/, deploy/, tests/
- commandmaps.json (as quoted literal) -- ZERO matches
- TenantVector as IConfiguration section key -- ZERO matches (all replaced with Tenants)

---

## Gaps Summary

The phase accomplished all functional renames correctly. Every C# constant, local dev config file, K8s standalone manifest, deployment projection, production configmap definition, and E2E save/restore path is on the new naming convention. The critical wiring chains (service constants through K8s manifest names through deployment projections) are fully consistent and correct.

Two categories of issues remain that prevent full goal achievement:

**Gap 1 -- Stale working-tree file (BLOCKER for strict goal):**
tests/e2e/fixtures/.original-oidmaps-configmap.yaml was not deleted from the working tree. It is untracked by git and contains name: simetra-oidmaps and data key oidmaps.json internally. The plan required renaming it to .original-oid-metric-map-configmap.yaml. The SUMMARY for plan 36-01 noted that .original-tenantvector-configmap.yaml is runtime-generated and not git-tracked -- but for the oidmaps equivalent, the stale file was not cleaned up. Since it is untracked, a plain rm removes it. At runtime kubectl.sh will correctly create a new .original-oid-metric-map-configmap.yaml, but the stale old file on disk contradicts the goal that no artifact retains the old name.

**Gap 2 -- Stale comment text (WARNING-level):**
ServiceCollectionExtensions.cs line 362 has a doc comment listing config files by their old short names. OidMapAutoScanTests.cs lines 177 and 196 have inline comment prose saying oidmaps.json (the path constant GetOidMapsPath on line 24 was correctly updated to oid_metric_map.json). E2E scenario scripts 18, 19, 20, and 24 have comment lines and log_info strings describing their scenario using oidmaps as a noun -- but their functional kubectl apply calls use correctly named fixture files whose internal content was updated. These are documentation inconsistencies with no runtime impact.

---

*Verified: 2026-03-15T08:00:00Z*
*Verifier: Claude (gsd-verifier)*
