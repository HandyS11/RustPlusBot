using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Switches.Relaying;

/// <summary>
/// Deletes all of a wiped server's switches: entity ids are permanently invalid after a wipe, so the rows
/// are removed first, then the #switches embeds best-effort. Idempotent: a duplicate wipe event finds no
/// rows and is a no-op.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped switch store.</param>
/// <param name="locator">Resolves the #switches channel id.</param>
/// <param name="poster">Deletes the switch embeds.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class SwitchWipePurger(
    IServiceScopeFactory scopeFactory,
    ISwitchChannelLocator locator,
    ISwitchChannelPoster poster,
    ILogger<SwitchWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by purging the server's switches.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and embeds have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        IReadOnlyList<SmartSwitch> switches;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            switches = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var device in switches)
            {
                await store.RemoveAsync(evt.GuildId, evt.ServerId, device.EntityId, ct).ConfigureAwait(false);
            }
        }

        if (switches.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var device in switches)
            {
                if (device.MessageId is { } messageId)
                {
                    await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
                }
            }
        }

        LogPurged(logger, switches.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} switches after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
