# Scenario 26: Invalid JSON does not crash pods (WATCH-03)
# Applies 4 invalid JSON fixtures (syntax + schema errors for both ConfigMaps)
# and verifies all pods remain Running after each. Collects error log evidence.
SCENARIO_NAME="Invalid JSON ConfigMaps do not crash pods"

snapshot_configmaps

ALL_SURVIVED=1
EVIDENCE=""

INVALID_FIXTURES=(
    "invalid-json-oidmaps-syntax-configmap.yaml"
    "invalid-json-oidmaps-schema-configmap.yaml"
    "invalid-json-devices-syntax-configmap.yaml"
    "invalid-json-devices-schema-configmap.yaml"
)

for FIXTURE in "${INVALID_FIXTURES[@]}"; do
    log_info "Applying invalid fixture: ${FIXTURE}..."
    kubectl apply -f "$SCRIPT_DIR/fixtures/${FIXTURE}" -n simetra

    # Allow watcher to process the invalid config
    sleep 5

    # Verify all pods still running
    if check_pods_ready; then
        log_info "Pods survived ${FIXTURE}"
    else
        log_error "Pods NOT ready after ${FIXTURE}"
        ALL_SURVIVED=0
    fi

    # Collect error log evidence from any pod
    PODS=$(kubectl get pods -n simetra -l app=snmp-collector -o jsonpath='{.items[*].metadata.name}')
    for POD in $PODS; do
        POD_LOGS=$(kubectl logs "$POD" -n simetra --since=15s 2>/dev/null) || true

        # Look for expected error handling messages
        ERROR_LINE=$(echo "$POD_LOGS" | grep -m1 "Failed to parse\|is null -- skipping reload\|skipping reload" 2>/dev/null) || true
        if [ -n "$ERROR_LINE" ]; then
            EVIDENCE="${EVIDENCE}[${FIXTURE}] pod=${POD}: ${ERROR_LINE}; "
            break
        fi
    done

    # Restore before next sub-test
    log_info "Restoring ConfigMaps after ${FIXTURE}..."
    restore_configmaps
    sleep 3
done

if [ "$ALL_SURVIVED" -eq 1 ]; then
    record_pass "$SCENARIO_NAME" "All 4 invalid fixtures applied; pods survived all. ${EVIDENCE}"
else
    record_fail "$SCENARIO_NAME" "Pods crashed after invalid JSON. ${EVIDENCE}"
fi
