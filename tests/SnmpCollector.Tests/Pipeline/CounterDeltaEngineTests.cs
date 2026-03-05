using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Pipeline;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

/// <summary>
/// Unit tests for <see cref="CounterDeltaEngine"/> covering all five delta computation paths:
/// first-poll baseline skip, normal increment, Counter32 wrap-around, sysUpTime-based reboot
/// detection, and Counter64 decrease reboot detection.
/// </summary>
public sealed class CounterDeltaEngineTests
{
    private readonly TestSnmpMetricFactory _factory;
    private readonly CounterDeltaEngine _engine;

    private const string DefaultOid    = "1.3.6.1.2.1.2.2.1.10.1";
    private const string DefaultAgent  = "192.168.1.1";
    private const string DefaultSource = "poll";
    private const string DefaultMetric = "ifInOctets";

    public CounterDeltaEngineTests()
    {
        _factory = new TestSnmpMetricFactory();
        _engine  = new CounterDeltaEngine(_factory, NullLogger<CounterDeltaEngine>.Instance);
    }

    // -------------------------------------------------------------------------
    // SC #4: First poll — stores baseline, produces no CounterRecords entry
    // -------------------------------------------------------------------------

    [Fact]
    public void FirstPoll_ReturnsFalse_AndNoCounterRecord()
    {
        // Act
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1000, sysUpTimeCentiseconds: 50000);

        // Assert
        Assert.False(result);
        Assert.Empty(_factory.CounterRecords);
    }

    // -------------------------------------------------------------------------
    // SC #1: Normal increment 1000 -> 1500 produces delta 500
    // -------------------------------------------------------------------------

    [Fact]
    public void NormalIncrement_ProducesDelta500()
    {
        // Arrange — establish baseline
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1000, sysUpTimeCentiseconds: 50000);

        // Act — advance counter
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1500, sysUpTimeCentiseconds: 60000);

        // Assert
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(500.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // SC #2: Counter32 wrap-around: 4,294,967,200 -> 100 => delta 196
    // -------------------------------------------------------------------------

    [Fact]
    public void Counter32Wrap_ProducesCorrectDelta()
    {
        // Arrange — baseline near the Counter32 ceiling
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 4_294_967_200UL, sysUpTimeCentiseconds: 50000);

        // Act — counter wraps; sysUpTime is still increasing (no reboot)
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 100, sysUpTimeCentiseconds: 60000);

        // Assert: (2^32 - 4_294_967_200) + 100 = 96 + 100 = 196
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(196.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // SC #3: sysUpTime decrease triggers reboot path — current value used as delta
    // -------------------------------------------------------------------------

    [Fact]
    public void RebootDetectedViaSysUpTimeDecrease_UsesCurrentValueAsDelta()
    {
        // Arrange — baseline with high uptime
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 5000, sysUpTimeCentiseconds: 90000);

        // Act — sysUpTime went from 90000 to 1000 (reboot), counter reset to 300
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 300, sysUpTimeCentiseconds: 1000);

        // Assert — delta is the current value, not a wrap computation
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(300.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // Counter64 decrease always treated as reboot (current value as delta)
    // -------------------------------------------------------------------------

    [Fact]
    public void Counter64Decrease_TreatedAsReboot_UsesCurrentValueAsDelta()
    {
        // Arrange
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter64, 5000, sysUpTimeCentiseconds: 50000);

        // Act — 64-bit counter decreased with sysUpTime still rising (no uptime reboot signal)
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter64, 200, sysUpTimeCentiseconds: 60000);

        // Assert — conservative: current value 200 used as delta
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(200.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // SC #5: Two agents reporting same OID maintain independent delta state
    // -------------------------------------------------------------------------

    [Fact]
    public void TwoAgents_SameOid_MaintainIndependentState()
    {
        const string agentA = "10.0.0.1";
        const string agentB = "10.0.0.2";

        // Establish baselines for both agents
        _engine.RecordDelta(DefaultOid, agentA, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1000, sysUpTimeCentiseconds: 50000);
        _engine.RecordDelta(DefaultOid, agentB, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 5000, sysUpTimeCentiseconds: 50000);

        // Second polls
        _engine.RecordDelta(DefaultOid, agentA, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1100, sysUpTimeCentiseconds: 60000);
        _engine.RecordDelta(DefaultOid, agentB, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 5050, sysUpTimeCentiseconds: 60000);

        // Assert — two records, one per agent, with independent deltas
        Assert.Equal(2, _factory.CounterRecords.Count);

        var recordA = _factory.CounterRecords.Single(r => r.Agent == agentA);
        var recordB = _factory.CounterRecords.Single(r => r.Agent == agentB);

        Assert.Equal(100.0, recordA.Delta);
        Assert.Equal(50.0,  recordB.Delta);
    }

    // -------------------------------------------------------------------------
    // sysUpTime null + current < previous => conservative reboot (delta = current)
    // -------------------------------------------------------------------------

    [Fact]
    public void NullSysUpTime_CounterDecreased_TreatedConservativelyAsReboot()
    {
        // Arrange — baseline with no sysUpTime available
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter64, 5000, sysUpTimeCentiseconds: null);

        // Act — value decreased, still no sysUpTime
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter64, 300, sysUpTimeCentiseconds: null);

        // Assert — treated as reboot; delta = current value 300
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(300.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // Counter32 exact boundary: 4,294,967,295 -> 0 => delta 1
    // -------------------------------------------------------------------------

    [Fact]
    public void Counter32ExactBoundary_BaselineAtMax_WrapProducesDelta1()
    {
        // Arrange — baseline is 2^32 - 1 (maximum Counter32)
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 4_294_967_295UL, sysUpTimeCentiseconds: 50000);

        // Act — counter wraps to 0; sysUpTime increases (no reboot)
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 0, sysUpTimeCentiseconds: 60000);

        // Assert: (2^32 - 4_294_967_295) + 0 = 1 + 0 = 1
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(1.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // Same value (zero delta): baseline=1000, current=1000 => delta 0.0
    // -------------------------------------------------------------------------

    [Fact]
    public void ZeroDelta_SameValueBothPolls_RecordsDeltaZero()
    {
        // Arrange
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1000, sysUpTimeCentiseconds: 50000);

        // Act — value unchanged
        var result = _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1000, sysUpTimeCentiseconds: 60000);

        // Assert — delta is exactly 0.0 (non-negative guarantee satisfied)
        Assert.True(result);
        Assert.Single(_factory.CounterRecords);
        Assert.Equal(0.0, _factory.CounterRecords[0].Delta);
    }

    // -------------------------------------------------------------------------
    // Label pass-through: metricName, oid, agent, source propagated to RecordCounter
    // -------------------------------------------------------------------------

    [Fact]
    public void LabelPassThrough_AllFieldsPropagatedToCounterRecord()
    {
        const string oid      = "1.3.6.1.2.1.2.2.1.10.5";
        const string agent    = "172.16.0.99";
        const string source   = "trap";
        const string metric   = "ifInOctetsPort5";

        // Establish baseline
        _engine.RecordDelta(oid, agent, source, metric, SnmpType.Counter32, 1000, 50000);

        // Second poll
        _engine.RecordDelta(oid, agent, source, metric, SnmpType.Counter32, 1200, 60000);

        // Assert all labels forwarded to factory
        Assert.Single(_factory.CounterRecords);
        var rec = _factory.CounterRecords[0];
        Assert.Equal(metric, rec.MetricName);
        Assert.Equal(oid,    rec.Oid);
        Assert.Equal(agent,  rec.Agent);
        Assert.Equal(source, rec.Source);
        Assert.Equal(200.0,  rec.Delta);
    }

    // -------------------------------------------------------------------------
    // All deltas are non-negative (clamp safety check)
    // -------------------------------------------------------------------------

    [Fact]
    public void AllEmittedDeltas_AreNonNegative()
    {
        // Arrange — run several scenarios that could produce a delta
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1000, 50000);
        _engine.RecordDelta(DefaultOid, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 1500, 60000);  // normal +500

        const string oid2 = "1.3.6.1.2.1.99.0";
        _engine.RecordDelta(oid2, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 5000, 50000);
        _engine.RecordDelta(oid2, DefaultAgent, DefaultSource, DefaultMetric,
            SnmpType.Counter32, 200, 45000);   // reboot path (sysUpTime decreased)

        // Assert
        Assert.All(_factory.CounterRecords, rec => Assert.True(rec.Delta >= 0.0,
            $"Expected non-negative delta but got {rec.Delta} for oid={rec.Oid}"));
    }
}
