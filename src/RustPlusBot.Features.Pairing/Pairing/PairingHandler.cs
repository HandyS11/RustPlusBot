using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Default <see cref="IPairingHandler"/>: resolve-or-create the server, upsert the credential, announce new servers.</summary>
/// <param name="servers">Server resolve-or-create.</param>
/// <param name="credentials">The credential pool store.</param>
/// <param name="eventBus">Publishes the real ServerRegisteredEvent on new-server creation.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PairingHandler(
    IServerService servers,
    ICredentialStore credentials,
    IEventBus eventBus,
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

        var (server, created) = await servers.ResolveOrCreateByEndpointAsync(
                guildId, ownerUserId, notification.ServerName, notification.Ip, notification.Port, cancellationToken)
            .ConfigureAwait(false);

        await servers.SetFacepunchServerIdAsync(server.Id, notification.FacepunchServerId, cancellationToken)
            .ConfigureAwait(false);

        await credentials.UpsertFromPairingAsync(
            new StoreCredentialRequest(guildId, server.Id, ownerUserId, notification.PlayerId,
                notification.PlayerToken),
            markActive: created,
            cancellationToken).ConfigureAwait(false);

        if (created)
        {
            await eventBus.PublishAsync(new ServerRegisteredEvent(guildId, server.Id), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleEntityAsync(
        ulong guildId, PairingNotification notification, CancellationToken cancellationToken)
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

        await eventBus.PublishAsync(
                new SwitchPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Dropping entity pairing for unknown Facepunch server {FacepunchServerId} (no matching server).")]
    private static partial void LogUnknownEntityServer(ILogger logger, Guid facepunchServerId);
}
