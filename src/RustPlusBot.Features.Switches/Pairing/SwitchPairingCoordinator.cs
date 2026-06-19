using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Switches.Pairing;

/// <summary>Turns a <see cref="SwitchPairedEvent"/> into an "Add it?" prompt and, on Accept, a managed switch.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped switch/workspace stores.</param>
/// <param name="locator">Resolves the #switches channel id.</param>
/// <param name="poster">Posts/edits switch + prompt messages.</param>
/// <param name="renderer">Renders the prompt and switch embeds.</param>
internal sealed class SwitchPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    ISwitchChannelLocator locator,
    ISwitchChannelPoster poster,
    SwitchEmbedRenderer renderer)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server, ulong Entity), Pending> _pending = new();

    /// <summary>Gets the held default name for a pending pairing, or null.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The switch entity id.</param>
    /// <returns>The held default name, or null when no pending pairing exists.</returns>
    public string? PendingName(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryGetValue((guildId, serverId, entityId), out var p) ? p.DefaultName : null;

    /// <summary>Handles a paired switch: ignore if already managed, else post the prompt and hold pending state.</summary>
    /// <param name="evt">The paired-switch event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the prompt has been posted (or the switch was ignored).</returns>
    public async Task HandlePairedAsync(SwitchPairedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (await ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is not { } channel)
        {
            return;
        }

        var culture = await GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        var defaultName = $"Switch {evt.EntityId}";
        var (embed, components) = renderer.RenderPrompt(evt.ServerId, evt.EntityId, defaultName, culture);
        var messageId = await poster.EnsureAsync(channel, null, embed, components, cancellationToken)
            .ConfigureAwait(false);
        _pending[(evt.GuildId, evt.ServerId, evt.EntityId)] = new Pending(defaultName, messageId);
    }

    /// <summary>Accepts a pending pairing: persist + replace prompt with the switch embed. Race-guarded.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The switch entity id.</param>
    /// <param name="acceptingUserId">The id of the user who accepted the pairing.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when the switch was persisted; false when it was already managed (race).</returns>
    public async Task<bool> TryAcceptAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong acceptingUserId,
        CancellationToken cancellationToken)
    {
        if (await ExistsAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false))
        {
            _pending.TryRemove((guildId, serverId, entityId), out _);
            return false;
        }

        _pending.TryGetValue((guildId, serverId, entityId), out var pending);
        var name = pending?.DefaultName ?? $"Switch {entityId}";

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            var added = await store.AddAsync(guildId, serverId, entityId, name, acceptingUserId, cancellationToken)
                .ConfigureAwait(false);

            var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (channelId is { } channel)
            {
                var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);

                // The switch is freshly accepted; state is unknown until the next prime/trigger, so render the
                // persisted LastIsActive (defaults false). The supervisor's prime path republishes real state shortly.
                var (embed, components) = renderer.RenderSwitch(added, isActive: added.LastIsActive, culture);
                var newMessageId = await poster
                    .EnsureAsync(channel, pending?.MessageId, embed, components, cancellationToken)
                    .ConfigureAwait(false);
                if (newMessageId is { } mid)
                {
                    await store.SetMessageIdAsync(guildId, serverId, entityId, mid, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        _pending.TryRemove((guildId, serverId, entityId), out _);
        return true;
    }

    /// <summary>Drops a pending pairing; returns whether one was present.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The switch entity id.</param>
    /// <returns>True when a pending pairing was removed; false when none was held.</returns>
    public bool TryDismiss(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryRemove((guildId, serverId, entityId), out _);

    private async Task<bool> ExistsAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            return await store.ExistsAsync(guildId, serverId, entityId, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> GetCultureAsync(ulong guildId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            return await store.GetCultureAsync(guildId, ct).ConfigureAwait(false);
        }
    }

    private sealed record Pending(string DefaultName, ulong? MessageId);
}
