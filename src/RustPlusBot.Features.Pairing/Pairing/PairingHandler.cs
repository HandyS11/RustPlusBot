using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Default <see cref="IPairingHandler"/>: known servers get a silent credential upsert; new servers
/// are routed to the #setup confirmation prompt; entity pairings fan out to their feature events.</summary>
/// <param name="servers">Server lookup/backfill.</param>
/// <param name="credentials">The credential pool store.</param>
/// <param name="eventBus">Publishes the entity paired events.</param>
/// <param name="serverPairings">Prompts for confirmation before a new server is registered.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PairingHandler(
    IServerService servers,
    ICredentialStore credentials,
    IEventBus eventBus,
    IServerPairingCoordinator serverPairings,
    ILogger<PairingHandler> logger) : IPairingHandler
{
    /// <inheritdoc />
    public async Task HandleAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Kind == PairingKind.Entity)
        {
            await HandleEntityAsync(guildId, notification, cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = await servers.GetByEndpointAsync(guildId, notification.Ip, notification.Port, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            // New server: nothing is persisted until the user accepts the #setup prompt.
            LogNewServerDetected(logger, notification.ServerName, notification.Ip, notification.Port, guildId);
            await serverPairings.HandleDetectedAsync(guildId, ownerUserId, notification, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        LogKnownServerCredentialUpsert(logger, existing.Id, notification.Ip, notification.Port);

        // Only backfill a real Facepunch GUID. Persisting Guid.Empty would make every server that paired
        // without one share the same id, breaking GUID-based entity-pairing attribution.
        if (notification.FacepunchServerId != Guid.Empty)
        {
            await servers.SetFacepunchServerIdAsync(existing.Id, notification.FacepunchServerId, cancellationToken)
                .ConfigureAwait(false);
        }

        await credentials.UpsertFromPairingAsync(
            new StoreCredentialRequest(guildId, existing.Id, ownerUserId, notification.PlayerId,
                notification.PlayerToken),
            markActive: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleEntityAsync(
        ulong guildId,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        var server = await servers
            .GetByFacepunchServerIdAsync(guildId, notification.FacepunchServerId, cancellationToken)
            .ConfigureAwait(false);
        if (server is null)
        {
            // Never create a server from an entity pairing; an unknown Facepunch server is logged and dropped.
            LogUnknownEntityServer(logger, notification.FacepunchServerId);
            return;
        }

        switch (notification.EntityKind)
        {
            case RustPlusBot.Domain.Entities.PairedEntityKind.SmartSwitch:
                await eventBus
                    .PublishAsync(new SwitchPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case RustPlusBot.Domain.Entities.PairedEntityKind.SmartAlarm:
                await eventBus
                    .PublishAsync(new AlarmPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case RustPlusBot.Domain.Entities.PairedEntityKind.StorageMonitor:
                await eventBus
                    .PublishAsync(new StorageMonitorPairedEvent(guildId, server.Id, notification.EntityId),
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                LogUnroutedEntityKind(logger, notification.EntityKind);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Dropping entity pairing for unknown Facepunch server {FacepunchServerId} (no matching server).")]
    private static partial void LogUnknownEntityServer(ILogger logger, Guid facepunchServerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropping entity pairing of unrouted kind {Kind}.")]
    private static partial void
        LogUnroutedEntityKind(ILogger logger, RustPlusBot.Domain.Entities.PairedEntityKind kind);

    [LoggerMessage(Level = LogLevel.Information,
        Message =
            "Pairing detected a new server '{ServerName}' ({Ip}:{Port}) in guild {GuildId}; prompting in #setup.")]
    private static partial void LogNewServerDetected(ILogger logger,
        string serverName,
        string ip,
        int port,
        ulong guildId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Pairing matched known server {ServerId} ({Ip}:{Port}); upserting its credential.")]
    private static partial void LogKnownServerCredentialUpsert(ILogger logger, Guid serverId, string ip, int port);
}
