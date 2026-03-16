using Lextm.SharpSnmpLib;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class SharpSnmpClientSetTests
{
    [Fact]
    public void ParseSnmpData_Integer32_ReturnsInteger32Instance()
    {
        var result = SharpSnmpClient.ParseSnmpData("42", "Integer32");
        Assert.IsType<Integer32>(result);
    }

    [Fact]
    public void ParseSnmpData_OctetString_ReturnsOctetStringInstance()
    {
        var result = SharpSnmpClient.ParseSnmpData("hello", "OctetString");
        Assert.IsType<OctetString>(result);
    }

    [Fact]
    public void ParseSnmpData_IpAddress_ReturnsIPInstance()
    {
        var result = SharpSnmpClient.ParseSnmpData("10.0.0.1", "IpAddress");
        Assert.IsType<IP>(result);
    }

    [Fact]
    public void ParseSnmpData_Integer32_CorrectValue()
    {
        var result = SharpSnmpClient.ParseSnmpData("42", "Integer32");
        var integer = Assert.IsType<Integer32>(result);
        Assert.Equal(42, integer.ToInt32());
    }

    [Fact]
    public void ParseSnmpData_NegativeInteger_Works()
    {
        var result = SharpSnmpClient.ParseSnmpData("-1", "Integer32");
        var integer = Assert.IsType<Integer32>(result);
        Assert.Equal(-1, integer.ToInt32());
    }

    [Fact]
    public void ParseSnmpData_UnknownType_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => SharpSnmpClient.ParseSnmpData("value", "Unknown"));
        Assert.Contains("Unsupported ValueType: Unknown", ex.Message);
    }

    [Theory]
    [InlineData("99", "Integer32")]
    [InlineData("test", "OctetString")]
    [InlineData("192.168.1.1", "IpAddress")]
    public void ParseSnmpData_ValidTypes_ReturnsNotNull(string value, string valueType)
    {
        var result = SharpSnmpClient.ParseSnmpData(value, valueType);
        Assert.NotNull(result);
    }
}
