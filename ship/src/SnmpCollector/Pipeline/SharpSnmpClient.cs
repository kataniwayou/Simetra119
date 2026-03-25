using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Production implementation of <see cref="ISnmpClient"/> that delegates to the static
/// <see cref="Messenger"/> methods from the SharpSnmpLib library.
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

    /// <inheritdoc />
    public Task<IList<Variable>> SetAsync(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        Variable variable,
        CancellationToken ct)
        => Messenger.SetAsync(version, endpoint, community, new List<Variable> { variable }, ct);

    /// <summary>
    /// Converts a string value and type name into the corresponding SharpSnmpLib <see cref="ISnmpData"/> instance.
    /// Supported types: Integer32, OctetString, IpAddress.
    /// </summary>
    public static ISnmpData ParseSnmpData(string value, string valueType) => valueType switch
    {
        "Integer32" => new Integer32(int.Parse(value)),
        "OctetString" => new OctetString(value),
        "IpAddress" => new IP(value),
        _ => throw new ArgumentException($"Unsupported ValueType: {valueType}", nameof(valueType))
    };
}
