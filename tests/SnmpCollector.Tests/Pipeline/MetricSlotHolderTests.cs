using Lextm.SharpSnmpLib;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using Xunit;


namespace SnmpCollector.Tests.Pipeline;

public sealed class MetricSlotHolderTests
{
    private static MetricSlotHolder CreateHolder(
        string ip = "10.0.0.1",
        int port = 161,
        string metricName = "hrProcessorLoad",
        int intervalSeconds = 30,
        string role = "Evaluate")
        => new(ip, port, metricName, intervalSeconds, role);

    [Fact]
    public void ReadSlot_BeforeAnyWrite_ReturnsNull()
    {
        var holder = CreateHolder();
        Assert.Null(holder.ReadSlot());
    }

    [Fact]
    public void WriteValue_ThenReadSlot_ReturnsWrittenGaugeValue()
    {
        var holder = CreateHolder();
        var before = DateTimeOffset.UtcNow;

        holder.WriteValue(42.5, null, SnmpType.Integer32, SnmpSource.Poll);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(42.5, slot.Value);
        Assert.Null(slot.StringValue);
        Assert.True(slot.Timestamp >= before, "Timestamp should be >= time before write");
        Assert.True(slot.Timestamp <= DateTimeOffset.UtcNow, "Timestamp should be <= now");
    }

    [Fact]
    public void WriteValue_ThenReadSlot_ReturnsWrittenInfoValue()
    {
        var holder = CreateHolder();

        holder.WriteValue(0, "linkUp", SnmpType.Integer32, SnmpSource.Poll);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(0, slot.Value);
        Assert.Equal("linkUp", slot.StringValue);
    }

    [Fact]
    public void WriteValue_OverwritesPreviousValue()
    {
        var holder = CreateHolder();

        holder.WriteValue(1.0, "first", SnmpType.Integer32, SnmpSource.Poll);
        holder.WriteValue(2.0, "second", SnmpType.Integer32, SnmpSource.Poll);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(2.0, slot.Value);
        Assert.Equal("second", slot.StringValue);
    }

    [Fact]
    public void ReadSlot_ReturnsConsistentSnapshot()
    {
        var holder = CreateHolder();
        holder.WriteValue(99.9, "status", SnmpType.Integer32, SnmpSource.Poll);

        // Read the slot reference once — all fields come from the same immutable record
        var slot = holder.ReadSlot();
        Assert.NotNull(slot);

        // Verify all fields are internally consistent (no torn read possible on a reference type)
        var value = slot.Value;
        var stringValue = slot.StringValue;
        var timestamp = slot.Timestamp;

        Assert.Equal(99.9, value);
        Assert.Equal("status", stringValue);
        Assert.True(timestamp > DateTimeOffset.MinValue, "Timestamp should be a real timestamp");
    }

    [Fact]
    public void Constructor_SetsMetadataProperties()
    {
        var holder = new MetricSlotHolder("192.168.1.1", 162, "sysUpTime", 60, "Evaluate");

        Assert.Equal("192.168.1.1", holder.Ip);
        Assert.Equal(162, holder.Port);
        Assert.Equal("sysUpTime", holder.MetricName);
        Assert.Equal(60, holder.IntervalSeconds);
    }

    [Fact]
    public void WriteValue_PreservesTypeCode()
    {
        var holder = CreateHolder();

        holder.WriteValue(12345.0, null, SnmpType.Gauge32, SnmpSource.Poll);

        Assert.Equal(SnmpType.Gauge32, holder.TypeCode);
    }

    [Fact]
    public void ReadSeries_BeforeAnyWrite_ReturnsEmpty()
    {
        var holder = CreateHolder();
        Assert.True(holder.ReadSeries().IsEmpty);
    }

    [Fact]
    public void WriteValue_TimeSeriesSize3_AccumulatesSamples()
    {
        var holder = new MetricSlotHolder("10.0.0.1", 161, "hrProcessorLoad", 30, "Evaluate", timeSeriesSize: 3);
        holder.WriteValue(1.0, null, SnmpType.Integer32, SnmpSource.Poll);
        holder.WriteValue(2.0, null, SnmpType.Integer32, SnmpSource.Poll);
        holder.WriteValue(3.0, null, SnmpType.Integer32, SnmpSource.Poll);
        var series = holder.ReadSeries();
        Assert.Equal(3, series.Length);
        Assert.Equal(1.0, series[0].Value);
        Assert.Equal(3.0, series[2].Value);
    }

    [Fact]
    public void WriteValue_ExceedsTimeSeriesSize_EvictsOldest()
    {
        var holder = new MetricSlotHolder("10.0.0.1", 161, "hrProcessorLoad", 30, "Evaluate", timeSeriesSize: 2);
        holder.WriteValue(1.0, null, SnmpType.Integer32, SnmpSource.Poll);
        holder.WriteValue(2.0, null, SnmpType.Integer32, SnmpSource.Poll);
        holder.WriteValue(3.0, null, SnmpType.Integer32, SnmpSource.Poll);
        var series = holder.ReadSeries();
        Assert.Equal(2, series.Length);
        Assert.Equal(2.0, series[0].Value);
        Assert.Equal(3.0, series[1].Value);
        // ReadSlot returns latest
        Assert.Equal(3.0, holder.ReadSlot()!.Value);
    }

    [Fact]
    public void WriteValue_PromotesTypeCodeAndSourceToHolder()
    {
        var holder = CreateHolder();
        holder.WriteValue(1.0, null, SnmpType.Gauge32, SnmpSource.Poll);
        Assert.Equal(SnmpType.Gauge32, holder.TypeCode);
        Assert.Equal(SnmpSource.Poll, holder.Source);
        // Overwrite with different type
        holder.WriteValue(2.0, null, SnmpType.Counter32, SnmpSource.Trap);
        Assert.Equal(SnmpType.Counter32, holder.TypeCode);
        Assert.Equal(SnmpSource.Trap, holder.Source);
    }

    [Fact]
    public void CopyFrom_CopiesSeriesAndMetadata()
    {
        var old = new MetricSlotHolder("10.0.0.1", 161, "test", 30, "Evaluate", timeSeriesSize: 3);
        old.WriteValue(1.0, "a", SnmpType.Gauge32, SnmpSource.Poll);
        old.WriteValue(2.0, "b", SnmpType.Gauge32, SnmpSource.Poll);

        var fresh = new MetricSlotHolder("10.0.0.1", 161, "test", 30, "Evaluate", timeSeriesSize: 3);
        fresh.CopyFrom(old);

        Assert.Equal(2, fresh.ReadSeries().Length);
        Assert.Equal(2.0, fresh.ReadSlot()!.Value);
        Assert.Equal(SnmpType.Gauge32, fresh.TypeCode);
        Assert.Equal(SnmpSource.Poll, fresh.Source);
    }

    [Fact]
    public void CopyFrom_TruncatesWhenNewHolderHasSmallerSize()
    {
        var old = new MetricSlotHolder("10.0.0.1", 161, "test", 30, "Evaluate", timeSeriesSize: 5);
        old.WriteValue(1.0, null, SnmpType.Integer32, SnmpSource.Poll);
        old.WriteValue(2.0, null, SnmpType.Integer32, SnmpSource.Poll);
        old.WriteValue(3.0, null, SnmpType.Integer32, SnmpSource.Poll);

        var fresh = new MetricSlotHolder("10.0.0.1", 161, "test", 30, "Evaluate", timeSeriesSize: 2);
        fresh.CopyFrom(old);

        var series = fresh.ReadSeries();
        Assert.Equal(2, series.Length);
        Assert.Equal(2.0, series[0].Value);  // keeps latest 2
        Assert.Equal(3.0, series[1].Value);
    }

    [Fact]
    public void Constructor_StoresThreshold()
    {
        var threshold = new ThresholdOptions { Min = 10.0, Max = 90.0 };
        var holder = new MetricSlotHolder("10.0.0.1", 161, "m", 30, "Evaluate", threshold: threshold);

        Assert.NotNull(holder.Threshold);
        Assert.Equal(10.0, holder.Threshold.Min);
        Assert.Equal(90.0, holder.Threshold.Max);
    }

    [Fact]
    public void Constructor_NullThreshold_DefaultsToNull()
    {
        var holder = new MetricSlotHolder("10.0.0.1", 161, "m", 30, "Evaluate");
        Assert.Null(holder.Threshold);
    }
}
