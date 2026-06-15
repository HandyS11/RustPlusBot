using System.Diagnostics.CodeAnalysis;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Discord.Notifications;

/// <summary>Discord-backed <see cref="IUserDmSender"/>.</summary>
/// <param name="client">The socket client.</param>
/// <param name="logger">The logger.</param>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container via IUserDmSender registration.")]
internal sealed partial class DiscordUserDmSender(DiscordSocketClient client, ILogger<DiscordUserDmSender> logger)
    : IUserDmSender
{
    /// <inheritdoc />
    public async Task SendAsync(ulong userId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Discord.Net uses RequestOptions, not CancellationToken, for per-call cancellation, so the token is not forwarded.
            var user = await client.GetUserAsync(userId).ConfigureAwait(false);
            if (user is null)
            {
                LogUserNotFound(logger, userId);
                return;
            }

            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(message).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch is intentional: a closed DM (or any send failure) must not break callers.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDmFailed(logger, ex, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot DM user {UserId}: user not found.")]
    private static partial void LogUserNotFound(ILogger logger, ulong userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to DM user {UserId}.")]
    private static partial void LogDmFailed(ILogger logger, Exception ex, ulong userId);
}
