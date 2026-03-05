using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Telemetry;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Tests for <see cref="AlwaysLeaderElection"/> behavior (SC#4: always-leader for local dev)
/// and the DI singleton pattern that ensures <see cref="ILeaderElection"/> and
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> resolve to the same object (SC#5).
///
/// No <c>[Collection(NonParallelCollection.Name)]</c> needed: these tests do not use
/// MeterListener or any OTel SDK global state.
/// </summary>
public sealed class LeaderElectionTests
{
    // -----------------------------------------------------------------------
    // 1. AlwaysLeaderElection.IsLeader returns true
    // -----------------------------------------------------------------------

    [Fact]
    public void AlwaysLeaderElection_IsLeader_ReturnsTrue()
    {
        var election = new AlwaysLeaderElection();
        Assert.True(election.IsLeader);
    }

    // -----------------------------------------------------------------------
    // 2. AlwaysLeaderElection.CurrentRole returns "leader"
    // -----------------------------------------------------------------------

    [Fact]
    public void AlwaysLeaderElection_CurrentRole_ReturnsLeader()
    {
        var election = new AlwaysLeaderElection();
        Assert.Equal("leader", election.CurrentRole);
    }

    // -----------------------------------------------------------------------
    // 3. DI singleton - local dev path registers AlwaysLeaderElection for ILeaderElection
    // -----------------------------------------------------------------------

    [Fact]
    public void DiSingleton_LocalDev_RegistersAlwaysLeaderElection()
    {
        var services = new ServiceCollection();
        // Simulates the non-K8s branch in ServiceCollectionExtensions.AddSnmpConfiguration.
        services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();

        using var sp = services.BuildServiceProvider();
        var election = sp.GetRequiredService<ILeaderElection>();

        Assert.IsType<AlwaysLeaderElection>(election);
        Assert.True(election.IsLeader);
    }

    // -----------------------------------------------------------------------
    // 4. DI singleton - concrete-first pattern resolves both interfaces to same instance (SC#5)
    //    Simulates the K8s path without requiring real K8s infrastructure by using
    //    AlwaysLeaderElection as a stand-in for K8sLeaseElection.
    // -----------------------------------------------------------------------

    [Fact]
    public void DiSingleton_K8sPath_ResolvesToSameInstance()
    {
        var services = new ServiceCollection();

        // CORRECT pattern (as used in ServiceCollectionExtensions for K8s path):
        // Register concrete type FIRST, then forward both interface registrations to same instance.
        // Production code uses:
        //   services.AddSingleton<K8sLeaseElection>();
        //   services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
        //   services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());
        services.AddSingleton<AlwaysLeaderElection>();
        services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<AlwaysLeaderElection>());

        using var sp = services.BuildServiceProvider();
        var fromInterface = sp.GetRequiredService<ILeaderElection>();
        var fromConcrete = sp.GetRequiredService<AlwaysLeaderElection>();

        // SC#5: both resolutions MUST return the exact same object reference.
        Assert.Same(fromInterface, fromConcrete);
    }

    // -----------------------------------------------------------------------
    // 5. Anti-pattern proof: naive double-registration creates two distinct instances
    //    This test documents WHY the concrete-first pattern is required.
    //    If DI creates two instances, ILeaderElection consumers read stale state from the
    //    wrong instance while IHostedService updates a different one.
    // -----------------------------------------------------------------------

    [Fact]
    public void DiSingleton_NaiveRegistration_CreatesTwoInstances()
    {
        var services = new ServiceCollection();

        // ANTI-PATTERN: two separate AddSingleton calls create two independent singletons.
        services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
        services.AddSingleton<AlwaysLeaderElection>();

        using var sp = services.BuildServiceProvider();
        var fromInterface = sp.GetRequiredService<ILeaderElection>();
        var fromConcrete = sp.GetRequiredService<AlwaysLeaderElection>();

        // Deliberately asserts NOT same -- proves the anti-pattern is broken.
        // This is the failure mode the concrete-first pattern was designed to prevent.
        Assert.NotSame(fromInterface, fromConcrete);
    }
}
