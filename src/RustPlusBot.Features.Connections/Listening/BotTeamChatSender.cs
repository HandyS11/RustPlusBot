using System.Globalization;
using RustPlusBot.Abstractions.Chat;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Prepends <see cref="BotTeamChat.Prefix"/> and forwards to the raw <see cref="IChatSender"/>.</summary>
/// <param name="inner">The raw chat sender.</param>
internal sealed class BotTeamChatSender(IChatSender inner) : IBotTeamChatSender
{
    /// <inheritdoc />
    public Task<ChatSendResult> SendAsync(ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken) =>
        inner.SendAsync(ChatChannelKind.Team,
            guildId,
            serverId,
            string.Create(CultureInfo.InvariantCulture, $"{BotTeamChat.Prefix} {message}"),
            cancellationToken);
}
