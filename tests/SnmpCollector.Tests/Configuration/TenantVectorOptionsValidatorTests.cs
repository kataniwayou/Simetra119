using SnmpCollector.Configuration;
using SnmpCollector.Configuration.Validators;
using Xunit;

namespace SnmpCollector.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="TenantVectorOptionsValidator"/>.
/// Confirms unconditional acceptance (always returns Success).
/// </summary>
public class TenantVectorOptionsValidatorTests
{
    private readonly TenantVectorOptionsValidator _validator;

    public TenantVectorOptionsValidatorTests()
    {
        _validator = new TenantVectorOptionsValidator();
    }

    private static TenantVectorOptions ValidOptions() => new()
    {
        Tenants =
        [
            new TenantOptions
            {
                Priority = 1,
                Metrics =
                [
                    new MetricSlotOptions
                    {
                        Ip = "10.0.0.1",
                        Port = 161,
                        MetricName = "obp_link_state_L1"
                    }
                ]
            }
        ]
    };

    // ========== Positive cases ==========

    [Fact]
    public void Validate_ValidConfig_ReturnsSuccess()
    {
        var result = _validator.Validate(null, ValidOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleTenants_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants.Add(new TenantOptions
        {
            Priority = 2,
            Metrics =
            [
                new MetricSlotOptions
                {
                    Ip = "10.0.0.2",
                    Port = 161,
                    MetricName = "npb_cpu_util",
                }
            ]
        });

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyMetricsArray_ReturnsSuccess()
    {
        var options = new TenantVectorOptions
        {
            Tenants =
            [
                new TenantOptions
                {
                    Priority = 1,
                    Metrics = []
                }
            ]
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_CrossTenantOverlap_ReturnsSuccess()
    {
        var options = new TenantVectorOptions
        {
            Tenants =
            [
                new TenantOptions
                {
                    Priority = 1,
                    Metrics =
                    [
                        new MetricSlotOptions
                        {
                            Ip = "10.0.0.1",
                            Port = 161,
                            MetricName = "obp_link_state_L1"
                        }
                    ]
                },
                new TenantOptions
                {
                    Priority = 2,
                    Metrics =
                    [
                        new MetricSlotOptions
                        {
                            Ip = "10.0.0.1",
                            Port = 161,
                            MetricName = "obp_link_state_L1"
                        }
                    ]
                }
            ]
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyTenantsList_ReturnsSuccess()
    {
        var options = new TenantVectorOptions { Tenants = [] };
        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NegativePriority_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants[0].Priority = -100;

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    // ========== Unconditional acceptance cases ==========

    [Fact]
    public void Validate_EmptyIp_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Ip = "";

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_InvalidPort_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Port = 0;

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyMetricName_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].MetricName = "";

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }
}
