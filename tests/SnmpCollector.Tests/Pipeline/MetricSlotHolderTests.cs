using Lextm.SharpSnmpLib;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class MetricSlotHolderTests
{
    private static MetricSlotHolder CreateHolder(
        string ip = "10.0.0.1",
        int port = 161,
        string metricName = "hrProcessorLoad",
        int intervalSeconds = 30)
        => new(ip, port, metricName, intervalSeconds);

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

        holder.WriteValue(42.5, null, SnmpType.Integer32);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(42.5, slot.Value);
        Assert.Null(slot.StringValue);
        Assert.True(slot.UpdatedAt >= before, "UpdatedAt should be >= time before write");
        Assert.True(slot.UpdatedAt <= DateTimeOffset.UtcNow, "UpdatedAt should be <= now");
    }

    [Fact]
    public void WriteValue_ThenReadSlot_ReturnsWrittenInfoValue()
    {
        var holder = CreateHolder();

        holder.WriteValue(0, "linkUp", SnmpType.Integer32);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(0, slot.Value);
        Assert.Equal("linkUp", slot.StringValue);
    }

    [Fact]
    public void WriteValue_OverwritesPreviousValue()
    {
        var holder = CreateHolder();

        holder.WriteValue(1.0, "first", SnmpType.Integer32);
        holder.WriteValue(2.0, "second", SnmpType.Integer32);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(2.0, slot.Value);
        Assert.Equal("second", slot.StringValue);
    }

    [Fact]
    public void ReadSlot_ReturnsConsistentSnapshot()
    {
        var holder = CreateHolder();
        holder.WriteValue(99.9, "status", SnmpType.Integer32);

        // Read the slot reference once — all fields come from the same immutable record
        var slot = holder.ReadSlot();
        Assert.NotNull(slot);

        // Verify all fields are internally consistent (no torn read possible on a reference type)
        var value = slot.Value;
        var stringValue = slot.StringValue;
        var updatedAt = slot.UpdatedAt;

        Assert.Equal(99.9, value);
        Assert.Equal("status", stringValue);
        Assert.True(updatedAt > DateTimeOffset.MinValue, "UpdatedAt should be a real timestamp");
    }

    [Fact]
    public void Constructor_SetsMetadataProperties()
    {
        var holder = new MetricSlotHolder("192.168.1.1", 162, "sysUpTime", 60);

        Assert.Equal("192.168.1.1", holder.Ip);
        Assert.Equal(162, holder.Port);
        Assert.Equal("sysUpTime", holder.MetricName);
        Assert.Equal(60, holder.IntervalSeconds);
    }

    [Fact]
    public void WriteValue_PreservesTypeCode()
    {
        var holder = CreateHolder();

        holder.WriteValue(12345.0, null, SnmpType.Gauge32);

        var slot = holder.ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(SnmpType.Gauge32, slot.TypeCode);
    }
}
