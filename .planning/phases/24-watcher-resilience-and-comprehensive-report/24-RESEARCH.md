# Phase 24: Watcher Resilience and Comprehensive Report - Research

**Researched:** 2026-03-09
**Domain:** K8s ConfigMap watcher error handling verification via pod logs + E2E report generation
**Confidence:** HIGH

## Summary

This phase is entirely about E2E test scenarios (bash scripts) and report generation. No C# code modifications are needed. The research focused on four areas: (1) exact log messages emitted by both watcher services for grep patterns, (2) error handling behavior in source code for invalid JSON, (3) existing E2E infrastructure (record_pass/record_fail, report.sh, run-all.sh), and (4) reconnection logging patterns.

Both `OidMapWatcherService` and `DeviceWatcherService` follow an identical architectural pattern: watch loop with automatic reconnect, semaphore-serialized reload, and structured logging at every decision point. The log messages are highly grepable and consistent.

**Primary recommendation:** Build four new scenario scripts (24-27) following the existing Phase 23 pattern (snapshot/restore, kubectl apply, poll/grep), plus upgrade `report.sh` and `run-all.sh` to produce a comprehensive categorized REPORT.md.

## Standard Stack

No new libraries needed. This phase uses only existing bash infrastructure.

### Core
| Tool | Purpose | Already Available |
|------|---------|-------------------|
| kubectl | Apply ConfigMaps, get pod status, read logs | Yes |
| bash + grep | Parse pod logs for expected messages | Yes |
| lib/common.sh | record_pass/record_fail, assertions | Yes |
| lib/kubectl.sh | snapshot_configmaps/restore_configmaps, check_pods_ready | Yes |
| lib/report.sh | generate_report (Markdown output) | Yes (needs enhancement) |
| lib/prometheus.sh | query_prometheus, polling utilities | Yes |

## Architecture Patterns

### Existing E2E Scenario Structure
```
tests/e2e/
├── lib/
│   ├── common.sh       # record_pass/record_fail, SCENARIO_RESULTS[], SCENARIO_EVIDENCE[]
│   ├── prometheus.sh   # query_prometheus, poll_until, get_evidence
│   ├── kubectl.sh      # snapshot_configmaps, restore_configmaps, check_pods_ready
│   └── report.sh       # generate_report (writes Markdown)
├── fixtures/           # Static YAML ConfigMap files
│   ├── oid-renamed-configmap.yaml      # Reusable for WATCH-01
│   ├── device-added-configmap.yaml     # Reusable for WATCH-02
│   └── .original-*-configmap.yaml      # Created at runtime by snapshot_configmaps
├── scenarios/
│   ├── 01-poll-executed.sh ... 23-device-modify-interval.sh
│   └── (new: 24-27 go here)
├── reports/            # Generated reports (gitignored)
├── run-all.sh          # Orchestrator: sources lib/, runs scenarios, generates report
└── .gitignore          # Ignores .original-* backups and reports/
```

### Pattern: Log-Grep Scenario (new pattern for this phase)
```bash
# Scenario N: description
SCENARIO_NAME="..."

snapshot_configmaps

# Apply a ConfigMap mutation to trigger the watcher
kubectl apply -f "$SCRIPT_DIR/fixtures/some-configmap.yaml" -n simetra

# Wait for watcher to detect change
sleep 10

# Grep pod logs for expected message across all pods
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
FOUND=0
for pod in $PODS; do
    LOGS=$(kubectl logs "$pod" -n simetra --since=30s 2>/dev/null) || continue
    if echo "$LOGS" | grep -q "expected log pattern"; then
        FOUND=1
        EVIDENCE="pod=$pod matched: ..."
        break
    fi
done

if [ "$FOUND" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "$EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "pattern not found in any pod logs"
fi

restore_configmaps
```

### Pattern: Pod Health After Invalid Input
```bash
# Apply invalid ConfigMap
kubectl apply -f "$SCRIPT_DIR/fixtures/invalid-json-configmap.yaml" -n simetra
sleep 10

# Check all pods still running
if check_pods_ready; then
    # Also verify error was logged (not silent)
    # grep for "Failed to parse" message
fi

restore_configmaps
```

### Anti-Patterns to Avoid
- **Fixed sleeps without verification:** Always follow a sleep with a concrete check (pod status, log grep)
- **Grepping all-time logs:** Use `--since=30s` or `--since=60s` to avoid matching stale log entries from previous test runs
- **Failing to restore:** Always restore_configmaps even on failure; wrap in trap or ensure it runs unconditionally

## Exact Log Messages (Source-Verified)

### OidMapWatcherService Log Messages (for grep patterns)

**File:** `src/SnmpCollector/Services/OidMapWatcherService.cs`

| Log Level | Message Template | When Emitted | Grep Pattern |
|-----------|-----------------|--------------|--------------|
| Information | `OidMapWatcher initial load complete for {ConfigMap}/{Key} in namespace {Namespace}` | Startup | `"OidMapWatcher initial load complete"` |
| Error | `OidMapWatcher initial load failed for {ConfigMap}/{Key} -- will retry via watch loop` | Startup failure | `"OidMapWatcher initial load failed"` |
| Debug | `OidMapWatcher starting watch on {ConfigMap} in namespace {Namespace}` | Watch (re)connect | `"OidMapWatcher starting watch"` |
| Information | `OidMapWatcher received {EventType} event for {ConfigMap}` | ConfigMap change detected | `"OidMapWatcher received"` |
| Warning | `ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current OID map` | ConfigMap deleted | `"was deleted -- skipping reload"` |
| Debug | `OidMapWatcher watch connection closed, reconnecting` | Normal watch timeout (~30m) | `"watch connection closed, reconnecting"` |
| Warning | `OidMapWatcher watch disconnected unexpectedly, reconnecting in 5s` | Watch error/disconnect | `"watch disconnected unexpectedly"` |
| Information | `OID map reload complete: {OidCount} entries` | Successful reload | `"OID map reload complete"` |
| Error | `Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload` | Invalid JSON | `"Failed to parse"` |
| Warning | `ConfigMap {ConfigMap} does not contain key {ConfigKey} -- skipping reload` | Missing key | `"does not contain key"` |
| Warning | `Deserialized {ConfigKey} is null -- skipping reload` | Null deserialization | `"is null -- skipping reload"` |
| Error | `OID map reload failed -- previous map remains active` | Reload exception | `"reload failed -- previous map remains active"` |

**Downstream OidMapService log messages (also visible in pod logs):**

| Log Level | Message Template | Grep Pattern |
|-----------|-----------------|--------------|
| Information | `OidMap hot-reloaded: {EntryCount} entries total, +{Added} added, -{Removed} removed, ~{Changed} changed` | `"OidMap hot-reloaded"` |
| Information | `OidMap changed: {Oid} {OldName} -> {NewName}` | `"OidMap changed"` |
| Information | `OidMap added: {Oid} -> {MetricName}` | `"OidMap added"` |
| Information | `OidMap removed: {Oid}` | `"OidMap removed"` |

### DeviceWatcherService Log Messages (for grep patterns)

**File:** `src/SnmpCollector/Services/DeviceWatcherService.cs`

| Log Level | Message Template | When Emitted | Grep Pattern |
|-----------|-----------------|--------------|--------------|
| Information | `DeviceWatcher initial load complete for {ConfigMap}/{Key} in namespace {Namespace}` | Startup | `"DeviceWatcher initial load complete"` |
| Error | `DeviceWatcher initial load failed for {ConfigMap}/{Key} -- will retry via watch loop` | Startup failure | `"DeviceWatcher initial load failed"` |
| Debug | `DeviceWatcher starting watch on {ConfigMap} in namespace {Namespace}` | Watch (re)connect | `"DeviceWatcher starting watch"` |
| Information | `DeviceWatcher received {EventType} event for {ConfigMap}` | ConfigMap change detected | `"DeviceWatcher received"` |
| Warning | `ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current devices` | ConfigMap deleted | `"was deleted -- skipping reload"` |
| Debug | `DeviceWatcher watch connection closed, reconnecting` | Normal watch timeout (~30m) | `"watch connection closed, reconnecting"` |
| Warning | `DeviceWatcher watch disconnected unexpectedly, reconnecting in 5s` | Watch error/disconnect | `"watch disconnected unexpectedly"` |
| Information | `Device reload complete: {DeviceCount} devices` | Successful reload | `"Device reload complete"` |
| Error | `Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload` | Invalid JSON | `"Failed to parse"` |
| Error | `Device reload failed -- previous config remains active` | Reload exception | `"reload failed -- previous config remains active"` |

**Downstream DeviceRegistry log message:**

| Log Level | Message Template | Grep Pattern |
|-----------|-----------------|--------------|
| Information | `DeviceRegistry reloaded: {DeviceCount} devices, +{Added} added, -{Removed} removed` | `"DeviceRegistry reloaded"` |

**Downstream DynamicPollScheduler log message:**

| Log Level | Message Template | Grep Pattern |
|-----------|-----------------|--------------|
| Information | `Poll scheduler reconciled: +{Added} added, -{Removed} removed, ~{Rescheduled} rescheduled, {Total} total jobs` | `"Poll scheduler reconciled"` |

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ConfigMap snapshot/restore | Custom save/restore logic | `snapshot_configmaps` / `restore_configmaps` from kubectl.sh | Already proven in 6 scenarios (18-23) |
| Pod readiness check | Custom kubectl parsing | `check_pods_ready` from kubectl.sh | Handles all edge cases |
| Result tracking | Custom counters/arrays | `record_pass` / `record_fail` from common.sh | Already stores evidence strings |

## Common Pitfalls

### Pitfall 1: Debug-level logs not visible by default
**What goes wrong:** Several key watcher messages (watch start, reconnect) are logged at Debug level. Default K8s logging may not include Debug.
**Why it happens:** .NET log level filtering -- Debug is typically suppressed in Production.
**How to avoid:** For WATCH-01/02, grep for Information-level messages (`"OidMapWatcher received"`, `"OID map reload complete"`, `"OidMap hot-reloaded"`). For WATCH-04 (reconnection), the `"watch disconnected unexpectedly"` message is Warning level (visible), but `"watch connection closed, reconnecting"` is Debug (may not be visible). The unexpected disconnect warning is the reliable grep target.
**Warning signs:** Empty grep results even when the feature is working.

### Pitfall 2: Log grep timing window
**What goes wrong:** `kubectl logs --since=30s` might miss messages if the watcher processes the ConfigMap change faster than expected, or if clock skew exists.
**Why it happens:** K8s log timestamps can have slight drift; watcher reacts within seconds.
**How to avoid:** Use `--since=60s` for safety margin. The test applies the ConfigMap change, waits a reasonable time, then greps.

### Pitfall 3: Invalid JSON fixture must be syntactically invalid, not just wrong-schema
**What goes wrong:** Valid JSON with wrong structure (e.g., array instead of object for oidmaps) gets deserialized as null, hitting the "is null" warning path, not the JsonException catch.
**Why it happens:** `JsonSerializer.Deserialize<Dictionary<string,string>>` on `[]` returns null, not an exception.
**How to avoid:** Test BOTH cases: (1) truly broken syntax (triggers `"Failed to parse"` error log) and (2) valid JSON wrong type (triggers `"is null -- skipping reload"` warning log). The context doc already calls for both types.

### Pitfall 4: Grepping across multiple pods
**What goes wrong:** Only one pod may receive the ConfigMap watch event first. Grepping a single pod might miss the message.
**Why it happens:** All 3 replicas run their own watcher, but K8s watch delivery timing varies.
**How to avoid:** Grep ALL pods in a loop, succeed if ANY pod has the expected message. Use `kubectl get pods -l app=snmp-collector` to get all pod names.

### Pitfall 5: Report format change breaks existing flow
**What goes wrong:** Enhancing report.sh changes the Markdown structure, breaking expectations.
**Why it happens:** report.sh is already integrated into run-all.sh.
**How to avoid:** The report enhancement should be additive (add categories/sections) rather than changing the core generate_report interface. Keep the same function signature.

## Code Examples

### Example: Grep pod logs for watcher event (WATCH-01 pattern)
```bash
# Get all snmp-collector pod names
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')

# Grep for OidMapWatcher reload evidence
FOUND=0
EVIDENCE=""
for pod in $PODS; do
    LOGS=$(kubectl logs "$pod" -n simetra --since=60s 2>/dev/null) || continue
    # Primary: watcher received the event
    MATCH=$(echo "$LOGS" | grep "OidMapWatcher received" | tail -1) || true
    if [ -n "$MATCH" ]; then
        # Secondary: reload completed successfully
        RELOAD=$(echo "$LOGS" | grep "OID map reload complete" | tail -1) || true
        FOUND=1
        EVIDENCE="pod=$pod event='$MATCH' reload='$RELOAD'"
        break
    fi
done
```

### Example: Invalid JSON ConfigMap fixture (syntactically broken)
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-oidmaps
  namespace: simetra
data:
  oidmaps.json: |
    { this is not valid JSON at all!!!
```

### Example: Invalid JSON ConfigMap fixture (wrong schema -- array instead of dict)
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: simetra-oidmaps
  namespace: simetra
data:
  oidmaps.json: |
    ["this", "is", "valid", "json", "but", "wrong", "schema"]
```

### Example: Enhanced report with categories
```bash
generate_report() {
    local output_file="$1"
    {
        echo "# Simetra E2E Verification Report"
        echo ""
        echo "Generated: $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
        echo ""
        # Summary table (same as current)
        # ...

        # Categorized results
        echo "## Pipeline Counters (01-10)"
        echo ""
        # Filter SCENARIO_RESULTS for scenarios 01-10

        echo "## Business Metrics (11-17)"
        echo ""
        # Filter for 11-17

        echo "## OID Mutations (18-20)"
        # ...
        echo "## Device Lifecycle (21-23)"
        # ...
        echo "## Watcher Resilience (24-27)"
        # ...
    } > "$output_file"
}
```

### record_pass / record_fail Data Model

**Current implementation** (from `lib/common.sh`):
- `SCENARIO_RESULTS` array: entries are `"PASS|scenario_name"` or `"FAIL|scenario_name"`
- `SCENARIO_EVIDENCE` array: entries are `"scenario_name|evidence_string"`
- `PASS_COUNT` / `FAIL_COUNT`: simple integer counters
- Evidence is a free-form string (Prometheus query results, label values, etc.)

**For categorized report:** Scenario numbering is embedded in the scenario filename (e.g., `01-poll-executed.sh`). The report generator can extract the number prefix from the scenario name or use the array index to determine category grouping.

**Key insight:** The scenario name stored in `SCENARIO_RESULTS` does NOT include the number prefix -- it's a human-readable description. To categorize, the report generator needs to either: (a) add a number prefix to scenario names, or (b) use array position (fragile). Recommendation: prefix scenario names with the number, e.g., `SCENARIO_NAME="[24] OID map watcher detects ConfigMap change"`.

## Reconnection Observation (WATCH-04) Analysis

The watcher reconnection behavior is well-documented in source code:

1. **Normal reconnect (Debug level):** After K8s server closes watch connection (~30 min timeout), logs `"watch connection closed, reconnecting"` and loops back to re-establish watch. This is Debug level and likely NOT visible in production logs.

2. **Error reconnect (Warning level):** On unexpected exceptions, logs `"watch disconnected unexpectedly, reconnecting in 5s"` with 5-second backoff. This IS visible at default log levels.

3. **For WATCH-04:** The scenario should grep for either pattern. If pods have been running > 30 minutes, there SHOULD be natural reconnection events. If not, the scenario should record_pass with a caveat noting the source code has retry logic but no reconnection events were observed during the test window.

4. **No way to force reconnection without chaos testing.** The context doc explicitly says "pass with caveat" is acceptable.

## Report Infrastructure Enhancement Plan

### Current State
- `generate_report()` produces a flat list: Summary table + Results table + Evidence sections
- Report file goes to `tests/e2e/reports/pipeline-counters-TIMESTAMP.md` (gitignored)
- Report title is "E2E Pipeline Counter Verification Report" (now outdated -- covers more than counters)

### What Needs to Change
1. **Report title:** Update to "Simetra E2E Verification Report" (comprehensive)
2. **Output path:** Context says `tests/e2e/REPORT.md` -- but reports/ is gitignored, and REPORT.md at the root level would be a persistent file
3. **Categorized sections:** Group by phase category using scenario number prefixes
4. **Evidence per scenario:** Already implemented -- each scenario has evidence string
5. **Scenario naming convention:** Add number prefix to scenario names for categorization

### Implementation Approach
- Scenarios should set `SCENARIO_NAME` with a bracket prefix like `[24]` to enable categorization
- The generate_report function reads SCENARIO_RESULTS array and groups by prefix
- Alternatively (simpler): just output all results in order -- since scenarios run sequentially by filename sort, the natural ordering already groups by category

## Open Questions

1. **REPORT.md location:** Context says `tests/e2e/REPORT.md` but `reports/` is gitignored. Should REPORT.md be at `tests/e2e/REPORT.md` (tracked) or continue in `reports/` (gitignored)? The context says "generate tests/e2e/REPORT.md at the end of run-all.sh execution" which implies a fixed-name file, not timestamped. Recommendation: generate to `tests/e2e/REPORT.md` (fixed name, overwritten each run), and optionally keep timestamped copies in reports/.

2. **Debug log visibility:** The "starting watch" and "watch connection closed" messages are Debug level. Need to verify if the production deployment has Debug logging enabled for these namespaces, or if only Information+ is visible. If Debug is suppressed, WATCH-04 reconnection evidence will only be available from unexpected disconnects (Warning level), not normal timeouts.

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Services/OidMapWatcherService.cs` -- all log message templates extracted directly
- `src/SnmpCollector/Services/DeviceWatcherService.cs` -- all log message templates extracted directly
- `src/SnmpCollector/Pipeline/OidMapService.cs` -- UpdateMap() log messages
- `src/SnmpCollector/Pipeline/DeviceRegistry.cs` -- ReloadAsync() log message
- `src/SnmpCollector/Services/DynamicPollScheduler.cs` -- ReconcileAsync() log message
- `tests/e2e/lib/common.sh` -- record_pass/record_fail implementation
- `tests/e2e/lib/report.sh` -- generate_report implementation
- `tests/e2e/lib/kubectl.sh` -- snapshot/restore, check_pods_ready
- `tests/e2e/run-all.sh` -- orchestrator flow
- `tests/e2e/scenarios/18-oid-rename.sh` -- existing mutation scenario pattern
- `tests/e2e/scenarios/21-device-add.sh` -- existing device mutation pattern

## Metadata

**Confidence breakdown:**
- Watcher log patterns: HIGH -- extracted directly from source code
- E2E infrastructure: HIGH -- read all existing lib/ files and scenarios
- Report enhancement: HIGH -- straightforward extension of existing generate_report
- WATCH-04 reconnection: MEDIUM -- behavior is clear from source, but observability depends on log level config

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable -- no external dependencies)
