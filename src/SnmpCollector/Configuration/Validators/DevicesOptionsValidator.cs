using System.Net;
using Microsoft.Extensions.Options;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="DevicesOptions"/> at startup.
/// Manually walks the entire nested object graph (Devices -> MetricPolls -> Oids)
/// because ValidateDataAnnotations does not validate nested objects.
/// </summary>
public sealed class DevicesOptionsValidator : IValidateOptions<DevicesOptions>
{
    public ValidateOptionsResult Validate(string? name, DevicesOptions options)
    {
        var failures = new List<string>();

        // Empty Devices[] is valid -- no devices to poll
        for (var i = 0; i < options.Devices.Count; i++)
        {
            var device = options.Devices[i];
            ValidateDevice(device, i, failures);
        }

        // Duplicate detection across all devices
        ValidateNoDuplicates(options.Devices, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateDevice(DeviceOptions device, int index, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
        {
            failures.Add($"Devices[{index}].Name is required");
        }

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            failures.Add($"Devices[{index}].IpAddress is required");
        }
        else if (!IPAddress.TryParse(device.IpAddress, out _))
        {
            failures.Add($"Devices[{index}].IpAddress '{device.IpAddress}' is not a valid IP address");
        }

        for (var j = 0; j < device.MetricPolls.Count; j++)
        {
            var poll = device.MetricPolls[j];
            ValidateMetricPoll(poll, index, j, failures);
        }
    }

    private static void ValidateNoDuplicates(List<DeviceOptions> devices, List<string> failures)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];

            if (!string.IsNullOrWhiteSpace(device.Name) && !seenNames.Add(device.Name))
            {
                failures.Add($"Devices[{i}].Name '{device.Name}' is a duplicate — device names must be unique");
            }

            if (!string.IsNullOrWhiteSpace(device.IpAddress) && !seenIps.Add(device.IpAddress))
            {
                failures.Add($"Devices[{i}].IpAddress '{device.IpAddress}' is a duplicate — device IPs must be unique");
            }
        }
    }

    private static void ValidateMetricPoll(MetricPollOptions poll, int deviceIndex, int pollIndex, List<string> failures)
    {
        var prefix = $"Devices[{deviceIndex}].MetricPolls[{pollIndex}]";

        if (poll.IntervalSeconds <= 0)
        {
            failures.Add($"{prefix}.IntervalSeconds must be greater than 0");
        }

        if (poll.Oids.Count == 0)
        {
            failures.Add($"{prefix}.Oids must contain at least one entry");
        }
    }
}
