using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Persistence.Wipes;

namespace RustPlusBot.Features.Wipes.Detection;

/// <summary>
/// Default <see cref="IWipeDetector"/>. A wipe always implies a server restart, so the check runs on each
/// transition to connected: the server wiped when the wipe time advanced beyond a small jitter tolerance
/// or the map seed/size changed. The first-ever observation backfills the baseline silently so existing
/// deployments never see a false wipe on upgrade.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped baseline store.</param>
/// <param name="query">Reads live server info from the socket.</param>
/// <param name="eventBus">Publishes <see cref="ServerWipedEvent"/>.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class WipeDetector(
    IServiceScopeFactory scopeFactory,
    IRustServerQuery query,
    IEventBus eventBus,
    ILogger<WipeDetector> logger) : IWipeDetector
{
    /// <summary>Absorbs clock jitter in the reported wipe time across restarts; anything larger counts as a wipe.</summary>
    private static readonly TimeSpan _wipeTimeTolerance = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task CheckAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var info = await query.GetServerInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        var world = await query.GetWorldAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (info is null || world is null)
        {
            return; // Socket dropped mid-check; the next reconnect retries.
        }

        WipeBaseline? baseline;
        WipeBaseline observed;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWipeBaselineStore>();
            baseline = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (baseline is null)
            {
                return; // Server removed mid-check.
            }

            observed = new WipeBaseline(info.WipeTimeUtc ?? baseline.WipeTimeUtc, world.Seed, world.WorldSize);
            if (baseline == observed)
            {
                return; // Plain reconnect with nothing changed.
            }

            await store.SetAsync(guildId, serverId, observed, cancellationToken).ConfigureAwait(false);
        }

        if (baseline is { WipeTimeUtc: null, MapSeed: null, MapSize: null })
        {
            LogBaselineStored(logger, guildId, serverId);
            return; // First observation ever: backfill silently.
        }

        var wiped =
            (info.WipeTimeUtc is { } newWipe && baseline.WipeTimeUtc is { } oldWipe
                                             && newWipe > oldWipe + _wipeTimeTolerance)
            || (baseline.MapSeed is { } oldSeed && world.Seed != oldSeed)
            || (baseline.MapSize is { } oldSize && world.WorldSize != oldSize);
        if (!wiped)
        {
            return; // Jitter within tolerance or a null field backfilled — baseline refreshed silently.
        }

        await eventBus.PublishAsync(
                new ServerWipedEvent(guildId, serverId, baseline.WipeTimeUtc, info.WipeTimeUtc, world.Seed,
                    world.WorldSize),
                cancellationToken)
            .ConfigureAwait(false);
        LogWipeDetected(logger, guildId, serverId, world.Seed, world.WorldSize);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Stored first wipe baseline for guild {GuildId} server {ServerId}.")]
    private static partial void LogBaselineStored(ILogger logger, ulong guildId, Guid serverId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Wipe detected for guild {GuildId} server {ServerId}: seed {Seed}, size {WorldSize}.")]
    private static partial void LogWipeDetected(ILogger logger,
        ulong guildId,
        Guid serverId,
        uint seed,
        uint worldSize);
}
