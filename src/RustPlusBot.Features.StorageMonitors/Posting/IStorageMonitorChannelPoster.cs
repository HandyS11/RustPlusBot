using RustPlusBot.Features.Devices.Posting;

namespace RustPlusBot.Features.StorageMonitors.Posting;

/// <summary>Posts/edits a storage monitor embed in #storagemonitors by message id, self-healing a deleted message.</summary>
internal interface IStorageMonitorChannelPoster : IDeviceChannelPoster
{
    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #storagemonitors channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted (or the failure swallowed).</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
}
