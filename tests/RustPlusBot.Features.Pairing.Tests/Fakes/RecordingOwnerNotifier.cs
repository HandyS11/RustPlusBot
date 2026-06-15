using System.Collections.Concurrent;
using RustPlusBot.Features.Pairing.Notifications;

namespace RustPlusBot.Features.Pairing.Tests.Fakes;

/// <summary>Records expiry notifications instead of DMing.</summary>
internal sealed class RecordingOwnerNotifier : IOwnerNotifier
{
    /// <summary>All (guild, owner) pairs that received an expiry notification.</summary>
    public ConcurrentBag<(ulong Guild, ulong Owner)> Notified { get; } = [];

    /// <inheritdoc />
    public Task NotifyCredentialsExpiredAsync(ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        Notified.Add((guildId, ownerUserId));
        return Task.CompletedTask;
    }
}
