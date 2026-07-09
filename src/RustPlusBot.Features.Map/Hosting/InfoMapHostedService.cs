using System.Collections.Concurrent;
using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.RustMaps;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>Posts the static RustMaps map to #info: a bot-rendered grid+monuments fallback until the
/// RustMaps render is generated, then the RustMaps image. Credit-safe generation via the driver.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">The shared RustMaps generation state.</param>
/// <param name="driver">Advances one map key's generation one step per tick.</param>
/// <param name="composer">Renders the grid+monuments fallback image.</param>
/// <param name="poster">Posts the #info map image.</param>
/// <param name="locator">Resolves the #info Discord channel for a server.</param>
/// <param name="query">Live query seam (world size/seed).</param>
/// <param name="localizer">Localizes the #info embed strings.</param>
/// <param name="options">Supplies the generation-poll interval.</param>
/// <param name="scopeFactory">Opens scopes to read connection state and guild culture.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class InfoMapHostedService(
    IEventBus eventBus,
    IRustMapsMapCoordinator coordinator,
    RustMapsGenerationDriver driver,
    MapComposer composer,
    IInfoMapPoster poster,
    IInfoChannelLocator locator,
    IRustServerQuery query,
    ILocalizer localizer,
    IOptions<MapOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<InfoMapHostedService> logger) : IHostedService, IDisposable
{
    // Not a real URI: Discord's attachment:// pseudo-scheme references the file uploaded alongside the embed.
#pragma warning disable S1075
    private const string AttachmentImageUrl = "attachment://map.png";
#pragma warning restore S1075
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte> _connected = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Serializes the two callers (connection-status loop + tick loop) per server so their
    /// delete-then-repost cannot interleave and leave two #info map messages.
    /// </summary>
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), SemaphoreSlim> _gates = new();

    /// <summary>The id of each server's single #info map message, so it is edited in place, never duplicated.</summary>
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ulong> _messageIds = new();

    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), Posted> _posted = new();
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeConnectionStatusAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunTickAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _statusLoop, _tickLoop
                 }.Where(t => t is not null))
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop tasks, joined on stop.
                await loop!.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <summary>Ensures #info shows the correct map image for the server's current generation state.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the check (and any post) is done.</returns>
    public async Task EnsureInfoMapAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        // Both the connection-status loop and the periodic tick loop call this for the same server.
        // Serialize per server: otherwise the fallback post and the RustMaps-ready post run their
        // delete-prior-image scans concurrently, each misses the other's not-yet-committed message,
        // and both survive — leaving two #info map messages.
        var gate = _gates.GetOrAdd((guildId, serverId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInfoMapCoreAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureInfoMapCoreAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var world = await query.GetWorldAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (world is null)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } id)
        {
            return;
        }

        var key = new RustMapsMapKey((int)world.WorldSize, (int)world.Seed);
        coordinator.Register(key, guildId, serverId);

        var snapshot = coordinator.Snapshot(key);
        var mapKey = (guildId, serverId);

        if (snapshot is { State: RustMapsGenerationState.Ready, Ready: { } ready })
        {
            if (_posted.TryGetValue(mapKey, out var v) && v == Posted.Ready)
            {
                return;
            }

            var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            await UpsertAsync(mapKey, id, BuildEmbed(key, ready.RustMapsUrl, culture), ready.ImageBytes,
                cancellationToken).ConfigureAwait(false);
            _posted[mapKey] = Posted.Ready;
            return;
        }

        if (_posted.TryGetValue(mapKey, out var posted) && posted != Posted.None)
        {
            return; // fallback already up; nothing better to show yet.
        }

        var png = await composer.ComposeStaticAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            return; // base map not ready yet; retry next tick.
        }

        var fallbackCulture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        await UpsertAsync(mapKey, id, BuildEmbed(key, rustMapsUrl: null, fallbackCulture), png, cancellationToken)
            .ConfigureAwait(false);
        _posted[mapKey] = Posted.Fallback;
    }

    private async Task UpsertAsync((ulong Guild, Guid Server) mapKey,
        ulong channelId,
        Embed embed,
        byte[] pngBytes,
        CancellationToken cancellationToken)
    {
        var existing = _messageIds.TryGetValue(mapKey, out var tracked) ? tracked : (ulong?)null;
        var messageId = await poster.UpsertAsync(channelId, existing, embed, pngBytes, cancellationToken)
            .ConfigureAwait(false);
        if (messageId is { } id)
        {
            _messageIds[mapKey] = id;
        }
    }

    private Embed BuildEmbed(RustMapsMapKey key, string? rustMapsUrl, string culture)
    {
        var builder = new EmbedBuilder()
            .WithTitle(localizer.Get("map.info.title", culture))
            .WithColor(Color.DarkGreen)
            .AddField(localizer.Get("map.info.size", culture),
                key.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), inline: true)
            .AddField(localizer.Get("map.info.seed", culture),
                key.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), inline: true)
            .WithImageUrl(AttachmentImageUrl);
        if (rustMapsUrl is not null)
        {
            builder.WithUrl(rustMapsUrl);
        }
        else
        {
            builder.WithFooter(localizer.Get("map.info.generating", culture));
        }

        return builder.Build();
    }

    private async Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            return await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Advances every pending RustMaps generation key strictly one-at-a-time (sequential, never
    /// concurrent) — the driver's check-then-spend path is not atomic across awaits, so concurrent
    /// calls for the same key could double-spend real RustMaps credits. Then repaints every connected
    /// server's #info.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(options.Value.RustMaps.GenerationPollInterval, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var key in coordinator.PendingKeys())
                {
                    await driver.AdvanceAsync(key, cancellationToken).ConfigureAwait(false);
                }

                foreach (var (guild, server) in _connected.Keys)
                {
                    await EnsureInfoMapAsync(guild, server, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting tick must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogTickFaulted(logger, ex);
        }
    }

    private async Task ConsumeConnectionStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await OnConnectionStatusAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStatusFaulted(logger, ex);
        }
    }

    private async Task OnConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        var key = (evt.GuildId, evt.ServerId);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await store.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
            if (state is null || state.Status != ConnectionStatus.Connected)
            {
                _connected.TryRemove(key, out _);
                return;
            }

            _connected[key] = 0;
        }

        await EnsureInfoMapAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map tick loop faulted.")]
    private static partial void LogTickFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map connection-status loop faulted.")]
    private static partial void LogStatusFaulted(ILogger logger, Exception exception);

    private enum Posted
    {
        None = 0,
        Fallback = 1,
        Ready = 2,
    }
}
