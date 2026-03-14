using System.Net;
using Microsoft.Extensions.Options;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="DevicesOptions"/> at startup.
/// Manually walks the entire nested object graph (Devices -> Polls -> MetricNames)
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
        if (string.IsNullOrWhiteSpace(device.CommunityString))
        {
            failures.Add($"Devices[{index}].CommunityString is required");
        }
        else if (!CommunityStringHelper.TryExtractDeviceName(device.CommunityString, out _))
        {
            failures.Add(
                $"Devices[{index}].CommunityString '{device.CommunityString}' " +
                "does not follow Simetra.{DeviceName} convention");
        }

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            failures.Add($"Devices[{index}].IpAddress is required");
        }
        else if (!IPAddress.TryParse(device.IpAddress, out _))
        {
            failures.Add($"Devices[{index}].IpAddress '{device.IpAddress}' is not a valid IP address");
        }

        if (device.Port < 1 || device.Port > 65535)
        {
            failures.Add($"Devices[{index}].Port must be between 1 and 65535");
        }

        for (var j = 0; j < device.Polls.Count; j++)
        {
            var poll = device.Polls[j];
            ValidatePoll(poll, index, j, failures);
        }
    }

    private static void ValidateNoDuplicates(List<DeviceOptions> devices, List<string> failures)
    {
        var seenIpPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];

            if (!string.IsNullOrWhiteSpace(device.IpAddress))
            {
                var ipPort = $"{device.IpAddress}:{device.Port}";
                if (!seenIpPorts.Add(ipPort))
                {
                    failures.Add($"Devices[{i}] IP+Port '{ipPort}' is a duplicate -- each device must have a unique IP+Port combination");
                }
            }
        }
    }

    private static void ValidatePoll(PollOptions poll, int deviceIndex, int pollIndex, List<string> failures)
    {
        var prefix = $"Devices[{deviceIndex}].Polls[{pollIndex}]";

        if (poll.IntervalSeconds <= 0)
        {
            failures.Add($"{prefix}.IntervalSeconds must be greater than 0");
        }

        if (poll.MetricNames.Count == 0)
        {
            failures.Add($"{prefix}.MetricNames must contain at least one entry");
        }

        if (poll.TimeoutMultiplier is < 0.1 or > 0.9)
        {
            failures.Add($"{prefix}.TimeoutMultiplier must be between 0.1 and 0.9 (got {poll.TimeoutMultiplier})");
        }
    }
}
