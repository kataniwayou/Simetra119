using Microsoft.Extensions.Options;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Minimal structural validator for <see cref="DevicesOptions"/>.
/// Per-entry validation (CommunityString format, DNS resolution, duplicate IP+Port detection,
/// OID resolution) is handled by
/// <see cref="SnmpCollector.Services.DeviceWatcherService.ValidateAndBuildDevicesAsync"/>.
/// This validator always returns Success since <see cref="DevicesOptions.Devices"/> defaults
/// to an empty list and the framework guarantees <paramref name="options"/> is non-null.
/// </summary>
public sealed class DevicesOptionsValidator : IValidateOptions<DevicesOptions>
{
    public ValidateOptionsResult Validate(string? name, DevicesOptions options)
    {
        return ValidateOptionsResult.Success;
    }
}
