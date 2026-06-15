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
        if (notification.Kind != PairingKind.Server)
        {
            LogIgnoringKind(logger, notification.Kind);
            return;
        }

        var (server, created) = await servers.ResolveOrCreateByEndpointAsync(
                guildId, ownerUserId, notification.ServerName, notification.Ip, notification.Port, cancellationToken)
            .ConfigureAwait(false);

        await credentials.UpsertFromPairingAsync(
            new StoreCredentialRequest(guildId, server.Id, ownerUserId, notification.PlayerId,
                notification.PlayerToken),
            cancellationToken).ConfigureAwait(false);

        if (created)
        {
            await eventBus.PublishAsync(new ServerRegisteredEvent(guildId, server.Id), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Ignoring {Kind} pairing notification (deferred to a later subsystem).")]
    private static partial void LogIgnoringKind(ILogger logger, PairingKind kind);
}
