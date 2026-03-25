namespace SnmpCollector.Pipeline;

/// <summary>
/// Static helper for the Simetra.{DeviceName} community string convention.
/// Community strings encode device identity: "Simetra.npb-core-01" identifies device "npb-core-01".
/// </summary>
public static class CommunityStringHelper
{
    private const string CommunityPrefix = "Simetra.";

    /// <summary>
    /// Attempts to extract a device name from a community string following the Simetra.{DeviceName} convention.
    /// </summary>
    /// <param name="community">The community string to parse.</param>
    /// <param name="deviceName">The extracted device name, or empty string if parsing fails.</param>
    /// <returns>True if the community string follows the Simetra.{DeviceName} convention.</returns>
    public static bool TryExtractDeviceName(string community, out string deviceName)
    {
        if (community.StartsWith(CommunityPrefix, StringComparison.Ordinal)
            && community.Length > CommunityPrefix.Length)
        {
            deviceName = community[CommunityPrefix.Length..];
            return true;
        }
        deviceName = string.Empty;
        return false;
    }

    /// <summary>
    /// Derives a community string from a device name using the Simetra.{DeviceName} convention.
    /// </summary>
    /// <param name="deviceName">The device name to encode.</param>
    /// <returns>The derived community string (e.g., "Simetra.npb-core-01").</returns>
    public static string DeriveFromDeviceName(string deviceName)
        => string.Concat(CommunityPrefix, deviceName);
}
