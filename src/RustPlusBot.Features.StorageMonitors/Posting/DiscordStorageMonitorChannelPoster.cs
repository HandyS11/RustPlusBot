using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Devices.Posting;

namespace RustPlusBot.Features.StorageMonitors.Posting;

/// <summary>Posts/edits storage monitor embeds in #storagemonitors by message id. Untested integration shim.</summary>
/// <param name="client">The Discord socket client (used directly for raw message deletes).</param>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordStorageMonitorChannelPoster(
    DiscordSocketClient client,
    DiscordChannelMessenger messenger,
    ILogger<DiscordStorageMonitorChannelPoster> logger)
    : DiscordDeviceChannelPoster(client, messenger, logger), IStorageMonitorChannelPoster;
