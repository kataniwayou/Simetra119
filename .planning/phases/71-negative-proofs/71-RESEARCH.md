# Phase 71: Negative Proofs - Research

**Researched:** 2026-03-22
**Domain:** E2E negative-path scenario scripting in bash/Prometheus PromQL
**Confidence:** HIGH

---

## Summary

Phase 71 adds five E2E scenarios (MNP-01 through MNP-05) proving the system provably suppresses
or withholds metrics in every defined negative-path. Each scenario must produce exactly one
`SCENARIO_RESULTS` entry and follow the established pattern used by existing negative scenarios
(scenario 78 is the closest prior art).

The scenarios are straightforward shell scripts that use the existing `query_prometheus`,
`record_pass`, `record_fail` helper functions. The primary challenge is understanding exactly
what the pipeline does or does not emit for each negative path, which is now fully understood
from code inspection. The report category system must be extended with one new entry.

**Primary recommendation:** Model MNP-01/02 on `query_prometheus` result-count checks (count == 0
means metric absent), model MNP-03 as a reference to MCV-09b's proof, model MNP-04 on MCV-07
(delta == 0 guard), and model MNP-05 using `kubectl port-forward` to a specific follower pod.

---

## What Was Learned Per Research Question

### Q1: How does the heartbeat OID enter the pipeline?

**Source:** `HeartbeatJob.cs`, `HeartbeatJobOptions.cs` (HIGH confidence)

HeartbeatJob fires a real SNMPv2c loopback trap to `127.0.0.1:<listenerPort>` with:
- OID: `1.3.6.1.4.1.9999.1.1.1.0` (`HeartbeatJobOptions.HeartbeatOid`)
- Community string: `Simetra.Simetra` (derived via `CommunityStringHelper.DeriveFromDeviceName("Simetra")`)
- Interval: 15 seconds (default)
- Value: Counter32 (autoincrement)

The trap is a real UDP datagram to the loopback. It goes through `SnmpTrapListenerService` ->
`TrapChannel` -> `ChannelConsumerService` -> MediatR pipeline (Validation -> OidResolution ->
OtelMetricHandler) exactly like any external trap.

### Q2: Is the heartbeat OID in oidmaps.json? What metric name does it get?

**Source:** `OidMapService.cs` `MergeWithHeartbeatSeed()` method (HIGH confidence)

The heartbeat OID `1.3.6.1.4.1.9999.1.1.1.0` is **NOT** in oidmaps.json (the user-facing config).
However, `OidMapService.MergeWithHeartbeatSeed()` always injects it into the in-memory map with
`MetricName = "Heartbeat"` before building the FrozenDictionary. This means:

- OidResolutionBehavior resolves the OID to `"Heartbeat"` (not `"Unknown"`)
- OtelMetricHandler calls `_metricFactory.RecordGauge("Heartbeat", ...)` with TypeCode=Counter32
- `SnmpMetricFactory.RecordGauge()` creates/records to the `snmp_gauge` instrument on the
  `SnmpCollector.Leader` meter
- **MetricRoleGatedExporter gates the Leader meter behind `IsLeader`** — only the leader pod
  exports `snmp_gauge` to OTLP/Prometheus

So `snmp_gauge{device_name="Simetra", resolved_name="Heartbeat"}` WILL exist in Prometheus
on the leader. MNP-01 must assert that this series does NOT exist. This means the scenario
must query for `snmp_gauge{device_name="Simetra"}` or `snmp_gauge{resolved_name="Heartbeat"}`
and check that result count == 0.

**Critical correction for MNP-01:** The heartbeat OID resolves to `"Heartbeat"` (not "Unknown"),
and the heartbeat DOES reach OtelMetricHandler and DOES call RecordGauge. The leader WILL export
`snmp_gauge{resolved_name="Heartbeat", device_name="Simetra"}`. MNP-01's success criterion
"Heartbeat OID never appears as snmp_gauge" requires re-examination.

Wait — re-reading the success criterion: "Heartbeat OID never appears as snmp_gauge or snmp_info
in Prometheus." But the code does emit it. This is a contradiction with the codebase reality.
**The heartbeat OID will appear in Prometheus as `snmp_gauge{resolved_name="Heartbeat"}`.**

The scenario can only test that the heartbeat is classified as `resolved_name="Heartbeat"` (not
leaked under an Unknown or incorrect label), OR that the specific OID `1.3.6.1.4.1.9999.1.1.1.0`
does not appear with `resolved_name="Unknown"`. The MNP-01 scenario as specified in the phase
context cannot be literally true — the heartbeat metric IS in Prometheus.

**Planner must reconcile:** Either MNP-01 becomes "heartbeat OID is classified as Heartbeat,
not leaked as Unknown or with device_name of a real device" (a positive label-correctness test),
or the planner accepts that MNP-01 can only test `snmp_info` (the heartbeat is Counter32 so it
goes through RecordGauge, not RecordInfo — so `snmp_info` with Heartbeat will never appear,
which is a valid negative proof for that instrument).

**Most defensible MNP-01 formulation:** Assert `snmp_info{device_name="Simetra"}` has count==0
(heartbeat is numeric, never appears in snmp_info) AND assert that `snmp_gauge{device_name="Simetra"}`
has count > 0 with `resolved_name="Heartbeat"` (heartbeat IS in gauge, correctly labeled).
This proves it doesn't bleed into wrong label combinations.

Alternatively, the scenario can be framed as: "Heartbeat does not appear with resolved_name=Unknown"
or "no snmp_gauge/snmp_info series exist for device_name=Simetra with resolved_name=Unknown."

### Q3: Does heartbeat produce snmp_gauge?

Yes. The heartbeat trap is Counter32 type. OtelMetricHandler case `Counter32` calls RecordGauge.
RecordGauge calls `_metricFactory.RecordGauge("Heartbeat", ...)` which records on the Leader meter.
MetricRoleGatedExporter passes it through on the leader. Prometheus will have
`snmp_gauge{resolved_name="Heartbeat", device_name="Simetra", oid="1.3.6.1.4.1.9999.1.1.1.0", source="trap"}`.

### Q4: For MNP-02 (unmapped OID) — can we query for absence?

**Source:** `OidMapService.cs`, `e2e-sim-unmapped-configmap.yaml`, `15-unknown-oid.sh` (HIGH confidence)

The unmapped OIDs `.999.2.1` and `.999.2.2` are served by the E2E simulator but are **NOT** in
oidmaps.json and NOT in the poll config (devices.json). Since they're not in the poll config,
the collector never polls them. They also don't send traps. So they're truly absent.

The safest negative proof is: `snmp_gauge{oid="1.3.6.1.4.1.47477.999.2.1.0"}` returns 0 results,
and `snmp_info{oid="1.3.6.1.4.1.47477.999.2.2.0"}` returns 0 results.

No ConfigMap mutation is needed — these OIDs are simply never polled in the base config.
The prior scenario 15 (`15-unknown-oid.sh`) actually ADDS these OIDs to poll config and verifies
they appear as Unknown. MNP-02 should not repeat that mutation; it tests the baseline state where
they are absent entirely.

### Q5: MNP-03 (bad-community trap) vs MCV-09b — can MNP-03 reference/reuse?

**Source:** `78-mcv09b-trap-received-negative.sh` (HIGH confidence)

Scenario 78 (MCV-09b) already proves: "bad-community traps do not increment `snmp_trap_received_total`."
MCV-09b also proves bad-community traps never reach ChannelConsumerService (which is the only path
to MediatR and OtelMetricHandler). Therefore bad-community traps cannot produce `snmp_gauge` or
`snmp_info`.

MNP-03 is effectively a re-statement of MCV-09b for the business metric dimension. The scenario
can be short: wait for at least one auth_failed increment (confirming bad trap arrived), then assert
`snmp_gauge{device_name="unknown"}` has 0 results and `snmp_info{device_name="unknown"}` has 0 results.
This extends MCV-09b's proof to the Prometheus business metric dimension.

Note: bad-community traps set device_name="unknown" in auth_failed counter (line 146 of
`SnmpTrapListenerService.cs`), but that community string never reaches channel consumer — so
device_name="unknown" will never appear in snmp_gauge or snmp_info.

### Q6: MNP-04 (trap.dropped == 0) — pattern

**Source:** `75-mcv07-errors-stays-zero.sh`, `PipelineMetricService.cs`, `TrapChannel.cs` (HIGH confidence)

`snmp.trap.dropped` is a counter on the standard `SnmpCollector` meter (not the Leader meter),
so it's exported by ALL instances regardless of role. It only increments when `TrapChannel`'s
`BoundedChannelOptions(FullMode=DropOldest)` drops an item. Under normal E2E load this never fires.

Pattern: snapshot `snmp_trap_dropped_total` before, wait for pipeline activity, snapshot after,
assert delta == 0. Use `poll_until` to confirm pipeline is active (using `snmp_trap_received_total`
as the activity proof), then assert dropped delta == 0.

Existing scenario 10 (`10-trap-dropped.sh`) only checks if the metric exists (passes either way).
MNP-04 should actually assert the delta is 0, making it a true negative proof.

### Q7 & Q8: MNP-05 (follower pod metrics) — how to identify follower and query it

**Source:** `deployment.yaml`, `ILeaderElection.cs`, `MetricRoleGatedExporter.cs` (HIGH confidence)

The deployment is a Kubernetes Deployment (NOT a StatefulSet) with `replicas: 3`. Pods are named
`snmp-collector-<replicaset-hash>-<random>`. There is no stable pod ordering.

The snmp-collector service (`service.yaml`) only exposes port 8080 (health) and port 162 (SNMP UDP).
**There is no dedicated Prometheus metrics HTTP endpoint on snmp-collector pods.** The metrics
flow through OTLP gRPC to the otel-collector (port 4317), which then remote-writes to Prometheus.

This is the critical architectural finding for MNP-05: **snmp-collector pods do not expose an
HTTP Prometheus scrape endpoint.** There is no `/metrics` endpoint to curl. The MetricRoleGatedExporter
gates what gets sent to the OTLP collector. You cannot directly curl a follower pod's metrics endpoint
because the pod does not serve one.

**Consequence for MNP-05:** The follower proof must come from a different angle:
- Query Prometheus for `snmp_gauge` and filter by the `k8s.pod.name` resource attribute (set in
  `ServiceCollectionExtensions.cs` line 82 as `"k8s.pod.name": podName`). If Prometheus receives
  remote_write with this resource attribute converted to a label, follower pods' series would
  be distinguishable. However, this depends on whether `resource_to_telemetry_conversion.enabled: true`
  in the OTel collector config converts the resource attribute to a label.

**Checking OTel collector config:** `otel-collector.yaml` line 23 shows
`resource_to_telemetry_conversion.enabled: true` in the `prometheusremotewrite` exporter. This means
resource attributes ARE converted to metric labels. So `k8s.pod.name` becomes a Prometheus label.

**MNP-05 approach:** List all `snmp-collector` pods, identify which are not the leader by checking
Prometheus for series with `k8s_pod_name=<podname>` that have `snmp_gauge`. The leader's pod name
will have snmp_gauge series; follower pod names will have no snmp_gauge series (since
MetricRoleGatedExporter suppresses them). Actually `k8s.pod.name` has a dot; in Prometheus label
names dots become underscores. The label becomes `k8s_pod_name`.

**Implementation:**
1. `kubectl get pods -n simetra -l app=snmp-collector -o jsonpath=...` to get pod names
2. For each pod, query `snmp_gauge{k8s_pod_name="<podname>"}` count in Prometheus
3. The leader pod will have snmp_gauge series; followers will have 0 series
4. Identify at least one follower pod (count == 0 for snmp_gauge)
5. Assert that follower pod name has 0 snmp_gauge/snmp_info results in Prometheus
6. This proves MetricRoleGatedExporter is working

Note: `service.instance.id` is set to `PHYSICAL_HOSTNAME` (node name), not pod name. Pod name
comes from `HOSTNAME` env var (the pod's hostname). The `k8s.pod.name` attribute uses the
`HOSTNAME` environment variable (line 73: `var podName = Environment.GetEnvironmentVariable("HOSTNAME")`).

### Q9: kubectl port-forward to specific pod

Not needed — MNP-05 uses Prometheus label query approach. However, if direct pod scraping were
needed: `kubectl port-forward pod/<podname> <localport>:8080` — but there's no /metrics at port 8080
(that's the health endpoint). Port-forward is not applicable here.

### Q10: Report categories

**Source:** `report.sh` (HIGH confidence)

Current last category: `"Label Correctness|96|103"` (indices 96-103, 8 scenarios: 97-104 in
1-based numbering). The phase context states scenarios continue from 102 with indices from 104.
The new category covering 5 scenarios at indices 104-108 (scenarios 105-109 in 1-based numbers)
should be: `"Negative Proofs|104|108"`.

---

## Architecture Patterns

### Pattern 1: Negative metric presence check (count == 0)

```bash
# Source: 100-mlc07-resolved-name.sh / 101-mlc08-device-name.sh pattern (inverted)
RESPONSE=$(query_prometheus 'snmp_gauge{device_name="Simetra",resolved_name="Heartbeat"}')
RESULT_COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')

if [ "$RESULT_COUNT" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "no snmp_gauge{device_name=Simetra} series found (suppressed)"
else
    record_fail "$SCENARIO_NAME" "snmp_gauge{device_name=Simetra} has $RESULT_COUNT series -- not suppressed"
fi
```

### Pattern 2: Delta stays zero with activity guard (from MCV-07)

```bash
# Source: 75-mcv07-errors-stays-zero.sh
ACTIVITY_METRIC="snmp_trap_received_total"
GUARD_FILTER='device_name="E2E-SIM"'
DROPPED_METRIC="snmp_trap_dropped_total"

ACTIVITY_BEFORE=$(snapshot_counter "$ACTIVITY_METRIC" "$GUARD_FILTER")
DROPPED_BEFORE=$(snapshot_counter "$DROPPED_METRIC" "")

poll_until 45 "$POLL_INTERVAL" "$ACTIVITY_METRIC" "$GUARD_FILTER" "$ACTIVITY_BEFORE" || true
sleep 20

ACTIVITY_AFTER=$(snapshot_counter "$ACTIVITY_METRIC" "$GUARD_FILTER")
DROPPED_AFTER=$(snapshot_counter "$DROPPED_METRIC" "")
ACTIVITY_DELTA=$((ACTIVITY_AFTER - ACTIVITY_BEFORE))
DROPPED_DELTA=$((DROPPED_AFTER - DROPPED_BEFORE))

if [ "$ACTIVITY_DELTA" -gt 0 ]; then
    assert_delta_eq "$DROPPED_DELTA" 0 "$SCENARIO_NAME" "activity_delta=$ACTIVITY_DELTA dropped_delta=$DROPPED_DELTA"
else
    record_fail "$SCENARIO_NAME" "Pipeline inactive -- cannot verify negative assertion"
fi
```

### Pattern 3: Auth-failed confirm then check business metric absence (from MCV-09b)

```bash
# Source: 78-mcv09b-trap-received-negative.sh
AUTH_BEFORE=$(snapshot_counter "snmp_trap_auth_failed_total" "")
poll_until 75 "$POLL_INTERVAL" "snmp_trap_auth_failed_total" "" "$AUTH_BEFORE" || true
sleep 15

AUTH_AFTER=$(snapshot_counter "snmp_trap_auth_failed_total" "")
AUTH_DELTA=$((AUTH_AFTER - AUTH_BEFORE))

UNKNOWN_GAUGE=$(query_prometheus 'snmp_gauge{device_name="unknown"}' | jq -r '.data.result | length')

if [ "$AUTH_DELTA" -gt 0 ]; then
    if [ "$UNKNOWN_GAUGE" -eq 0 ]; then
        record_pass "$SCENARIO_NAME" "auth_failed fired; no snmp_gauge{device_name=unknown} (auth_delta=$AUTH_DELTA)"
    else
        record_fail "$SCENARIO_NAME" "snmp_gauge{device_name=unknown} found ($UNKNOWN_GAUGE series)"
    fi
else
    record_fail "$SCENARIO_NAME" "No bad-community trap in window -- cannot verify"
fi
```

### Pattern 4: Follower pod identification via Prometheus labels

```bash
# Get all pod names
POD_NAMES=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{.items[*].metadata.name}')

FOLLOWER_POD=""
for POD in $POD_NAMES; do
    COUNT=$(query_prometheus "snmp_gauge{k8s_pod_name=\"${POD}\"}" \
        | jq -r '.data.result | length')
    if [ "$COUNT" -eq 0 ]; then
        FOLLOWER_POD="$POD"
        break
    fi
done

if [ -n "$FOLLOWER_POD" ]; then
    record_pass "$SCENARIO_NAME" "follower pod $FOLLOWER_POD has 0 snmp_gauge series"
else
    record_fail "$SCENARIO_NAME" "could not identify follower pod (all pods have snmp_gauge?)"
fi
```

---

## Scenario-by-Scenario Analysis

### MNP-01: Heartbeat OID never appears as snmp_gauge/snmp_info in Prometheus

**Reality:** The heartbeat DOES appear as `snmp_gauge{resolved_name="Heartbeat", device_name="Simetra"}`.
The success criterion as written cannot pass.

**Recommended reframe:** MNP-01 should prove "heartbeat OID does not appear with
`resolved_name="Unknown"`" (it was correctly seeded in OidMapService) AND "heartbeat does not
appear in snmp_info" (it's Counter32, never octetstring). This is a meaningful negative proof about
label correctness and type routing.

Alternative reframe: "No snmp_gauge or snmp_info series exist for the heartbeat OID with an
unexpected label value (device_name != 'Simetra' or resolved_name != 'Heartbeat')." This tests
that the heartbeat is correctly isolated and not leaking into real device metric namespaces.

Most testable formulation: Assert 0 results for `snmp_info{device_name="Simetra"}` (heartbeat is
numeric, never goes to snmp_info) and assert 0 results for `snmp_gauge{device_name="Simetra",resolved_name="Unknown"}`.

### MNP-02: Unmapped OID produces no snmp_gauge or snmp_info

**Reality:** Unmapped OIDs `.999.2.x` are not in poll config and not sending traps. They will
have 0 Prometheus series. Safe to query `snmp_gauge{oid="1.3.6.1.4.1.47477.999.2.1.0"}` and
`snmp_info{oid="1.3.6.1.4.1.47477.999.2.2.0"}` for count == 0 without any ConfigMap mutation.

No waiting needed — these are baseline absence assertions on stable data.

### MNP-03: Bad-community trap produces no increment to snmp.trap.received

**Reality:** Proven by MCV-09b (scenario 78). MNP-03 should extend this proof by also asserting
no `snmp_gauge{device_name="unknown"}` and no `snmp_info{device_name="unknown"}`. Requires waiting
for `auth_failed` increment to confirm a bad trap arrived (same pattern as MCV-09b).

### MNP-04: snmp.trap.dropped reads 0 after normal E2E run

**Reality:** `snmp_trap_dropped_total` is on the standard meter (not leader-gated). Under normal
load the channel (bounded capacity, DropOldest) never fills. Pattern: delta == 0 with activity guard.
Need to handle the case where `snmp_trap_dropped_total` was never incremented and thus doesn't
exist in Prometheus (use `query_counter` which returns 0 for absent metrics via `or vector(0)`).

### MNP-05: Follower pod scrape target returns no snmp_gauge/snmp_info

**Reality:** There is no direct /metrics endpoint to scrape on pods. The proof must come from
Prometheus labels. `k8s.pod.name` resource attribute is converted to `k8s_pod_name` Prometheus
label via `resource_to_telemetry_conversion.enabled: true`. Query Prometheus for each pod's
`snmp_gauge` series count. Leader will have series; followers will have 0. Identify a follower
and assert 0 series.

**Risk:** If Prometheus label conversion changes the dot to underscore differently, label name
may differ. Also, the label name in Prometheus from OTel resource attributes uses the exact
attribute name with dots replaced by underscores: `k8s.pod.name` -> `k8s_pod_name`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Prometheus absence check | Custom HTTP query | `query_prometheus` + `jq .data.result \| length` | Already in prometheus.sh |
| Counter delta tracking | Custom counters | `snapshot_counter` + arithmetic | Already in prometheus.sh |
| Activity waiting | Sleep loop | `poll_until` | Already in prometheus.sh |
| Pod list | kubectl parse | `kubectl get pods -o jsonpath` | Standard K8s pattern |

---

## Common Pitfalls

### Pitfall 1: MNP-01 success criterion conflicts with codebase reality
**What goes wrong:** Writing a scenario that asserts `snmp_gauge{device_name="Simetra"}` == 0
when the heartbeat legitimately produces this series.
**How to avoid:** Reframe MNP-01 as "heartbeat does not appear in snmp_info" (which is true) and
"heartbeat does not appear with resolved_name=Unknown" (which is true). Do not assert absence of
snmp_gauge for the Simetra device.

### Pitfall 2: Asserting metric absence before pipeline has run
**What goes wrong:** Checking absence immediately without confirming other metrics are active, then
passing vacuously if Prometheus hasn't scraped yet.
**How to avoid:** For MNP-02, the metrics have had time to appear since the collector has been
running for the whole E2E suite before MNP-02 runs. For MNP-03, use the auth_failed guard.

### Pitfall 3: k8s_pod_name label may not exist
**What goes wrong:** If `resource_to_telemetry_conversion` doesn't convert `k8s.pod.name` as expected,
the MNP-05 query finds 0 results for ALL pods.
**How to avoid:** First verify that any pod has `k8s_pod_name` in at least one snmp_gauge series.
If the label doesn't exist, record_fail with a meaningful diagnostic message.

### Pitfall 4: MNP-03 duplicating MCV-09b
**What goes wrong:** Writing an identical scenario to 78-mcv09b-trap-received-negative.sh.
**How to avoid:** MNP-03 adds the business metric dimension (snmp_gauge/snmp_info) not tested
in MCV-09b. MCV-09b only checked `snmp_trap_received_total`. MNP-03 checks snmp_gauge/snmp_info.

### Pitfall 5: snmp_trap_dropped_total not registered = query returns 0 naturally
**What goes wrong:** `snmp_trap_dropped_total` never fires in normal operation, so it may not
appear in Prometheus at all. `query_counter` uses `or vector(0)` so it returns 0 — this looks
like a pass but could be vacuous.
**How to avoid:** Add an activity guard: only assert delta==0 if trap activity was confirmed.
Also note: the counter might genuinely not exist; that's acceptable (scenario 10 already handles
this check). MNP-04 should use the activity guard pattern.

---

## File Naming and Indices

Current last scenario file: `101-mlc08-device-name.sh` at SCENARIO_RESULTS index 103 (0-based).

New files:
```
102-mnp01-heartbeat-not-in-snmp-info.sh        → SCENARIO_RESULTS[104]
103-mnp02-unmapped-oid-absent.sh               → SCENARIO_RESULTS[105]
104-mnp03-bad-community-no-business-metric.sh  → SCENARIO_RESULTS[106]
105-mnp04-trap-dropped-stays-zero.sh           → SCENARIO_RESULTS[107]
106-mnp05-follower-no-snmp-gauge.sh            → SCENARIO_RESULTS[108]
```

Report category addition to `report.sh`:
```bash
"Negative Proofs|104|108"
```
This covers SCENARIO_RESULTS indices 104-108 (5 scenarios).

---

## Standard Stack

The scenarios use the same stack as all other E2E scenarios:

| Tool | Version | Purpose |
|------|---------|---------|
| bash | system | Script runtime |
| kubectl | cluster version | Pod enumeration for MNP-05 |
| curl | system | Prometheus HTTP API (via prometheus.sh) |
| jq | system | JSON parsing |

No new libraries or tools are needed.

**Sourced libraries:** `lib/common.sh`, `lib/prometheus.sh`, `lib/kubectl.sh`, `lib/report.sh`
(all sourced automatically by run-all.sh before scenario files execute).

---

## Code Examples

### Absence assertion pattern (for MNP-01, MNP-02, MNP-03, MNP-05)
```bash
# Check that a specific series is absent from Prometheus
RESPONSE=$(query_prometheus 'snmp_info{device_name="Simetra"}')
COUNT=$(echo "$RESPONSE" | jq -r '.data.result | length')
if [ "$COUNT" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "snmp_info{device_name=Simetra} has 0 series (correctly suppressed)"
else
    record_fail "$SCENARIO_NAME" "snmp_info{device_name=Simetra} has $COUNT series -- expected 0"
fi
```

### Pipeline activity guard (for MNP-04)
```bash
# Verify activity then assert counter stayed at zero
ACTIVITY_BEFORE=$(snapshot_counter "snmp_trap_received_total" 'device_name="E2E-SIM"')
DROPPED_BEFORE=$(snapshot_counter "snmp_trap_dropped_total" "")
poll_until 45 "$POLL_INTERVAL" "snmp_trap_received_total" 'device_name="E2E-SIM"' "$ACTIVITY_BEFORE" || true
sleep 20
ACTIVITY_AFTER=$(snapshot_counter "snmp_trap_received_total" 'device_name="E2E-SIM"')
DROPPED_AFTER=$(snapshot_counter "snmp_trap_dropped_total" "")
ACTIVITY_DELTA=$((ACTIVITY_AFTER - ACTIVITY_BEFORE))
DROPPED_DELTA=$((DROPPED_AFTER - DROPPED_BEFORE))
EVIDENCE="activity_delta=$ACTIVITY_DELTA dropped_delta=$DROPPED_DELTA"
if [ "$ACTIVITY_DELTA" -gt 0 ]; then
    assert_delta_eq "$DROPPED_DELTA" 0 "$SCENARIO_NAME" "$EVIDENCE"
else
    record_fail "$SCENARIO_NAME" "Pipeline inactive -- cannot verify dropped=0. $EVIDENCE"
fi
```

### Follower pod identification (for MNP-05)
```bash
# First verify k8s_pod_name label exists
VERIFY=$(query_prometheus 'snmp_gauge' | jq -r '.data.result[0].metric.k8s_pod_name // ""')
if [ -z "$VERIFY" ]; then
    record_fail "$SCENARIO_NAME" "k8s_pod_name label not present in snmp_gauge -- resource_to_telemetry_conversion may not be working"
    # exit early
fi

POD_NAMES=$(kubectl get pods -n simetra -l app=snmp-collector \
    -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}')

FOLLOWER_POD=""
for POD in $POD_NAMES; do
    [ -z "$POD" ] && continue
    COUNT=$(query_prometheus "snmp_gauge{k8s_pod_name=\"${POD}\"}" \
        | jq -r '.data.result | length')
    if [ "$COUNT" -eq 0 ]; then
        FOLLOWER_POD="$POD"
        break
    fi
done
```

---

## Open Questions

1. **MNP-01 success criterion conflict**
   - What we know: Heartbeat legitimately produces `snmp_gauge{resolved_name="Heartbeat", device_name="Simetra"}` in Prometheus on the leader
   - What's unclear: The phase requirement says "Heartbeat OID never appears as snmp_gauge or snmp_info" which contradicts the code
   - Recommendation: Planner should reframe MNP-01 as two negative proofs: (a) heartbeat never appears in snmp_info, (b) heartbeat never appears with resolved_name="Unknown". Both are genuinely verifiable.

2. **k8s_pod_name label reliability for MNP-05**
   - What we know: `resource_to_telemetry_conversion.enabled: true` in otel-collector config, `k8s.pod.name` attribute is set in ServiceCollectionExtensions.cs
   - What's unclear: Whether OTel's prometheusremotewrite exporter converts resource attributes to labels with dots-to-underscores reliably
   - Recommendation: Add a preflight check in MNP-05: verify `k8s_pod_name` label is present in at least one existing snmp_gauge series before attempting the follower identification logic. Fail with diagnostic if label absent.

---

## Sources

### Primary (HIGH confidence)
- `src/SnmpCollector/Jobs/HeartbeatJob.cs` — heartbeat trap sending mechanism
- `src/SnmpCollector/Configuration/HeartbeatJobOptions.cs` — OID and device name constants
- `src/SnmpCollector/Pipeline/OidMapService.cs` — MergeWithHeartbeatSeed, "Heartbeat" resolution
- `src/SnmpCollector/Telemetry/MetricRoleGatedExporter.cs` — follower gating mechanism
- `src/SnmpCollector/Telemetry/TelemetryConstants.cs` — MeterName vs LeaderMeterName
- `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` — Leader meter registration
- `src/SnmpCollector/Services/SnmpTrapListenerService.cs` — bad-community drop path
- `src/SnmpCollector/Services/ChannelConsumerService.cs` — trap.received increment point
- `src/SnmpCollector/Pipeline/TrapChannel.cs` — trap.dropped increment (DropOldest)
- `src/SnmpCollector/Pipeline/Behaviors/OtelMetricHandler.cs` — heartbeat liveness stamp
- `tests/e2e/scenarios/78-mcv09b-trap-received-negative.sh` — MCV-09b prior art
- `tests/e2e/scenarios/75-mcv07-errors-stays-zero.sh` — delta==0 pattern
- `tests/e2e/lib/report.sh` — category format and current indices
- `tests/e2e/lib/prometheus.sh` — query_prometheus, snapshot_counter, poll_until
- `deploy/k8s/snmp-collector/deployment.yaml` — 3 replicas, port 8080 health
- `deploy/k8s/snmp-collector/service.yaml` — no /metrics port exposed
- `deploy/k8s/production/otel-collector.yaml` — resource_to_telemetry_conversion.enabled: true
- `src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs` — k8s.pod.name attribute

---

## Metadata

**Confidence breakdown:**
- Heartbeat pipeline flow: HIGH — read all code paths end-to-end
- MetricRoleGatedExporter gating: HIGH — code is explicit and straightforward
- MNP-01 contradiction: HIGH — code definitively shows heartbeat appears in snmp_gauge
- MNP-02 unmapped absence: HIGH — no poll config, no trap sends
- MNP-03 bad-community: HIGH — trap listener drops before channel write
- MNP-04 trap.dropped: HIGH — TrapChannel DropOldest is the only trigger
- MNP-05 follower approach: MEDIUM — k8s_pod_name label inference from OTel config is sound but not directly tested
- Report category indices: HIGH — counted from report.sh directly

**Research date:** 2026-03-22
**Valid until:** 2026-04-22 (stable codebase)
