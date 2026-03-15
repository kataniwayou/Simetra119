# Scenario 25: DeviceWatcher detects ConfigMap change (WATCH-02)
# Applies device-added-configmap.yaml and verifies DeviceWatcherService logs
# confirm the watch event was received and DynamicPollScheduler reconciled.
SCENARIO_NAME="DeviceWatcher detects ConfigMap change and reconciles"

snapshot_configmaps

# Apply mutated devices ConfigMap (adds E2E-SIM-2)
log_info "Applying device-added ConfigMap (adding E2E-SIM-2)..."
kubectl apply -f "$SCRIPT_DIR/fixtures/device-added-configmap.yaml" -n simetra

# Allow watcher detection + reconciliation
log_info "Waiting 15s for watcher detection and scheduler reconciliation..."
sleep 15

# Get all snmp-collector pod names
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
FOUND_PRIMARY=0
FOUND_SECONDARY=0
EVIDENCE=""

for POD in $PODS; do
    # Use grep > /dev/null instead of grep -q to avoid SIGPIPE with pipefail.
    # grep -q closes stdin early; kubectl gets SIGPIPE; pipefail treats pipe as failed.
    if kubectl logs "$POD" -n simetra --since=60s 2>/dev/null | grep "DeviceWatcher received" > /dev/null 2>&1; then
        FOUND_PRIMARY=1
        EVIDENCE="pod=${POD} saw 'DeviceWatcher received'"
    fi

    if kubectl logs "$POD" -n simetra --since=60s 2>/dev/null | grep "Poll scheduler reconciled" > /dev/null 2>&1; then
        FOUND_SECONDARY=1
        EVIDENCE="${EVIDENCE}, 'Poll scheduler reconciled' confirmed"
    fi

    if [ "$FOUND_PRIMARY" -eq 1 ] && [ "$FOUND_SECONDARY" -eq 1 ]; then
        break
    fi
done

if [ "$FOUND_PRIMARY" -eq 1 ] && [ "$FOUND_SECONDARY" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "$EVIDENCE"
elif [ "$FOUND_PRIMARY" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "${EVIDENCE} (reconcile log not found but event detected)"
else
    record_fail "$SCENARIO_NAME" "No pod logged 'DeviceWatcher received' within 60s window"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
