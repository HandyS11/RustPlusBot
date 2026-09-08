using Discord;

namespace RustPlusBot.Features.Devices.Posting;

/// <summary>
/// Posts or edits a smart-device embed in that device type's Discord channel, self-healing a
/// message a moderator deleted. One implementation per device feature (#switches, #storagemonitors…).
/// </summary>
public interface IDeviceChannelPoster
{
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The device channel id.</param>
    /// <param name="messageId">The known embed message id, or null to post fresh.</param>
    /// <param name="embed">The embed to show.</param>
    /// <param name="components">The control row.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The (possibly new) message id, or null on failure.</returns>
    Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        CancellationToken cancellationToken);
}
