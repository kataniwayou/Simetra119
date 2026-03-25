namespace SnmpCollector.Pipeline;

/// <summary>
/// Tracks consecutive poll failures per device for unreachability detection.
/// Thread-safe: multiple jobs may call simultaneously (different devices).
/// </summary>
public interface IDeviceUnreachabilityTracker
{
    /// <summary>
    /// Record a poll failure for the device. Returns true on the TRANSITION to unreachable
    /// (i.e., the Nth consecutive failure where N == threshold). Returns false otherwise.
    /// </summary>
    bool RecordFailure(string deviceName);

    /// <summary>
    /// Record a poll success for the device. Returns true on TRANSITION from unreachable to
    /// healthy (i.e., device was marked unreachable, now recovered). Returns false if device
    /// was already healthy.
    /// </summary>
    bool RecordSuccess(string deviceName);

    /// <summary>Returns the current consecutive failure count for the device.</summary>
    int GetFailureCount(string deviceName);

    /// <summary>Returns true if the device is currently in the unreachable state.</summary>
    bool IsUnreachable(string deviceName);
}
