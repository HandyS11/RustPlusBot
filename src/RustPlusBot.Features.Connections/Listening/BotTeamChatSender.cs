using System.Globalization;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Prepends <see cref="BotTeamChat.Prefix"/> and forwards to the raw <see cref="ITeamChatSender"/>.</summary>
/// <param name="inner">The raw team-chat sender.</param>
internal sealed class BotTeamChatSender(ITeamChatSender inner) : IBotTeamChatSender
{
    /// <inheritdoc />
    public Task<TeamChatSendResult> SendAsync(ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken) =>
        inner.SendAsync(guildId,
            serverId,
            string.Create(CultureInfo.InvariantCulture, $"{BotTeamChat.Prefix} {message}"),
            cancellationToken);
}
