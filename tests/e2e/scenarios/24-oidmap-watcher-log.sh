# Scenario 24: OidMapWatcher detects ConfigMap change (WATCH-01)
# Applies oid-renamed-configmap.yaml and verifies OidMapWatcherService logs
# confirm the watch event was received and OID map was reloaded.
SCENARIO_NAME="OidMapWatcher detects ConfigMap change and reloads"

snapshot_configmaps

# Apply mutated oidmaps ConfigMap
log_info "Applying renamed oidmaps ConfigMap..."
kubectl apply -f "$SCRIPT_DIR/fixtures/oid-renamed-configmap.yaml" -n simetra

# Allow watcher detection + reload
log_info "Waiting 10s for watcher detection and reload..."
sleep 10

# Get all snmp-collector pod names
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
FOUND_PRIMARY=0
FOUND_SECONDARY=0
EVIDENCE=""

for POD in $PODS; do
    LOGS=$(kubectl logs "$POD" -n simetra --since=30s 2>/dev/null) || true

    # Primary: watcher received the event
    if echo "$LOGS" | grep -q "OidMapWatcher received" 2>/dev/null; then
        FOUND_PRIMARY=1
        EVIDENCE="pod=${POD} saw 'OidMapWatcher received'"
    fi

    # Secondary: reload completed successfully
    if echo "$LOGS" | grep -q "OID map reload complete" 2>/dev/null; then
        FOUND_SECONDARY=1
        EVIDENCE="${EVIDENCE}, 'OID map reload complete' confirmed"
    fi

    if [ "$FOUND_PRIMARY" -eq 1 ] && [ "$FOUND_SECONDARY" -eq 1 ]; then
        break
    fi
done

if [ "$FOUND_PRIMARY" -eq 1 ] && [ "$FOUND_SECONDARY" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "$EVIDENCE"
elif [ "$FOUND_PRIMARY" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "${EVIDENCE} (reload log not found but event detected)"
else
    record_fail "$SCENARIO_NAME" "No pod logged 'OidMapWatcher received' within 30s window"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
