using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Pairing;

/// <summary>Turns an <see cref="AlarmPairedEvent"/> into an "Add it?" prompt and, on Accept, a managed alarm.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped alarm/workspace stores.</param>
/// <param name="locator">Resolves the #alarms channel id.</param>
/// <param name="poster">Posts/edits alarm + prompt messages.</param>
/// <param name="renderer">Renders the prompt and alarm embeds.</param>
internal sealed class AlarmPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    IAlarmChannelLocator locator,
    IAlarmChannelPoster poster,
    AlarmEmbedRenderer renderer)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server, ulong Entity), Pending> _pending = new();

    /// <summary>Gets the held default name for a pending pairing, or null.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The alarm entity id.</param>
    /// <returns>The held default name, or null when no pending pairing exists.</returns>
    public string? PendingName(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryGetValue((guildId, serverId, entityId), out var p) ? p.DefaultName : null;

    /// <summary>Handles a paired alarm: ignore if already managed, else post the prompt and hold pending state.</summary>
    /// <param name="evt">The paired-alarm event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the prompt has been posted (or the alarm was ignored).</returns>
    public async Task HandlePairedAsync(AlarmPairedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            if (await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (channelId is not { } channel)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken).ConfigureAwait(false);
            var defaultName = $"Alarm {evt.EntityId}";
            var (embed, components) = renderer.RenderPrompt(evt.ServerId, evt.EntityId, defaultName, culture);
            var messageId = await poster.EnsureAsync(channel, null, embed, components, cancellationToken)
                .ConfigureAwait(false);
            _pending[(evt.GuildId, evt.ServerId, evt.EntityId)] = new Pending(defaultName, messageId);
        }
    }

    /// <summary>Accepts a pending pairing: persist + replace prompt with the alarm embed. Race-guarded.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The alarm entity id.</param>
    /// <param name="acceptingUserId">The id of the user who accepted the pairing.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when the alarm was persisted; false when it was already managed (race).</returns>
    public async Task<bool> TryAcceptAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong acceptingUserId,
        CancellationToken cancellationToken)
    {
        _pending.TryGetValue((guildId, serverId, entityId), out var pending);
        var name = pending?.DefaultName ?? $"Alarm {entityId}";

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            if (await store.ExistsAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false))
            {
                _pending.TryRemove((guildId, serverId, entityId), out _);
                return false;
            }

            var added = await store.AddAsync(guildId, serverId, entityId, name, acceptingUserId, cancellationToken)
                .ConfigureAwait(false);

            var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (channelId is { } channel)
            {
                var culture = await GetCultureAsync(scope.ServiceProvider, guildId, cancellationToken).ConfigureAwait(false);

                // The alarm is freshly accepted; unreachable is false (it just paired).
                // The supervisor's prime path will re-render real state shortly.
                var (embed, components) = renderer.RenderAlarm(added, unreachable: false, culture);
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
    /// <param name="entityId">The alarm entity id.</param>
    /// <returns>True when a pending pairing was removed; false when none was held.</returns>
    public bool TryDismiss(ulong guildId, Guid serverId, ulong entityId) =>
        _pending.TryRemove((guildId, serverId, entityId), out _);

    private static async Task<string> GetCultureAsync(IServiceProvider provider, ulong guildId, CancellationToken ct)
    {
        var store = provider.GetRequiredService<IWorkspaceStore>();
        return await store.GetCultureAsync(guildId, ct).ConfigureAwait(false);
    }

    private sealed record Pending(string DefaultName, ulong? MessageId);
}
