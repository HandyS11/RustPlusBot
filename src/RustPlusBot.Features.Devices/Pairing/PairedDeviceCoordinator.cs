using System.Collections.Concurrent;
using Discord;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Devices.Posting;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Devices.Pairing;

/// <summary>
/// Turns a paired-device event into an "Add it?" prompt and, on Accept, a managed device. Pending
/// pairings live in memory keyed by (guild, server, entity) until the user accepts or dismisses
/// them; only accepted pairings reach the store. Derived types supply the device-specific naming,
/// rendering, channel lookup and store calls.
/// </summary>
/// <typeparam name="TPairedEvent">The device feature's paired-device event.</typeparam>
/// <typeparam name="TEntity">The persisted device the store returns when the pairing is accepted.</typeparam>
/// <param name="scopeFactory">Opens scopes for the scoped device/workspace stores.</param>
/// <param name="locator">Resolves the device type's channel id.</param>
/// <param name="poster">Posts/edits the device + prompt messages in the device type's channel.</param>
public abstract class PairedDeviceCoordinator<TPairedEvent, TEntity>(
    IServiceScopeFactory scopeFactory,
    IDeviceChannelLocator locator,
    IDeviceChannelPoster poster)
    where TPairedEvent : class, IPairedDeviceEvent
    where TEntity : class
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server, ulong Entity), Pending> _pending = new();

    /// <summary>Gets the held default name for a pending pairing, or null.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The device entity id.</param>
    /// <returns>The held default name, or null when no pending pairing exists.</returns>
    public string? PendingName(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryGetValue((guildId, serverId, entityId), out var p) ? p.DefaultName : null;

    /// <summary>Handles a paired device: ignore if already managed, else post the prompt and hold pending state.</summary>
    /// <param name="evt">The paired-device event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the prompt has been posted (or the device was ignored).</returns>
    public async Task HandlePairedAsync(TPairedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (await IsManagedAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
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
        var defaultName = DefaultName(evt.EntityId);
        var (embed, components) = RenderPrompt(evt.ServerId, evt.EntityId, defaultName, culture);
        var messageId = await poster.EnsureAsync(channel, null, embed, components, cancellationToken)
            .ConfigureAwait(false);
        _pending[(evt.GuildId, evt.ServerId, evt.EntityId)] = new Pending(defaultName, messageId);
    }

    /// <summary>Accepts a pending pairing: persist + replace prompt with the device embed. Race-guarded.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The device entity id.</param>
    /// <param name="acceptingUserId">The id of the user who accepted the pairing.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when the device was persisted; false when it was already managed (race).</returns>
    public async Task<bool> TryAcceptAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong acceptingUserId,
        CancellationToken cancellationToken)
    {
        if (await IsManagedAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false))
        {
            _pending.TryRemove((guildId, serverId, entityId), out _);
            return false;
        }

        _pending.TryGetValue((guildId, serverId, entityId), out var pending);
        var name = pending?.DefaultName ?? DefaultName(entityId);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var added = await AddAsync(
                    scope.ServiceProvider, guildId, serverId, entityId, name, acceptingUserId, cancellationToken)
                .ConfigureAwait(false);

            var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken)
                .ConfigureAwait(false);
            if (channelId is { } channel)
            {
                var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);

                // The device is freshly accepted; its live state is unknown until the next prime/trigger
                // arrives moments later, so RenderAccepted shows whatever the just-persisted row says.
                // The supervisor's prime path republishes the real state shortly after.
                var (embed, components) = RenderAccepted(added, culture);
                var newMessageId = await poster
                    .EnsureAsync(channel, pending?.MessageId, embed, components, cancellationToken)
                    .ConfigureAwait(false);
                if (newMessageId is { } mid)
                {
                    await SetMessageIdAsync(
                            scope.ServiceProvider, guildId, serverId, entityId, mid, cancellationToken)
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
    /// <param name="entityId">The device entity id.</param>
    /// <returns>True when a pending pairing was removed; false when none was held.</returns>
    public bool TryDismiss(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryRemove((guildId, serverId, entityId), out _);

    /// <summary>Builds the generated display name a device gets before the user renames it.</summary>
    /// <param name="entityId">The device entity id.</param>
    /// <returns>The default display name, e.g. <c>Switch 42</c>.</returns>
    protected abstract string DefaultName(ulong entityId);

    /// <summary>Renders the transient "New device detected — Add it?" prompt.</summary>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The device entity id.</param>
    /// <param name="defaultName">The generated default name.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The prompt embed and its Accept/Dismiss row.</returns>
    protected abstract (Embed Embed, MessageComponent Components) RenderPrompt(
        Guid serverId,
        ulong entityId,
        string defaultName,
        string culture);

    /// <summary>Renders the embed that replaces the prompt once the pairing has been accepted.</summary>
    /// <param name="entity">The device row the store just persisted.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The device embed and its control row.</returns>
    protected abstract (Embed Embed, MessageComponent Components) RenderAccepted(TEntity entity, string culture);

    /// <summary>Asks the device store whether this identity is already managed.</summary>
    /// <param name="services">The scoped provider to resolve the device store from.</param>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The device entity id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when a managed device with this identity exists.</returns>
    protected abstract Task<bool> ExistsAsync(
        IServiceProvider services,
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken);

    /// <summary>Persists the accepted device and returns the stored row.</summary>
    /// <param name="services">The scoped provider to resolve the device store from.</param>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The device entity id.</param>
    /// <param name="name">The display name to persist.</param>
    /// <param name="pairedByUserId">The id of the user who accepted the pairing.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The persisted device.</returns>
    protected abstract Task<TEntity> AddAsync(
        IServiceProvider services,
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken);

    /// <summary>Records the Discord message id the device embed now lives at.</summary>
    /// <param name="services">The scoped provider to resolve the device store from.</param>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The device entity id.</param>
    /// <param name="messageId">The Discord embed message id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the message id has been persisted.</returns>
    protected abstract Task SetMessageIdAsync(
        IServiceProvider services,
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong messageId,
        CancellationToken cancellationToken);

    private async Task<bool> IsManagedAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await ExistsAsync(scope.ServiceProvider, guildId, serverId, entityId, ct).ConfigureAwait(false);
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
