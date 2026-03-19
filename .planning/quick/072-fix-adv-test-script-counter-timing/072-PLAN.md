---
phase: quick-072
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh
  - tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh
autonomous: true

must_haves:
  truths:
    - "ADV-01 sub-scenario 36b uses poll_until to wait for counter increment instead of immediate snapshot"
    - "ADV-02 sub-scenario 37b uses poll_until to wait for counter increment instead of immediate snapshot"
    - "Both scripts follow the same poll_until pattern already used in scenarios 30 and 35"
  artifacts:
    - path: "tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh"
      provides: "ADV-01 with counter polling"
      contains: "poll_until 45 5"
    - path: "tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh"
      provides: "ADV-02 with counter polling"
      contains: "poll_until 45 5"
  key_links:
    - from: "tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh"
      to: "tests/e2e/lib/prometheus.sh"
      via: "poll_until function call"
      pattern: "poll_until 45 5 \"snmp_command_sent_total\""
---

<objective>
Fix ADV-01 and ADV-02 E2E test scripts to use `poll_until` for counter increment checks instead of immediate `snapshot_counter` after tier=4 log detection.

Purpose: The SNMP SET command is async (dispatched via CommandWorkerService channel) and the Prometheus scrape interval is 15s. The immediate snapshot races against both, causing false failures (delta=0) even though commands are sent. Scenarios 30 (STS-02) and 35 (MTS-02) already solved this with `poll_until 45 5` -- ADV-01 and ADV-02 need the same pattern.

Output: Two updated test scripts with reliable counter assertions.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh
@tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh
@tests/e2e/lib/prometheus.sh
@tests/e2e/scenarios/30-sts-02-evaluate-violated.sh (reference pattern lines 59-70)
@tests/e2e/scenarios/35-mts-02-advance-gate.sh (reference pattern lines 84-95)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Replace immediate snapshot_counter with poll_until in both ADV scripts</name>
  <files>
    tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh
    tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh
  </files>
  <action>
In both scripts, replace the sub-scenario that checks snmp_command_sent_total (36b in ADV-01, 37b in ADV-02) with the `poll_until` polling pattern already used in scenarios 30 and 35.

**36-adv-01-aggregate-evaluate.sh (lines 50-64):**

Replace the immediate `AFTER_SENT=$(snapshot_counter ...)` + delta check block with:

```bash
# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-01: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_pass "ADV-01: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-01: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_fail "ADV-01: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} expected > 0 after 45s polling $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
fi
```

Add a comment above the block: `# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.`

**37-adv-02-depth3-allsamples.sh (lines 67-81):**

Same pattern. Replace the immediate snapshot + delta check in sub-scenario 37b with:

```bash
# Poll for counter — SNMP SET round-trip + OTel export + Prometheus scrape takes time.
if poll_until 45 5 "snmp_command_sent_total" 'device_name="E2E-SIM"' "$BEFORE_SENT"; then
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-02: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_pass "ADV-02: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
else
    AFTER_SENT=$(snapshot_counter "snmp_command_sent_total" 'device_name="E2E-SIM"')
    DELTA_SENT=$((AFTER_SENT - BEFORE_SENT))
    log_info "ADV-02: Sent counter after tier=4: before=${BEFORE_SENT} after=${AFTER_SENT} delta=${DELTA_SENT}"
    record_fail "ADV-02: Command sent counter incremented" \
        "sent_delta=${DELTA_SENT} expected > 0 after 45s polling $(get_evidence "snmp_command_sent_total" 'device_name="E2E-SIM"')"
fi
```

Keep the section comment headers (Sub-scenario 36b / 37b) and update their descriptions to note the polling approach.

Do NOT change any other parts of either script (baseline capture, tier=4 log polling, source=synthetic check in ADV-01, recovery phase in ADV-02, cleanup sections).
  </action>
  <verify>
    1. `bash -n tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh` -- syntax check passes
    2. `bash -n tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh` -- syntax check passes
    3. `grep -c "poll_until 45 5" tests/e2e/scenarios/36-adv-01-aggregate-evaluate.sh` returns 1
    4. `grep -c "poll_until 45 5" tests/e2e/scenarios/37-adv-02-depth3-allsamples.sh` returns 1
    5. Neither file contains a bare `AFTER_SENT=$(snapshot_counter` that is NOT inside a poll_until if/else block (the immediate-snapshot anti-pattern is gone)
  </verify>
  <done>
    Both ADV-01 and ADV-02 use poll_until with 45s timeout / 5s interval for the sent counter check, matching the established pattern in STS-02 and MTS-02. The race condition between tier=4 log detection and counter scrape is eliminated.
  </done>
</task>

</tasks>

<verification>
- Both scripts pass bash -n syntax check
- Both scripts contain exactly one `poll_until 45 5 "snmp_command_sent_total"` call
- The poll_until pattern matches scenarios 30 and 35 structurally (if poll_until ... then record_pass else record_fail fi)
- No other sections of either script are modified
</verification>

<success_criteria>
- ADV-01 sub-scenario 36b polls up to 45s for snmp_command_sent_total to exceed baseline before asserting
- ADV-02 sub-scenario 37b polls up to 45s for snmp_command_sent_total to exceed baseline before asserting
- Both scripts remain syntactically valid bash
- Pattern is consistent with existing STS-02 and MTS-02 counter checks
</success_criteria>

<output>
After completion, create `.planning/quick/072-fix-adv-test-script-counter-timing/072-SUMMARY.md`
</output>
