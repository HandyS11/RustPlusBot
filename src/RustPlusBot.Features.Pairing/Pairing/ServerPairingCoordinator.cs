using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Posting;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Turns a new-server pairing into an "Add it?" prompt in #setup and, on Accept, a registered server.
/// Pending state is in-memory only (mirrors the entity coordinators): lost on restart, re-pair to re-prompt.
/// A repeat detection re-ensures the prompt (self-healing a deleted message) rather than stranding pending.
/// Detections are serialized through a gate so concurrent pairings for one endpoint post a single prompt.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped server/credential/workspace stores.</param>
/// <param name="locator">Resolves the guild's #setup channel id.</param>
/// <param name="poster">Posts/edits the prompt message.</param>
/// <param name="renderer">Renders the prompt and confirmation embeds.</param>
/// <param name="ownerNotifier">DMs the owner when no #setup channel exists.</param>
/// <param name="eventBus">Publishes ServerRegisteredEvent on accepted creation.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ServerPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    ISetupChannelLocator locator,
    ISetupChannelPoster poster,
    ServerPairingPromptRenderer renderer,
    IOwnerNotifier ownerNotifier,
    IEventBus eventBus,
    ILogger<ServerPairingCoordinator> logger) : IServerPairingCoordinator, IDisposable
{
    private readonly SemaphoreSlim _detectGate = new(1, 1);
    private readonly ConcurrentDictionary<(ulong Guild, string Ip, int Port), Pending> _pending = new();

    /// <inheritdoc />
    public void Dispose() => _detectGate.Dispose();

    /// <inheritdoc />
    public async Task HandleDetectedAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Serialize detections: without the gate, two concurrent pairings for the same endpoint could
        // both see "not pending" and post duplicate prompts (check-then-await-then-set race).
        await _detectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = (guildId, notification.Ip, notification.Port);
            _pending.TryGetValue(key, out var existing);

            var channelId = await locator.GetChannelIdAsync(guildId, cancellationToken).ConfigureAwait(false);
            if (channelId is not { } channel)
            {
                // No #setup to prompt in — drop any stale pending so a later /setup + re-pair starts clean.
                _pending.TryRemove(key, out _);
                LogSetupChannelMissing(logger, guildId);
                await ownerNotifier.NotifySetupChannelMissingAsync(guildId, ownerUserId, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            var (embed, components) =
                renderer.RenderPrompt(notification.ServerName, notification.Ip, notification.Port, culture);

            // Pass the known message id (null on first detection) so an edit self-heals a deleted prompt and a
            // repeat "Pair" press re-ensures rather than posting a duplicate. The latest token always wins.
            var messageId = await poster.EnsureAsync(channel, existing?.MessageId, embed, components, cancellationToken)
                .ConfigureAwait(false);
            if (messageId is not { } mid)
            {
                if (existing is null)
                {
                    // First prompt failed to post — leave nothing pending so a later re-pair retries.
                    LogPromptPostFailed(logger, guildId);
                    return;
                }

                // Transient re-ensure failure with a prompt already shown — keep the live prompt/pending and
                // just refresh the token so a later re-pair can retry the heal.
                _pending[key] = new Pending(ownerUserId, notification, existing.MessageId);
                return;
            }

            _pending[key] = new Pending(ownerUserId, notification, mid);
        }
        finally
        {
            _detectGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServerPairingAcceptOutcome> TryAcceptAsync(
        ulong guildId,
        string ip,
        int port,
        CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove((guildId, ip, port), out var pending))
        {
            return ServerPairingAcceptOutcome.Expired;
        }

        var notification = pending.Notification;
        RustServer server;
        bool created;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            // Replays the pre-confirmation registration path verbatim (see PairingHandler history).
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var credentials = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
            (server, created) = await servers.ResolveOrCreateByEndpointAsync(
                    guildId, pending.OwnerUserId, notification.ServerName, notification.Ip, notification.Port,
                    cancellationToken)
                .ConfigureAwait(false);

            // Only backfill a real Facepunch GUID. Persisting Guid.Empty would make every server that paired
            // without one share the same id, breaking GUID-based entity-pairing attribution.
            if (notification.FacepunchServerId != Guid.Empty)
            {
                await servers.SetFacepunchServerIdAsync(server.Id, notification.FacepunchServerId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await credentials.UpsertFromPairingAsync(
                new StoreCredentialRequest(guildId, server.Id, pending.OwnerUserId, notification.PlayerId,
                    notification.PlayerToken),
                markActive: created,
                cancellationToken).ConfigureAwait(false);
        }

        if (created)
        {
            await eventBus.PublishAsync(new ServerRegisteredEvent(guildId, server.Id), cancellationToken)
                .ConfigureAwait(false);
        }

        var channelId = await locator.GetChannelIdAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (channelId is { } channel)
        {
            var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            var (embed, components) = renderer.RenderAdded(notification.ServerName, culture);
            _ = await poster.EnsureAsync(channel, pending.MessageId, embed, components, cancellationToken)
                .ConfigureAwait(false);
        }

        return created ? ServerPairingAcceptOutcome.Added : ServerPairingAcceptOutcome.AlreadyAdded;
    }

    /// <inheritdoc />
    public bool TryDismiss(ulong guildId, string ip, int port) => _pending.TryRemove((guildId, ip, port), out _);

    /// <summary>Gets whether a pairing is pending for the endpoint (test seam).</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <returns>True when a prompt is pending.</returns>
    public bool HasPending(ulong guildId, string ip, int port) => _pending.ContainsKey((guildId, ip, port));

    private async Task<string> GetCultureAsync(ulong guildId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            return await store.GetCultureAsync(guildId, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Server pairing for guild {GuildId} dropped: no #setup channel to prompt in.")]
    private static partial void LogSetupChannelMissing(ILogger logger, ulong guildId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Server-pairing prompt for guild {GuildId} could not be posted to #setup; " +
                  "pairing dropped — pair again in-game to retry.")]
    private static partial void LogPromptPostFailed(ILogger logger, ulong guildId);

    private sealed record Pending(ulong OwnerUserId, PairingNotification Notification, ulong? MessageId);
}
