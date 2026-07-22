using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Resolves the #clanchat channel id for a (guild, server) and supports the reverse direction
/// (channel id → guild + server) used by the Discord message handler.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class ClanChatChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerClanChat), IChatChannelLocator
{
    /// <inheritdoc />
    public ChatChannelKind Kind => ChatChannelKind.Clan;

    /// <inheritdoc />
    public async Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId,
        CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in Entries)
        {
            if (value == channelId)
            {
                return key;
            }
        }

        return null;
    }
}
