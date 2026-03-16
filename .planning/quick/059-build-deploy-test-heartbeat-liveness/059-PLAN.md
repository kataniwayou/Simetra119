---
phase: quick
plan: 059
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/e2e/test-heartbeat-liveness.sh
autonomous: true

must_haves:
  truths:
    - "Script builds Docker image, deploys to K8s, and validates heartbeat liveness end-to-end"
    - "Script verifies pipeline-heartbeat entry exists and is not stale in /healthz/live"
    - "Script verifies all job entries in liveness response are not stale"
    - "Script prints clear pass/fail summary with evidence"
  artifacts:
    - path: "tests/e2e/test-heartbeat-liveness.sh"
      provides: "Standalone E2E heartbeat liveness validation script"
  key_links:
    - from: "tests/e2e/test-heartbeat-liveness.sh"
      to: "tests/e2e/lib/common.sh"
      via: "source"
      pattern: "source.*lib/common.sh"
    - from: "tests/e2e/test-heartbeat-liveness.sh"
      to: "tests/e2e/lib/kubectl.sh"
      via: "source"
      pattern: "source.*lib/kubectl.sh"
---

<objective>
Create a standalone E2E bash script that builds the Docker image, deploys to K8s via rolling restart, waits for pods, then validates that the /healthz/live endpoint reports pipeline-heartbeat as NOT stale and all job entries as NOT stale.

Purpose: Validate the v1.10 heartbeat liveness feature works end-to-end in a real K8s cluster.
Output: tests/e2e/test-heartbeat-liveness.sh -- executable, self-contained test script.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@tests/e2e/run-all.sh
@tests/e2e/lib/common.sh
@tests/e2e/lib/kubectl.sh
@tests/e2e/lib/prometheus.sh
@src/SnmpCollector/HealthChecks/LivenessHealthCheck.cs
@src/SnmpCollector/HealthChecks/HealthCheckJsonWriter.cs
@deploy/k8s/snmp-collector/deployment.yaml
@Dockerfile
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create test-heartbeat-liveness.sh E2E script</name>
  <files>tests/e2e/test-heartbeat-liveness.sh</files>
  <action>
Create `tests/e2e/test-heartbeat-liveness.sh` as a standalone bash script. Follow the established
pattern from `run-all.sh` -- source the shared libs, use cleanup trap, use record_pass/record_fail.

Script structure (in order):

1. **Preamble**: `#!/usr/bin/env bash`, `set -euo pipefail`, resolve SCRIPT_DIR relative to
   the script's own location (same as run-all.sh). Source `lib/common.sh` and `lib/kubectl.sh`.
   Do NOT source `lib/prometheus.sh` or `lib/report.sh` -- not needed for core flow.

2. **Cleanup trap**: Function that calls `stop_port_forwards` and kills any background pids.
   Register with `trap cleanup EXIT`.

3. **Configuration variables** at top of script:
   - `IMAGE_NAME="snmp-collector:local"`
   - `DEPLOYMENT="snmp-collector"`
   - `NAMESPACE="simetra"`
   - `HEALTH_PORT=18080` (use 18080 to avoid conflict with any existing port-forward)
   - `LIVENESS_PATH="/healthz/live"`
   - `WAIT_READY_TIMEOUT=120` (seconds to wait for pods after restart)
   - `LIVENESS_SETTLE_WAIT=45` (seconds after pods ready to let heartbeat stamps populate --
     HeartbeatJob fires immediately on startup but pipeline-arrival needs one full loop)
   - `SKIP_BUILD` and `SKIP_DEPLOY` env vars (default false) so user can re-run validation
     without rebuilding: `SKIP_BUILD=${SKIP_BUILD:-false}`, `SKIP_DEPLOY=${SKIP_DEPLOY:-false}`

4. **Banner**: Print script name, timestamp (same style as run-all.sh).

5. **Step 1 - Build** (unless SKIP_BUILD=true):
   `log_info "Building Docker image..."` then run:
   `docker build -t "$IMAGE_NAME" -f Dockerfile .`
   Run from repo root. The SCRIPT_DIR is `tests/e2e`, so cd to `$SCRIPT_DIR/../..` for the build
   context. Use a subshell: `(cd "$SCRIPT_DIR/../.." && docker build -t "$IMAGE_NAME" -f Dockerfile .)`
   On failure, `log_error` and exit 1.

6. **Step 2 - Deploy** (unless SKIP_DEPLOY=true):
   `log_info "Rolling restart deployment..."` then:
   `kubectl rollout restart deployment/"$DEPLOYMENT" -n "$NAMESPACE"`
   Then wait: `kubectl rollout status deployment/"$DEPLOYMENT" -n "$NAMESPACE" --timeout="${WAIT_READY_TIMEOUT}s"`
   On failure, `log_error` and exit 1.

7. **Step 3 - Wait for pods ready**: Use a polling loop (not just rollout status -- we need
   container readiness, not just rollout). Poll `check_pods_ready` every 5s up to
   WAIT_READY_TIMEOUT. If timeout, log_error and exit 1.

8. **Step 4 - Wait for heartbeat liveness to settle**: `log_info "Waiting ${LIVENESS_SETTLE_WAIT}s for heartbeat stamps to populate..."`,
   `sleep "$LIVENESS_SETTLE_WAIT"`. This is necessary because pipeline-heartbeat needs the
   HeartbeatJob to fire, the loopback trap to traverse the full pipeline, and the
   OtelMetricHandler to stamp IHeartbeatLivenessService. With IntervalSeconds=15 and
   GraceMultiplier=2.0, the threshold is 30s, so 45s gives comfortable margin.

9. **Step 5 - Port-forward**: Pick a single pod (not service -- health endpoints are per-pod):
   ```
   POD=$(kubectl get pods -n "$NAMESPACE" -l app=snmp-collector -o jsonpath='{.items[0].metadata.name}')
   ```
   Then port-forward:
   ```
   kubectl port-forward "pod/$POD" "${HEALTH_PORT}:8080" -n "$NAMESPACE" &>/dev/null &
   PF_PIDS+=($!)
   sleep 2
   ```

10. **Step 6 - Query /healthz/live**:
    ```
    RESPONSE=$(curl -sf "http://localhost:${HEALTH_PORT}${LIVENESS_PATH}" 2>&1) || { ... }
    ```
    Store raw JSON. Print it with `log_info "Response: $RESPONSE"`.

11. **Step 7 - Parse and validate**: Use jq to extract and verify. The JSON structure from
    HealthCheckJsonWriter is:
    ```json
    {
      "status": "Healthy|Unhealthy",
      "checks": [
        {
          "name": "liveness",
          "status": "Healthy|Unhealthy",
          "data": {
            "pipeline-heartbeat": { "ageSeconds": N, "thresholdSeconds": N, "stale": bool },
            "jobkey1": { "ageSeconds": N, "thresholdSeconds": N, "stale": bool }
          }
        }
      ]
    }
    ```

    Extract the liveness check's data object:
    ```
    LIVENESS_DATA=$(echo "$RESPONSE" | jq -r '.checks[] | select(.name == "liveness") | .data')
    ```

    **Test A - Overall status**: Check top-level `status` is "Healthy".
    ```
    OVERALL=$(echo "$RESPONSE" | jq -r '.status')
    if [ "$OVERALL" = "Healthy" ]; then record_pass "overall-liveness-healthy" "status=$OVERALL"
    else record_fail "overall-liveness-healthy" "status=$OVERALL"; fi
    ```

    **Test B - pipeline-heartbeat exists**: Check pipeline-heartbeat key exists in data.
    ```
    HAS_PIPELINE=$(echo "$LIVENESS_DATA" | jq 'has("pipeline-heartbeat")')
    ```
    record_pass/record_fail accordingly.

    **Test C - pipeline-heartbeat not stale**: Check `stale` is false.
    ```
    PIPELINE_STALE=$(echo "$LIVENESS_DATA" | jq -r '.["pipeline-heartbeat"].stale')
    PIPELINE_AGE=$(echo "$LIVENESS_DATA" | jq -r '.["pipeline-heartbeat"].ageSeconds')
    ```
    record_pass if stale=false, record_fail if stale=true. Include ageSeconds in evidence.

    **Test D - No stale jobs**: Iterate all keys in data, check none have stale=true.
    ```
    STALE_COUNT=$(echo "$LIVENESS_DATA" | jq '[to_entries[] | select(.value.stale == true)] | length')
    ```
    record_pass if 0, record_fail with stale job names if > 0.

    **Test E (optional) - pipeline-heartbeat age is reasonable**: ageSeconds should be < thresholdSeconds.
    ```
    PIPELINE_THRESHOLD=$(echo "$LIVENESS_DATA" | jq -r '.["pipeline-heartbeat"].thresholdSeconds')
    ```
    Compare age < threshold. This is a softer check -- use record_pass/record_fail.

12. **Step 8 - Optional Prometheus check**: Gate behind `CHECK_PROMETHEUS=${CHECK_PROMETHEUS:-false}`.
    If true, source `lib/prometheus.sh`, start prometheus port-forward, query
    `snmp_event_handled_total{device_name="Simetra"}`, assert count > 0 using `assert_exists`.
    This is optional because Prometheus may not be deployed in all dev environments.

13. **Summary**: Call `print_summary`, then exit with `[ "$FAIL_COUNT" -eq 0 ]`.

Make the script executable: `chmod +x tests/e2e/test-heartbeat-liveness.sh`.

IMPORTANT: Use consistent quoting. All variable expansions in double quotes. Use `jq -e` where
appropriate for safer parsing. Handle curl failures gracefully -- if /healthz/live returns non-200,
record_fail for all tests rather than crashing.
  </action>
  <verify>
  - `bash -n tests/e2e/test-heartbeat-liveness.sh` (syntax check passes)
  - `head -1 tests/e2e/test-heartbeat-liveness.sh` shows `#!/usr/bin/env bash`
  - `grep -c record_pass tests/e2e/test-heartbeat-liveness.sh` shows >= 4 (at least 4 assertions)
  - `grep -c record_fail tests/e2e/test-heartbeat-liveness.sh` shows >= 4 (matching failure paths)
  - `grep 'pipeline-heartbeat' tests/e2e/test-heartbeat-liveness.sh` finds validation logic
  - `grep 'SKIP_BUILD\|SKIP_DEPLOY' tests/e2e/test-heartbeat-liveness.sh` finds skip flags
  </verify>
  <done>
  Script passes bash syntax check, contains at least 4 named assertions (overall-healthy,
  pipeline-heartbeat-exists, pipeline-heartbeat-not-stale, no-stale-jobs), sources shared libs,
  handles SKIP_BUILD/SKIP_DEPLOY flags, and follows established E2E patterns from run-all.sh.
  </done>
</task>

</tasks>

<verification>
- `bash -n tests/e2e/test-heartbeat-liveness.sh` -- syntax valid
- Script sources lib/common.sh and lib/kubectl.sh (grep confirms)
- Script has cleanup trap for port-forward cleanup
- Script has >= 4 distinct test assertions with record_pass/record_fail
- Script validates pipeline-heartbeat key specifically
</verification>

<success_criteria>
- tests/e2e/test-heartbeat-liveness.sh exists, is executable, passes bash -n syntax check
- Script builds image, deploys, waits, port-forwards, queries /healthz/live, validates JSON
- At least 4 assertions: overall healthy, pipeline-heartbeat exists, pipeline-heartbeat not stale, no stale jobs
- SKIP_BUILD and SKIP_DEPLOY env vars supported for re-running validation only
- Follows existing E2E lib patterns (source common.sh/kubectl.sh, record_pass/fail, print_summary)
</success_criteria>

<output>
After completion, create `.planning/quick/059-build-deploy-test-heartbeat-liveness/059-SUMMARY.md`
</output>
