using System.Threading.Channels;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Channel interface for dispatching SNMP SET commands from SnapshotJob to CommandWorkerService.
/// <para>
/// Unlike <see cref="ITrapChannel"/>, this interface exposes only <see cref="Writer"/> and
/// <see cref="Reader"/> -- there is no Complete() or WaitForDrainAsync(). On cancellation,
/// CommandWorkerService stops immediately without draining: SET commands are idempotent and
/// will be re-evaluated on the next SnapshotJob cycle.
/// </para>
/// </summary>
public interface ICommandChannel
{
    /// <summary>
    /// Channel writer used by SnapshotJob to enqueue <see cref="CommandRequest"/> items.
    /// Callers use TryWrite (non-blocking); false return indicates channel overflow (DropWrite).
    /// </summary>
    ChannelWriter<CommandRequest> Writer { get; }

    /// <summary>
    /// Channel reader used by CommandWorkerService to drain <see cref="CommandRequest"/> items.
    /// </summary>
    ChannelReader<CommandRequest> Reader { get; }
}
