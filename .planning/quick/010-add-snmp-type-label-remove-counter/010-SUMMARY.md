# Quick Task 010: Add snmp_type label, verify no dropped traps, verify no counter instrument

**One-liner:** Added snmp_type label (8 fixed enum values) to snmp_gauge and snmp_info, verified trap pipeline has no silent drops, confirmed snmp_counter instrument fully removed.

## Task 1: Add snmp_type label to all SNMP metrics

**Commit:** 4e7a503

### Changes

| File | Change |
|------|--------|
| `src/SnmpCollector/Telemetry/ISnmpMetricFactory.cs` | Added `string snmpType` parameter to RecordGauge and RecordInfo |
| `src/SnmpCollector/Telemetry/SnmpMetricFactory.cs` | Added `{ "snmp_type", snmpType }` tag to both TagList blocks |
| `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs` | Split info cases into individual arms; each arm passes lowercase type string |
| `src/SnmpCollector/Pipeline/CardinalityAuditService.cs` | Updated label taxonomy doc comment and log message to include snmp_type |
| `tests/SnmpCollector.Tests/Helpers/TestSnmpMetricFactory.cs` | Added SnmpType field to GaugeRecords/InfoRecords tuples |
| `tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs` | Updated CapturingSnmpMetricFactory, truncation test call, label assertions |
| `tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs` | Updated ThrowingSnmpMetricFactory and label assertions |
| `tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs` | Updated MeterListener tag count assertions (gauge: 5->6, info: 6->7) |

### Label values

| SnmpType | snmp_type string |
|----------|-----------------|
| Integer32 | `integer32` |
| Gauge32 | `gauge32` |
| TimeTicks | `timeticks` |
| Counter32 | `counter32` |
| Counter64 | `counter64` |
| OctetString | `octetstring` |
| IPAddress | `ipaddress` |
| ObjectIdentifier | `objectidentifier` |

### Cardinality impact

snmp_type has 8 fixed enum values -- bounded, no cardinality risk. The label taxonomy in CardinalityAuditService now documents this.

### Test results

125/125 tests pass.

## Task 2: Verify no traps dropped (verification only)

### Findings

The trap pipeline has **no silent drops** in normal flow:

1. **Normal path:** SnmpTrapListenerService -> DeviceChannelManager.GetWriter -> TryWrite -> ChannelConsumerService.ReadAllAsync -> ISender.Send. All varbinds flow through without loss.

2. **Backpressure drops (visible):** BoundedChannelFullMode.DropOldest only activates when channel reaches capacity. The `itemDropped` callback increments `snmp.trap.dropped` counter per device and logs Warning every 100 drops. This is instrumented, not silent.

3. **Intentional rejections (visible):**
   - Unknown device IP: logged at Warning, increments `snmp.trap.unknown_device`
   - Community string auth failure: logged at Warning, increments `snmp.trap.auth_failed`
   - Malformed packets: logged at Warning

4. **Consumer error handling:** Exceptions in ChannelConsumerService.ConsumeDeviceAsync are caught and logged at Warning; the consumer loop continues to the next envelope. No data loss beyond the failed varbind.

**Verdict:** No silent drops exist. All rejection/drop paths have both logging and OTel counter instrumentation.

## Task 3: Verify no snmp_counter instrument (verification only)

### Findings

1. **No `snmp_counter` string** exists anywhere in production code (grep confirmed zero matches).
2. **No `RecordCounter` method** exists on ISnmpMetricFactory or any implementation.
3. **CounterDeltaEngine.cs is deleted** from `src/SnmpCollector/Pipeline/`.
4. **Counter32 and Counter64** raw values are recorded as gauges via `RecordGauge` in OtelMetricHandler (Prometheus applies `rate()`/`increase()` at query time).
5. **SnmpMetricFactory** creates only two instruments: `snmp_gauge` (Gauge<double>) and `snmp_info` (Gauge<double> with value label).

**Verdict:** The snmp_counter instrument is fully removed. Only snmp_gauge and snmp_info exist as business metric instruments.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated SnmpMetricFactoryTests.cs**

- **Found during:** Task 1
- **Issue:** `SnmpMetricFactoryTests.cs` (not mentioned in the plan) also calls RecordGauge/RecordInfo directly and asserts tag counts. Would fail to compile without update.
- **Fix:** Updated all three test methods with new snmpType parameter and corrected tag count assertions (gauge: 5->6, info: 6->7).
- **Files modified:** `tests/SnmpCollector.Tests/Telemetry/SnmpMetricFactoryTests.cs`
- **Commit:** 4e7a503

**2. [Rule 2 - Missing Critical] Split OctetString/IPAddress/ObjectIdentifier into separate switch arms**

- **Found during:** Task 1
- **Issue:** The original code had OctetString, IPAddress, and ObjectIdentifier as fallthrough cases sharing one RecordInfo call. Each needs a distinct snmp_type string, so they must be separate arms.
- **Fix:** Split into three individual case blocks, each passing its own type string.
- **Files modified:** `src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs`
- **Commit:** 4e7a503
