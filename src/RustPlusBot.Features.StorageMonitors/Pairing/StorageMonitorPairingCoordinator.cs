using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.StorageMonitors.Pairing;

/// <summary>Turns a <see cref="StorageMonitorPairedEvent"/> into an "Add it?" prompt and, on Accept, a managed storage monitor.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped storage monitor/workspace stores.</param>
/// <param name="locator">Resolves the #storagemonitors channel id.</param>
/// <param name="poster">Posts/edits storage monitor + prompt messages.</param>
/// <param name="renderer">Renders the prompt and storage monitor embeds.</param>
internal sealed class StorageMonitorPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    IStorageMonitorChannelLocator locator,
    IStorageMonitorChannelPoster poster,
    StorageMonitorEmbedRenderer renderer)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server, ulong Entity), Pending> _pending = new();

    /// <summary>Gets the held default name for a pending pairing, or null.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The storage monitor entity id.</param>
    /// <returns>The held default name, or null when no pending pairing exists.</returns>
    public string? PendingName(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryGetValue((guildId, serverId, entityId), out var p) ? p.DefaultName : null;

    /// <summary>Handles a paired storage monitor: ignore if already managed, else post the prompt and hold pending state.</summary>
    /// <param name="evt">The paired-storage-monitor event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the prompt has been posted (or the storage monitor was ignored).</returns>
    public async Task HandlePairedAsync(StorageMonitorPairedEvent evt, CancellationToken cancellationToken)
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
        var defaultName = $"Storage Monitor {evt.EntityId}";
        var (embed, components) = renderer.RenderPrompt(evt.ServerId, evt.EntityId, defaultName, culture);
        var messageId = await poster.EnsureAsync(channel, null, embed, components, cancellationToken)
            .ConfigureAwait(false);
        _pending[(evt.GuildId, evt.ServerId, evt.EntityId)] = new Pending(defaultName, messageId);
    }

    /// <summary>Accepts a pending pairing: persist + replace prompt with the storage monitor embed. Race-guarded.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The storage monitor entity id.</param>
    /// <param name="acceptingUserId">The id of the user who accepted the pairing.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when the storage monitor was persisted; false when it was already managed (race).</returns>
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
        var name = pending?.DefaultName ?? $"Storage Monitor {entityId}";

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            var added = await store.AddAsync(guildId, serverId, entityId, name, acceptingUserId, cancellationToken)
                .ConfigureAwait(false);

            var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (channelId is { } channel)
            {
                var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);

                // The storage monitor is freshly accepted; contents are unknown until the next prime/trigger arrives
                // moments later. Render with contents: null (unreachable). The supervisor's prime path republishes
                // real contents shortly (same pattern as switches).
                var (embed, components) = renderer.RenderMonitor(added, contents: null, culture);
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
    /// <param name="entityId">The storage monitor entity id.</param>
    /// <returns>True when a pending pairing was removed; false when none was held.</returns>
    public bool TryDismiss(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryRemove((guildId, serverId, entityId), out _);

    private async Task<bool> ExistsAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
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
