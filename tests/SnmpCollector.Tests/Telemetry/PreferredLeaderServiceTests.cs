using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Telemetry;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Tests for <see cref="PreferredLeaderService"/> identity resolution, stub behavior,
/// DI singleton pattern, and <see cref="NullPreferredStampReader"/>.
/// <para>
/// Tests that set PHYSICAL_HOSTNAME restore the original value in a try/finally to
/// prevent cross-test contamination.
/// </para>
/// </summary>
public sealed class PreferredLeaderServiceTests
{
    private static IOptions<LeaseOptions> CreateOptions(string? preferredNode = null) =>
        Options.Create(new LeaseOptions
        {
            Name = "snmp-collector-leader",
            Namespace = "simetra",
            PreferredNode = preferredNode
        });

    // -----------------------------------------------------------------------
    // 1. IsPreferredPod_WhenHostnameMatches_ReturnsTrue
    // -----------------------------------------------------------------------

    [Fact]
    public void IsPreferredPod_WhenHostnameMatches_ReturnsTrue()
    {
        var original = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", "node-1");
            var svc = new PreferredLeaderService(
                CreateOptions("node-1"),
                NullLogger<PreferredLeaderService>.Instance);

            Assert.True(svc.IsPreferredPod);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", original);
        }
    }

    // -----------------------------------------------------------------------
    // 2. IsPreferredPod_WhenHostnameDiffers_ReturnsFalse
    // -----------------------------------------------------------------------

    [Fact]
    public void IsPreferredPod_WhenHostnameDiffers_ReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", "node-2");
            var svc = new PreferredLeaderService(
                CreateOptions("node-1"),
                NullLogger<PreferredLeaderService>.Instance);

            Assert.False(svc.IsPreferredPod);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", original);
        }
    }

    // -----------------------------------------------------------------------
    // 3. IsPreferredPod_WhenPreferredNodeEmpty_ReturnsFalse
    // -----------------------------------------------------------------------

    [Fact]
    public void IsPreferredPod_WhenPreferredNodeEmpty_ReturnsFalse()
    {
        var svc = new PreferredLeaderService(
            CreateOptions(preferredNode: null),
            NullLogger<PreferredLeaderService>.Instance);

        Assert.False(svc.IsPreferredPod);
    }

    // -----------------------------------------------------------------------
    // 4. IsPreferredPod_WhenPhysicalHostnameEmpty_ReturnsFalse
    // -----------------------------------------------------------------------

    [Fact]
    public void IsPreferredPod_WhenPhysicalHostnameEmpty_ReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME");
        try
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", null);
            var svc = new PreferredLeaderService(
                CreateOptions("node-1"),
                NullLogger<PreferredLeaderService>.Instance);

            Assert.False(svc.IsPreferredPod);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", original);
        }
    }

    // -----------------------------------------------------------------------
    // 5. IsPreferredStampFresh_AlwaysReturnsFalse
    // -----------------------------------------------------------------------

    [Fact]
    public void IsPreferredStampFresh_AlwaysReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME");
        try
        {
            // Test with matching hostname to confirm stub returns false even when preferred
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", "node-1");
            var svc = new PreferredLeaderService(
                CreateOptions("node-1"),
                NullLogger<PreferredLeaderService>.Instance);

            Assert.True(svc.IsPreferredPod);    // pod IS preferred
            Assert.False(svc.IsPreferredStampFresh); // but stamp is stub false (Phase 84)
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHYSICAL_HOSTNAME", original);
        }
    }

    // -----------------------------------------------------------------------
    // 6. DiSingleton_K8sPath_ResolvesToSameInstance
    //    Simulates the K8s concrete-first pattern from ServiceCollectionExtensions.
    // -----------------------------------------------------------------------

    [Fact]
    public void DiSingleton_K8sPath_ResolvesToSameInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<LeaseOptions>().Configure(o =>
        {
            o.Name = "snmp-collector-leader";
            o.Namespace = "simetra";
        });

        // Simulates the K8s branch in ServiceCollectionExtensions:
        //   services.AddSingleton<PreferredLeaderService>();
        //   services.AddSingleton<IPreferredStampReader>(sp => sp.GetRequiredService<PreferredLeaderService>());
        services.AddSingleton<PreferredLeaderService>();
        services.AddSingleton<IPreferredStampReader>(sp => sp.GetRequiredService<PreferredLeaderService>());

        using var sp = services.BuildServiceProvider();
        var fromInterface = sp.GetRequiredService<IPreferredStampReader>();
        var fromConcrete = sp.GetRequiredService<PreferredLeaderService>();

        // SC#5 equivalent: both resolutions MUST return the exact same object reference.
        Assert.Same(fromInterface, fromConcrete);
    }

    // -----------------------------------------------------------------------
    // 7. DiSingleton_LocalDev_RegistersNullStampReader
    // -----------------------------------------------------------------------

    [Fact]
    public void DiSingleton_LocalDev_RegistersNullStampReader()
    {
        var services = new ServiceCollection();
        // Simulates the non-K8s branch in ServiceCollectionExtensions:
        services.AddSingleton<IPreferredStampReader, NullPreferredStampReader>();

        using var sp = services.BuildServiceProvider();
        var reader = sp.GetRequiredService<IPreferredStampReader>();

        Assert.IsType<NullPreferredStampReader>(reader);
        Assert.False(reader.IsPreferredStampFresh);
    }

    // -----------------------------------------------------------------------
    // 8. NullPreferredStampReader_IsPreferredStampFresh_ReturnsFalse
    // -----------------------------------------------------------------------

    [Fact]
    public void NullPreferredStampReader_IsPreferredStampFresh_ReturnsFalse()
    {
        var reader = new NullPreferredStampReader();
        Assert.False(reader.IsPreferredStampFresh);
    }
}
