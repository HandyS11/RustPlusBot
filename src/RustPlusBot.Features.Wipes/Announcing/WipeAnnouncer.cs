using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Wipes.Announcing;

/// <summary>Default <see cref="IWipeAnnouncer"/>: resolves #events, the guild culture and the ping flag, then posts.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="locator">Resolves the #events channel id.</param>
/// <param name="renderer">Builds the announcement embed.</param>
/// <param name="poster">Sends the announcement message.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class WipeAnnouncer(
    IServiceScopeFactory scopeFactory,
    IEventChannelLocator locator,
    WipeEmbedRenderer renderer,
    IWipeChannelPoster poster,
    ILogger<WipeAnnouncer> logger) : IWipeAnnouncer
{
    /// <inheritdoc />
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is not { } cid)
        {
            LogNoEventsChannel(logger, evt.GuildId, evt.ServerId);
            return;
        }

        string culture;
        bool ping;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            culture = await workspace.GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
            ping = await workspace.GetPingEveryoneOnWipeAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        }

        var embed = renderer.Render(evt, culture);
        await poster.PostAsync(cid, ping ? "@everyone" : null, embed, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No #events channel for guild {GuildId} server {ServerId}; skipping wipe announcement.")]
    private static partial void LogNoEventsChannel(ILogger logger, ulong guildId, Guid serverId);
}
