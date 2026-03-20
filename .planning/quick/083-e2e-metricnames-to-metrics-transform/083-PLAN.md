---
phase: quick-083
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/e2e/fixtures/device-added-configmap.yaml
  - tests/e2e/fixtures/fake-device-configmap.yaml
  - tests/e2e/fixtures/device-modified-interval-configmap.yaml
  - tests/e2e/fixtures/device-removed-configmap.yaml
  - tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml
  - tests/e2e/scenarios/06-poll-unreachable.sh
autonomous: true

must_haves:
  truths:
    - "No remaining MetricNames references in tests/e2e/"
    - "All Polls entries use Metrics array of {MetricName: string} objects"
    - "JSON structure remains valid inside all YAML configmaps"
  artifacts:
    - path: "tests/e2e/fixtures/device-added-configmap.yaml"
      contains: '"Metrics"'
    - path: "tests/e2e/fixtures/fake-device-configmap.yaml"
      contains: '"Metrics"'
    - path: "tests/e2e/scenarios/06-poll-unreachable.sh"
      contains: '"Metrics"'
  key_links:
    - from: "tests/e2e/fixtures/*.yaml"
      to: "src/SnmpCollector/Models/PollOptions.cs"
      via: "JSON deserialization contract"
      pattern: '"Metrics".*"MetricName"'
---

<objective>
Transform all E2E fixture files and scripts from the old MetricNames flat-string-array format to the new Metrics object-wrapper format, completing the migration started in quick-082.

Purpose: E2E tests currently use the old `"MetricNames": ["a", "b"]` schema which no longer matches the C# model after quick-082 refactored to `"Metrics": [{"MetricName": "a"}, {"MetricName": "b"}]`. Without this fix, E2E tests will fail on deserialization.
Output: 6 updated files with zero remaining MetricNames references in tests/e2e/.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@tests/e2e/fixtures/device-added-configmap.yaml
@tests/e2e/fixtures/fake-device-configmap.yaml
@tests/e2e/fixtures/device-modified-interval-configmap.yaml
@tests/e2e/fixtures/device-removed-configmap.yaml
@tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml
@tests/e2e/scenarios/06-poll-unreachable.sh
</context>

<tasks>

<task type="auto">
  <name>Task 1: Transform all YAML fixture files (5 files, 34 occurrences)</name>
  <files>
    tests/e2e/fixtures/device-added-configmap.yaml
    tests/e2e/fixtures/fake-device-configmap.yaml
    tests/e2e/fixtures/device-modified-interval-configmap.yaml
    tests/e2e/fixtures/device-removed-configmap.yaml
    tests/e2e/fixtures/e2e-sim-unmapped-configmap.yaml
  </files>
  <action>
    In each YAML configmap fixture, perform the mechanical transform on every `"MetricNames"` array:

    FROM (flat string array):
    ```json
    "MetricNames": [
      "metric_a",
      "metric_b"
    ]
    ```

    TO (object wrapper array):
    ```json
    "Metrics": [
      {"MetricName": "metric_a"},
      {"MetricName": "metric_b"}
    ]
    ```

    Rules:
    - Rename the key from `"MetricNames"` to `"Metrics"`
    - Wrap each string element `"x"` into `{"MetricName": "x"}`
    - For single-element arrays, use inline format: `[{"MetricName": "x"}]`
    - For multi-element arrays, one object per line to match existing indentation style
    - Preserve all surrounding JSON structure, indentation (4-space YAML indent + JSON indent), and YAML pipe-literal formatting
    - Do NOT change any other fields (IntervalSeconds, CommunityString, IpAddress, Port, etc.)

    Occurrence counts to verify against:
    - device-added-configmap.yaml: 8 MetricNames blocks
    - fake-device-configmap.yaml: 7 MetricNames blocks
    - device-modified-interval-configmap.yaml: 7 MetricNames blocks
    - device-removed-configmap.yaml: 6 MetricNames blocks
    - e2e-sim-unmapped-configmap.yaml: 6 MetricNames blocks
  </action>
  <verify>
    Run: `grep -r "MetricNames" tests/e2e/fixtures/` -- must return zero results.
    Run: `grep -c '"Metrics"' tests/e2e/fixtures/*.yaml` -- counts should match the occurrence counts above (8, 7, 7, 6, 6 = 34 total).
    Validate JSON inside each YAML by extracting the pipe-literal block and piping through `python -m json.tool` or `jq .` for each file.
  </verify>
  <done>All 5 YAML fixture files use "Metrics" with object wrappers; zero "MetricNames" references remain; embedded JSON is valid.</done>
</task>

<task type="auto">
  <name>Task 2: Transform inline JSON in shell script (1 file, 1 occurrence)</name>
  <files>
    tests/e2e/scenarios/06-poll-unreachable.sh
  </files>
  <action>
    In `06-poll-unreachable.sh` line 26, transform the inline jq-generated JSON:

    FROM:
    ```bash
    "Polls": [{"IntervalSeconds": 10, "MetricNames": ["e2e_gauge_test"]}]
    ```

    TO:
    ```bash
    "Polls": [{"IntervalSeconds": 10, "Metrics": [{"MetricName": "e2e_gauge_test"}]}]
    ```

    This is a single inline occurrence inside a jq expression. Change only the MetricNames key and wrap the string value. Do not alter any other part of the script.
  </action>
  <verify>
    Run: `grep "MetricNames" tests/e2e/scenarios/06-poll-unreachable.sh` -- must return zero results.
    Run: `grep '"Metrics"' tests/e2e/scenarios/06-poll-unreachable.sh` -- must return 1 match.
    Run: `bash -n tests/e2e/scenarios/06-poll-unreachable.sh` -- syntax check passes (exit 0).
  </verify>
  <done>Shell script uses "Metrics" with object wrapper; zero "MetricNames" references remain; script syntax is valid.</done>
</task>

</tasks>

<verification>
Final sweep across entire tests/e2e/ directory:
- `grep -r "MetricNames" tests/e2e/` returns zero results
- `grep -rc '"Metrics"' tests/e2e/` returns 35 total (34 fixtures + 1 script)
- All YAML files parse correctly (embedded JSON is valid)
- Shell script passes bash syntax check
</verification>

<success_criteria>
- Zero occurrences of "MetricNames" anywhere in tests/e2e/
- 35 occurrences of "Metrics" across all 6 files
- All embedded JSON is structurally valid
- E2E fixtures match the new PollOptions.Metrics schema from quick-082
</success_criteria>

<output>
After completion, create `.planning/quick/083-e2e-metricnames-to-metrics-transform/083-SUMMARY.md`
</output>
