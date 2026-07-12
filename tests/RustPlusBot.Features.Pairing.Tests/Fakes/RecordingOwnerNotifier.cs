using System.Collections.Concurrent;
using RustPlusBot.Features.Pairing.Notifications;

namespace RustPlusBot.Features.Pairing.Tests.Fakes;

/// <summary>Records expiry notifications instead of DMing.</summary>
internal sealed class RecordingOwnerNotifier : IOwnerNotifier
{
    /// <summary>All (guild, owner) pairs that received an expiry notification.</summary>
    public ConcurrentBag<(ulong Guild, ulong Owner)> Notified { get; } = [];

    /// <summary>All (guild, owner) pairs told that #setup is missing.</summary>
    public ConcurrentBag<(ulong Guild, ulong Owner)> SetupMissing { get; } = [];

    /// <inheritdoc />
    public Task NotifyCredentialsExpiredAsync(ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        Notified.Add((guildId, ownerUserId));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifySetupChannelMissingAsync(ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        SetupMissing.Add((guildId, ownerUserId));
        return Task.CompletedTask;
    }
}
