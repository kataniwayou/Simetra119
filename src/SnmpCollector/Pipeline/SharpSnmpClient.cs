using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Production implementation of <see cref="ISnmpClient"/> that delegates to the static
/// <see cref="Messenger.GetAsync"/> method from the SharpSnmpLib library.
/// Registered as a singleton in <see cref="Extensions.ServiceCollectionExtensions.AddSnmpPipeline"/>.
/// </summary>
public sealed class SharpSnmpClient : ISnmpClient
{
    /// <inheritdoc />
    public Task<IList<Variable>> GetAsync(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        IList<Variable> variables,
        CancellationToken ct)
        => Messenger.GetAsync(version, endpoint, community, variables, ct);
}
