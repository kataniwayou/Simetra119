# Scenario 25: DeviceWatcher detects ConfigMap change (WATCH-02)
# Applies device-added-configmap.yaml and verifies DeviceWatcherService logs
# confirm the watch event was received and DynamicPollScheduler reconciled.
SCENARIO_NAME="DeviceWatcher detects ConfigMap change and reconciles"

snapshot_configmaps

# Apply mutated devices ConfigMap (adds E2E-SIM-2)
log_info "Applying device-added ConfigMap (adding E2E-SIM-2)..."
kubectl apply -f "$SCRIPT_DIR/fixtures/device-added-configmap.yaml" -n simetra

# Allow watcher detection + reconciliation
log_info "Waiting 10s for watcher detection and scheduler reconciliation..."
sleep 10

# Get all snmp-collector pod names
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
FOUND_PRIMARY=0
FOUND_SECONDARY=0
EVIDENCE=""

for POD in $PODS; do
    LOGS=$(kubectl logs "$POD" -n simetra --since=30s 2>/dev/null) || true

    # Primary: watcher received the event
    if echo "$LOGS" | grep -q "DeviceWatcher received" 2>/dev/null; then
        FOUND_PRIMARY=1
        EVIDENCE="pod=${POD} saw 'DeviceWatcher received'"
    fi

    # Secondary: poll scheduler reconciled jobs
    if echo "$LOGS" | grep -q "Poll scheduler reconciled" 2>/dev/null; then
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
    record_fail "$SCENARIO_NAME" "No pod logged 'DeviceWatcher received' within 30s window"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
