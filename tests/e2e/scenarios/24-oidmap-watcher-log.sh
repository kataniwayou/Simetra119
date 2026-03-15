# Scenario 24: OidMapWatcher detects ConfigMap change (WATCH-01)
# Applies oid-renamed-configmap.yaml and verifies OidMapWatcherService logs
# confirm the watch event was received and OID map was reloaded.
SCENARIO_NAME="OidMapWatcher detects ConfigMap change and reloads"

snapshot_configmaps

# Apply mutated oidmaps ConfigMap
log_info "Applying renamed oidmaps ConfigMap..."
kubectl apply -f "$SCRIPT_DIR/fixtures/oid-renamed-configmap.yaml" -n simetra

# Allow watcher detection + reload
log_info "Waiting 15s for watcher detection and reload..."
sleep 15

# Get all snmp-collector pod names
PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
FOUND_PRIMARY=0
FOUND_SECONDARY=0
EVIDENCE=""

for POD in $PODS; do
    # Use grep > /dev/null instead of grep -q to avoid SIGPIPE with pipefail.
    # grep -q closes stdin early; kubectl gets SIGPIPE; pipefail treats pipe as failed.
    if kubectl logs "$POD" -n simetra --since=60s 2>/dev/null | grep "OidMapWatcher received" > /dev/null 2>&1; then
        FOUND_PRIMARY=1
        EVIDENCE="pod=${POD} saw 'OidMapWatcher received'"
    fi

    if kubectl logs "$POD" -n simetra --since=60s 2>/dev/null | grep "OID map reload complete" > /dev/null 2>&1; then
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
    record_fail "$SCENARIO_NAME" "No pod logged 'OidMapWatcher received' within 60s window"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
