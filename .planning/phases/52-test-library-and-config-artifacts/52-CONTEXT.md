# Phase 52: Test Library and Config Artifacts - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Create all bash library helpers (sim_set_scenario, poll_until_log), test runner orchestration (port-forward lifecycle, cleanup), tenant fixture YAML files for each test topology, OID/command/device map entries for the 6 new test OIDs, and ensure the 28 existing E2E scenarios continue to pass unchanged.

</domain>

<decisions>
## Implementation Decisions

### Tenant config design
- Resolved metrics are **violated by default** (out-of-range in default scenario). This means the idle state hits Tier 2 (ConfirmedBad) and stops — no commands. Specific scenarios must clear resolved metrics for the flow to continue to Tier 3 evaluate check.
- Multi-tenant fixtures (CFG-05, CFG-06): both tenants monitor the **same OIDs, different tenant IDs**. Scenario switch affects both equally.
- Claude's discretion: threshold values for evaluate metric, suppression window duration

### Test runner orchestration
- Every scenario resets to "default" at start **AND** sets the specific scenario needed — belt and suspenders approach prevents bleed from prior tests.
- Log assertions: check **all 3 pods, any match** — pass if any pod has the expected log line.
- Claude's discretion: whether snapshot tests are integrated into run-all.sh or separate runner, per-scenario ConfigMap apply vs shared config

### OID/command map entries
- Command name: **e2e_set_bypass** — follows existing naming pattern (obp_set_bypass_Lx)
- Command mimics real OBP bypass: **SET e2e_command_response = 0** (Integer32, value 0 = bypass channel)
- Metric names use **device-semantic names**: e.g. e2e_port_utilization (evaluate), e2e_channel_state (resolved), e2e_bypass_status (resolved) — NOT the generic names from Phase 51 OID registration
- Note: The OID metric map must map the .999.4.x OIDs to these device-semantic resolved_names. The simulator's internal labels (e2e_evaluate_metric etc.) are irrelevant — only the oid_metric_map.json names matter for the collector.
- New entries added to the **existing** simetra-oid-metric-map ConfigMap (not a separate ConfigMap)

### Existing E2E compatibility
- Snapshot tests **restore original config** at the end — save/restore tenant ConfigMap so cluster state is unchanged after test run
- Claude's discretion: whether existing 28 scenarios and new snapshot tests coexist in one runner or separate

### Claude's Discretion
- Threshold values for evaluate and resolved metrics
- Suppression window duration
- Test runner organization (integrated vs separate)
- Per-scenario ConfigMap apply vs shared config with scenario-only switching
- Device config poll interval for test OIDs

</decisions>

<specifics>
## Specific Ideas

- Real-world flow: NPB detects anomalous traffic (evaluate: port utilization spike) -> system commands OBP to bypass (SET Channel = 0) -> resolved metrics confirm: channel state = bypass(0), bypass status active
- The E2E test should mirror this flow: evaluate metric breaches threshold -> command fires SET = 0 -> resolved metrics read back the post-bypass state
- Device analysis docs at Docs/OBP-Device-Analysis.md and Docs/NPB-Device-Analysis.md provide the real OID semantics that inform metric naming and threshold design

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 52-test-library-and-config-artifacts*
*Context gathered: 2026-03-17*
