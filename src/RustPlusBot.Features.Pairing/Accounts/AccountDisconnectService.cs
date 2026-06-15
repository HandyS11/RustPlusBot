using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Accounts;

/// <summary>Default <see cref="IAccountDisconnectService"/>.</summary>
/// <param name="supervisor">Stops the user's FCM listener.</param>
/// <param name="registrations">The FCM registration store.</param>
/// <param name="credentials">The player-credential pool store.</param>
/// <param name="servers">Resolves server names for the preview.</param>
/// <param name="eventBus">Publishes ServerCredentialsChangedEvent per affected server.</param>
internal sealed class AccountDisconnectService(
    IPairingSupervisor supervisor,
    IFcmRegistrationStore registrations,
    ICredentialStore credentials,
    IServerService servers,
    IEventBus eventBus) : IAccountDisconnectService
{
    /// <inheritdoc />
    public async Task<AccountDisconnectPreview> PreviewAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var registration = await registrations.GetAsync(guildId, ownerUserId, cancellationToken).ConfigureAwait(false);
        var serverIds = await credentials.ListServerIdsForOwnerAsync(guildId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);

        var names = new List<string>(serverIds.Count);
        foreach (var serverId in serverIds)
        {
            var server = await servers.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (server is not null)
            {
                names.Add(server.Name);
            }
        }

        var isConnected = (registration is not null && registration.Status != FcmRegistrationStatus.Disabled)
                          || serverIds.Count > 0;
        return new AccountDisconnectPreview(isConnected, names);
    }

    /// <inheritdoc />
    public async Task<int> DisconnectAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        await supervisor.StopListenerAsync(guildId, ownerUserId).ConfigureAwait(false);

        var registration = await registrations.GetAsync(guildId, ownerUserId, cancellationToken).ConfigureAwait(false);
        if (registration is not null)
        {
            await registrations.SetStatusAsync(registration.Id, FcmRegistrationStatus.Disabled, cancellationToken)
                .ConfigureAwait(false);
        }

        var affected = await credentials.RemoveForOwnerAsync(guildId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var serverId in affected)
        {
            await eventBus.PublishAsync(new ServerCredentialsChangedEvent(guildId, serverId), cancellationToken)
                .ConfigureAwait(false);
        }

        return affected.Count;
    }
}
