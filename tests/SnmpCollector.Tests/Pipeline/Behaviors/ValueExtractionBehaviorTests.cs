using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Behaviors;

public sealed class ValueExtractionBehaviorTests
{
    private static SnmpOidReceived MakeNotification(ISnmpData value, SnmpType typeCode) =>
        new()
        {
            Oid = "1.3.6.1.2.1.25.3.3.1.2",
            AgentIp = IPAddress.Parse("10.0.0.1"),
            Value = value,
            Source = SnmpSource.Poll,
            TypeCode = typeCode,
            DeviceName = "test-device"
        };

    private static ValueExtractionBehavior<SnmpOidReceived, Unit> CreateBehavior() =>
        new();

    [Fact]
    public async Task ExtractsInteger32Value()
    {
        // Integer32 sets ExtractedValue; ExtractedStringValue stays null
        var notification = MakeNotification(new Integer32(42), SnmpType.Integer32);
        var behavior = CreateBehavior();

        await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.Equal(42.0, notification.ExtractedValue);
        Assert.Null(notification.ExtractedStringValue);
    }

    [Fact]
    public async Task ExtractsGauge32Value()
    {
        var notification = MakeNotification(new Gauge32(1000), SnmpType.Gauge32);
        var behavior = CreateBehavior();

        await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.Equal(1000.0, notification.ExtractedValue);
        Assert.Null(notification.ExtractedStringValue);
    }

    [Fact]
    public async Task ExtractsCounter64Value()
    {
        ulong largeNumber = 9_876_543_210UL;
        var notification = MakeNotification(new Counter64(largeNumber), SnmpType.Counter64);
        var behavior = CreateBehavior();

        await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.Equal((double)largeNumber, notification.ExtractedValue);
        Assert.Null(notification.ExtractedStringValue);
    }

    [Fact]
    public async Task ExtractsOctetStringValue()
    {
        // OctetString sets ExtractedValue = 0 and ExtractedStringValue = string representation
        var notification = MakeNotification(new OctetString("router-01"), SnmpType.OctetString);
        var behavior = CreateBehavior();

        await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.Equal(0.0, notification.ExtractedValue);
        Assert.NotNull(notification.ExtractedStringValue);
        Assert.Contains("router-01", notification.ExtractedStringValue);
    }

    [Fact]
    public async Task AlwaysCallsNext()
    {
        // next() must always be called regardless of TypeCode
        var notification = MakeNotification(new Integer32(1), SnmpType.Integer32);
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task PassesThroughNonSnmpOidReceived()
    {
        // Non-SnmpOidReceived notifications must pass through to next() unmodified
        var behavior = new ValueExtractionBehavior<OtherNotification, Unit>();
        var other = new OtherNotification();
        var nextCalled = false;

        await behavior.Handle(other, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    private sealed class OtherNotification : IRequest<Unit> { }
}
