using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Clans.Messages;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Clans.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Clans.State;

/// <summary>
/// Applies one observed clan state change: persists it, diffs it against the previous snapshot,
/// posts the resulting feed lines, and reconciles the workspace on a transition.
/// </summary>
/// <param name="scopeFactory">Opens a scope per call for the scoped store, resolver and reconciler.</param>
/// <param name="locator">Resolves the #claninfo channel.</param>
/// <param name="poster">Posts feed lines.</param>
/// <param name="renderer">Renders a change into a feed line.</param>
/// <param name="capability">Invalidated before a transition reconcile so it sees the fresh row.</param>
/// <param name="clock">Drives the score-post throttle.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ClanStateService(
    IServiceScopeFactory scopeFactory,
    IClanInfoChannelLocator locator,
    IClanFeedPoster poster,
    ClanChangeRenderer renderer,
    ClanCapabilityProvider capability,
    IClock clock,
    ILogger<ClanStateService> logger)
{
    /// <summary>
    /// Minimum spacing between score posts for one server. Score moves on every kill; unthrottled
    /// it would drown every other event in the feed.
    /// </summary>
    private static readonly TimeSpan ScoreThrottle = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<(ulong GuildId, Guid ServerId), DateTimeOffset> _lastScorePost = new();

    /// <summary>Applies one clan state change end to end.</summary>
    /// <param name="evt">The observed change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the change has been applied.</returns>
    public async Task ApplyAsync(ClanStateChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // "Could not ask" must never be mistaken for "no clan": acting on it would clear the stored
        // snapshot and let the reconciler delete the clan channels on a transient socket failure.
        if (evt.Status == ClanProbeStatus.Unavailable)
        {
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            var store = services.GetRequiredService<IClanStore>();

            if (evt.Status == ClanProbeStatus.NoClan)
            {
                await ApplyNoClanAsync(services, store, evt, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (evt.Snapshot is not { } snapshot)
            {
                // HasClan without a payload is a contract violation upstream; there is nothing to apply.
                LogMissingSnapshot(logger, evt.GuildId, evt.ServerId);
                return;
            }

            await ApplyHasClanAsync(services, store, evt, snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Collect(HashSet<ulong> ids, ulong? id)
    {
        if (id is { } value)
        {
            ids.Add(value);
        }
    }

    private async Task ApplyNoClanAsync(
        IServiceProvider services,
        IClanStore store,
        ClanStateChangedEvent evt,
        CancellationToken cancellationToken)
    {
        // Read the outgoing snapshot first: the dissolved line names the clan we are about to forget.
        var previous = await store.GetAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        var cleared = await store.ClearAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        if (!cleared)
        {
            // No row existed, so nothing transitioned — a repeated NoClan probe is not news.
            return;
        }

        // Post BEFORE reconciling: the reconcile is about to delete the channel we are posting into.
        var changes = ClanSnapshotDiffer.Diff(previous, null);
        await PostChangesAsync(services, evt, changes, null, cancellationToken).ConfigureAwait(false);
        await ReconcileAsync(services, evt, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyHasClanAsync(
        IServiceProvider services,
        IClanStore store,
        ClanStateChangedEvent evt,
        ClanSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var previous = await store.GetAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        await store.SaveAsync(evt.GuildId, evt.ServerId, snapshot, cancellationToken).ConfigureAwait(false);

        // Harvest before rendering so the feed lines below can already use the learned names.
        await RecordTeamNamesAsync(services, store, evt, snapshot, cancellationToken).ConfigureAwait(false);

        var changes = ClanSnapshotDiffer.Diff(previous, snapshot);
        await PostChangesAsync(services, evt, changes, snapshot, cancellationToken).ConfigureAwait(false);

        if (previous is null)
        {
            // First detection: none→clan is a transition, so the channels have to be created. Every
            // later snapshot skips this — OnClanChanged fires on any clan edit and reconciling each
            // time would hammer Discord.
            await ReconcileAsync(services, evt, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Populates the SteamId → Name cache from the live team snapshot, the second of the two name
    /// sources (clan chat senders being the first). Without it a fresh install renders every roster
    /// entry as a bare profile link until that member happens to speak in clan chat.
    /// </summary>
    /// <param name="services">The per-call scope's services.</param>
    /// <param name="store">The scoped clan store.</param>
    /// <param name="evt">The change being applied (identifies the server).</param>
    /// <param name="snapshot">The clan snapshot whose roster bounds what is worth recording.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once any learned names have been recorded.</returns>
    private async Task RecordTeamNamesAsync(
        IServiceProvider services,
        IClanStore store,
        ClanStateChangedEvent evt,
        ClanSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = services.GetRequiredService<IRustServerQuery>();
            var team = await query.GetTeamInfoAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (team is null)
            {
                // Disconnected: names stay as they are, which is exactly the pre-existing behaviour.
                return;
            }

            var roster = snapshot.Members.Select(m => m.SteamId).ToHashSet();
            var candidates = team.Members
                .Where(m => roster.Contains(m.SteamId) && !string.IsNullOrWhiteSpace(m.Name))
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            var known = await store
                .GetNamesAsync(evt.GuildId, evt.ServerId, candidates.ConvertAll(m => m.SteamId), cancellationToken)
                .ConfigureAwait(false);

            foreach (var member in candidates.Where(m => !known.ContainsKey(m.SteamId)))
            {
                await store.RecordNameAsync(evt.GuildId, evt.ServerId, member.SteamId, member.Name,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down: the caller's own awaits will observe this too.
            throw;
        }
#pragma warning disable CA1031 // Broad catch: name harvesting is cosmetic and must never block persistence.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogNameHarvestFailed(logger, evt.GuildId, evt.ServerId, ex);
        }
    }

    private async Task ReconcileAsync(
        IServiceProvider services,
        ClanStateChangedEvent evt,
        CancellationToken cancellationToken)
    {
        capability.Invalidate(evt.GuildId, evt.ServerId);
        var reconciler = services.GetRequiredService<IWorkspaceReconciler>();
        await reconciler.ReconcileServerAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
    }

    private async Task PostChangesAsync(
        IServiceProvider services,
        ClanStateChangedEvent evt,
        IReadOnlyList<ClanChange> changes,
        ClanSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is not { } channel)
        {
            // Not provisioned yet (the very first snapshot arrives before the reconcile creates it).
            return;
        }

        var postable = ApplyScoreThrottle(evt, changes);
        if (postable.Count == 0)
        {
            return;
        }

        var workspace = services.GetRequiredService<IWorkspaceStore>();
        var culture = await workspace.GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        var names = await ResolveNamesAsync(services, evt, postable, snapshot, cancellationToken)
            .ConfigureAwait(false);

        foreach (var change in postable)
        {
            if (renderer.Render(change, names, culture) is { } line)
            {
                await poster.PostAsync(channel, line, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task<IReadOnlyDictionary<ulong, string>> ResolveNamesAsync(
        IServiceProvider services,
        ClanStateChangedEvent evt,
        IReadOnlyList<ClanChange> changes,
        ClanSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        // One batched resolve covering every id the lines can mention.
        var ids = new HashSet<ulong>();
        foreach (var change in changes)
        {
            Collect(ids, change.SteamId);
            Collect(ids, change.ActorSteamId);
        }

        if (snapshot is not null)
        {
            foreach (var member in snapshot.Members)
            {
                ids.Add(member.SteamId);
            }
        }

        var resolver = services.GetRequiredService<IClanNameResolver>();
        return resolver.ResolveAsync(evt.GuildId, evt.ServerId, ids, cancellationToken);
    }

    /// <summary>Drops a score change that follows too closely on the last posted one.</summary>
    /// <param name="evt">The change being applied (identifies the server).</param>
    /// <param name="changes">The diffed changes.</param>
    /// <returns>The changes that should be posted.</returns>
    private List<ClanChange> ApplyScoreThrottle(ClanStateChangedEvent evt, IReadOnlyList<ClanChange> changes)
    {
        var key = (evt.GuildId, evt.ServerId);
        var result = new List<ClanChange>(changes.Count);
        foreach (var change in changes)
        {
            if (change.Kind != ClanChangeKind.ScoreChanged)
            {
                result.Add(change);
                continue;
            }

            var now = clock.UtcNow;
            if (_lastScorePost.TryGetValue(key, out var last) && now - last < ScoreThrottle)
            {
                continue;
            }

            _lastScorePost[key] = now;
            result.Add(change);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "A HasClan clan state change for guild {GuildId} server {ServerId} carried no snapshot.")]
    private static partial void LogMissingSnapshot(ILogger logger, ulong guildId, Guid serverId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Harvesting clan member names from the team snapshot for guild {GuildId} server {ServerId} failed.")]
    private static partial void LogNameHarvestFailed(ILogger logger,
        ulong guildId,
        Guid serverId,
        Exception exception);
}
