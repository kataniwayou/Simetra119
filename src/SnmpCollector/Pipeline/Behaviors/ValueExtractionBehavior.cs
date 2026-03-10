using Lextm.SharpSnmpLib;
using MediatR;

namespace SnmpCollector.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that extracts the numeric or string value from an SNMP OID message once,
/// setting <see cref="SnmpOidReceived.ExtractedValue"/> and <see cref="SnmpOidReceived.ExtractedStringValue"/>
/// so downstream consumers (OtelMetricHandler, TenantVectorFanOutBehavior) read pre-extracted values
/// without repeating the TypeCode switch.
///
/// Runs after OidResolutionBehavior (MetricName must be set) and before TenantVectorFanOutBehavior.
/// Other notification types pass through to next() unmodified.
/// </summary>
public sealed class ValueExtractionBehavior<TNotification, TResponse>
    : IPipelineBehavior<TNotification, TResponse>
    where TNotification : notnull
{
    public async Task<TResponse> Handle(
        TNotification notification,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (notification is SnmpOidReceived msg)
        {
            switch (msg.TypeCode)
            {
                case SnmpType.Integer32:
                    msg.ExtractedValue = ((Integer32)msg.Value).ToInt32();
                    break;
                case SnmpType.Gauge32:
                    msg.ExtractedValue = ((Gauge32)msg.Value).ToUInt32();
                    break;
                case SnmpType.TimeTicks:
                    msg.ExtractedValue = ((TimeTicks)msg.Value).ToUInt32();
                    break;
                case SnmpType.Counter32:
                    msg.ExtractedValue = ((Counter32)msg.Value).ToUInt32();
                    break;
                case SnmpType.Counter64:
                    msg.ExtractedValue = (double)((Counter64)msg.Value).ToUInt64();
                    break;
                case SnmpType.OctetString:
                case SnmpType.IPAddress:
                case SnmpType.ObjectIdentifier:
                    msg.ExtractedValue = 0;
                    msg.ExtractedStringValue = msg.Value.ToString();
                    break;
                // default: leave ExtractedValue = 0, ExtractedStringValue = null
            }
        }

        return await next();
    }
}
