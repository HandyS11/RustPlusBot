using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Bundles the security/notification collaborators injected into <see cref="ConnectionSupervisor"/>.</summary>
/// <param name="DmSender">DMs an owner when their credential is rejected.</param>
/// <param name="Protector">Unprotects stored tokens before connecting.</param>
internal sealed record ConnectionSecurity(IUserDmSender DmSender, ICredentialProtector Protector);

/// <summary>Default <see cref="IConnectionSupervisor"/>: one connect->heartbeat->failover loop per (guild, server).</summary>
/// <param name="source">Creates sockets (RustPlusApi in production, a fake in tests).</param>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="security">Bundles the security/notification collaborators.</param>
/// <param name="eventBus">Publishes ConnectionStatusChangedEvent on state changes.</param>
/// <param name="clock">Wall-clock source used for AFK hysteresis timestamps.</param>
/// <param name="options">Timeouts/backoff/heartbeat settings.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ConnectionSupervisor(
    IRustSocketSource source,
    IServiceScopeFactory scopeFactory,
    ConnectionSecurity security,
    IEventBus eventBus,
    IClock clock,
    IOptions<ConnectionOptions> options,
    ILogger<ConnectionSupervisor> logger)
    : IConnectionSupervisor, IChatSender, IRustServerQuery, IAfkState, IAsyncDisposable
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), Handle> _connections = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), LiveSocket> _liveSockets = new();
    private readonly ConnectionOptions _options = options.Value;

    /// <summary>
    ///     Last status this process REACHED THE PUBLISH STEP WITH per key — the store's persisted status
    ///     survives restarts and would falsely report Connected at boot. Recorded before bus delivery on
    ///     purpose: WasConnected must reflect what the supervisor observed, so a failed/cancelled delivery
    ///     of a Connected event cannot make the next real drop skip its unreachable sweep.
    /// </summary>
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ConnectionStatus> _publishedStatuses = new();

    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    /// <inheritdoc />
    public Task<IReadOnlyList<AfkMember>?> GetAfkMembersAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return Task.FromResult<IReadOnlyList<AfkMember>?>(live.Tracker.CurrentAfk(clock.UtcNow));
        }

        return Task.FromResult<IReadOnlyList<AfkMember>?>(null);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The supervisor is registered as one singleton backing three service types (IConnectionSupervisor,
        // IChatSender, and the concrete type), so the DI container may invoke DisposeAsync more than once.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAllAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _gate.Dispose();
    }

    /// <inheritdoc />
    public async Task<ChatSendResult> SendAsync(
        ChatChannelKind kind,
        ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return ChatSendResult.NotConnected;
        }

        try
        {
            switch (kind)
            {
                case ChatChannelKind.Team:
                    await live.Connection.SendTeamMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    break;
                case ChatChannelKind.Clan:
                    await live.Connection.SendClanMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return ChatSendResult.Failed;
            }

            return ChatSendResult.Sent;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed relay send must not crash the caller; report Failed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, ex, serverId);
            return ChatSendResult.Failed;
        }
    }

    /// <inheritdoc />
    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(ulong GuildId, Guid ServerId)> servers;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            servers = await store.ListConnectableServersAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (guildId, serverId) in servers)
        {
            await EnsureConnectionAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task EnsureConnectionAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        var key = (guildId, serverId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopConnectionAsync(key).ConfigureAwait(false);
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _connections[key] = new Handle(cts, Task.Run(() => RunAsync(key, cts.Token), CancellationToken.None));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(ulong guildId, Guid serverId)
    {
        // CancellationToken.None, not _shutdown.Token: teardown must still acquire the gate after
        // StopAllAsync has already cancelled _shutdown, otherwise the connection is never stopped.
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopConnectionAsync((guildId, serverId)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        // CancellationToken.None: _shutdown was just cancelled, so waiting on it would abandon shutdown.
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            foreach (var key in _connections.Keys.ToList())
            {
                await StopConnectionAsync(key).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServerInfoSnapshot?> GetServerInfoAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetServerInfoAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ServerTimeSnapshot?> GetTimeAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetTimeAsync(_options.HeartbeatTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TeamInfoSnapshot?> GetTeamInfoAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetTeamInfoAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> PromoteToLeaderAsync(
        ulong guildId,
        Guid serverId,
        ulong steamId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return false;
        }

        return await live.Connection.PromoteToLeaderAsync(steamId, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetMapImageAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetMapImageAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MapDimensions?> GetMapDimensionsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetMapDimensionsAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorldSnapshot?> GetWorldAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetWorldAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return [];
        }

        try
        {
            return await live.Connection.GetMonumentsAsync(_options.HeartbeatTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: this seam promises degradation, so a failed fetch is "no monuments".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The connection-level call throws on a failed/timed-out GetMap (rate limit, no map, slow
            // endpoint). Callers here are render paths that must degrade to an icon-less map, never fault:
            // an escaping exception tears down the consuming loop for the rest of the process.
            LogMonumentsQueryFailed(logger, ex, serverId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<bool?> GetSmartSwitchStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        var reading = await live.Connection
            .GetSmartDeviceInfoAsync(entityId, SmartDeviceKind.Switch, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
        return reading.IsActive;
    }

    /// <inheritdoc />
    public async Task<DeviceReading> GetSmartAlarmReadingAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return new DeviceReading(null, DeviceReachability.NoResponse);
        }

        return await live.Connection
            .GetSmartDeviceInfoAsync(entityId, SmartDeviceKind.Alarm, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StorageContentsSnapshot?> GetStorageContentsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        var reading = await live.Connection
            .GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
        return reading.Contents;
    }

    /// <inheritdoc />
    public async Task<DeviceReachability> SetSmartSwitchAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return DeviceReachability.NoResponse;
        }

        return await live.Connection
            .SetSmartSwitchValueAsync(entityId, value, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DeviceReachability> StrobeSmartSwitchAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        int timeoutMs,
        bool value,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return DeviceReachability.NoResponse;
        }

        return await live.Connection
            .StrobeSmartSwitchAsync(entityId, timeoutMs, value, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> SetClanMotdAsync(
        ulong guildId,
        Guid serverId,
        string motd,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return false;
        }

        return await live.Connection
            .SetClanMotdAsync(motd, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection loop for server {ServerId} faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored token for credential {CredentialId} is unreadable.")]
    private static partial void LogUnreadableToken(ILogger logger, Exception exception, Guid credentialId);

    private async Task StopConnectionAsync((ulong Guild, Guid Server) key)
    {
        if (!_connections.TryRemove(key, out var handle))
        {
            return;
        }

        await handle.StopAsync().ConfigureAwait(false);
        await handle.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunAsync((ulong Guild, Guid Server) key, CancellationToken ct)
    {
        var delay = _options.InitialRetryDelay;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var prepared = await PrepareAsync(key, ct).ConfigureAwait(false);
                if (prepared is null)
                {
                    await PublishStatusAsync(key, ConnectionStatus.NoCredentials, null, null, ct).ConfigureAwait(false);
                    return;
                }

                var p = prepared.Value;
                await PublishStatusAsync(key, ConnectionStatus.Connecting, null, p.CredentialId, ct)
                    .ConfigureAwait(false);

                var connection = source.Create(p.Ip, p.Port, p.SteamId, p.PlayerToken);
                SocketConnectOutcome outcome;
                try
                {
                    outcome = await connection.ConnectAsync(_options.ConnectTimeout, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                if (outcome == SocketConnectOutcome.AuthRejected)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    // No backoff here: failover should be prompt. The loop is bounded — each rejection permanently
                    // marks one credential Invalid (never re-selected), so an all-reject pool converges to NoCredentials.
                    await FailoverAsync(p.CredentialId, p.OwnerUserId, p.ServerName, ct).ConfigureAwait(false);
                    delay = _options.InitialRetryDelay;
                    continue;
                }

                if (outcome == SocketConnectOutcome.Unreachable)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    await PublishStatusAsync(key, ConnectionStatus.Unreachable, null, p.CredentialId, ct)
                        .ConfigureAwait(false);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = NextDelay(delay);
                    continue;
                }

                // Connected: run the heartbeat loop until it signals a reason to reconnect.
                delay = _options.InitialRetryDelay;
                ReconnectReason reason;
                try
                {
                    reason = await RunConnectedAsync(key, connection, p.CredentialId, p.SteamId, ct)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }

                if (reason == ReconnectReason.AuthRejected)
                {
                    // No backoff: failover is prompt; pool exhaustion converges to NoCredentials.
                    await FailoverAsync(p.CredentialId, p.OwnerUserId, p.ServerName, ct).ConfigureAwait(false);
                }
                else if (reason == ReconnectReason.Unreachable)
                {
                    await PublishStatusAsync(key, ConnectionStatus.Unreachable, null, p.CredentialId, ct)
                        .ConfigureAwait(false);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = NextDelay(delay);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
#pragma warning disable CA1031 // Broad catch is intentional: a faulting loop must not crash the host or other servers.
        catch (Exception ex)
        {
            LogLoopFaulted(logger, ex, key.Server);
        }
#pragma warning restore CA1031
    }

    private async Task<ReconnectReason> RunConnectedAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        Guid credentialId,
        ulong activeSteamId,
        CancellationToken ct)
    {
        var first = await connection.GetInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        if (first.Kind == HeartbeatKind.AuthRejected)
        {
            return ReconnectReason.AuthRejected;
        }

        if (first.Kind == HeartbeatKind.Unreachable)
        {
            return ReconnectReason.Unreachable;
        }

        await PublishStatusAsync(key, ConnectionStatus.Connected, first.PlayerCount, credentialId, ct)
            .ConfigureAwait(false);

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<TeamChatLine> delegate shape.
        void OnTeamMessage(object? sender, TeamChatLine line)
        {
            // Fire-and-forget: PublishTeamMessageAsync catches everything internally, so the discarded task
            // never surfaces an unobserved exception. Team chat is low-volume, so unbounded concurrency is fine.
            _ = PublishTeamMessageAsync(key, activeSteamId, line);
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<SmartDeviceTrigger> delegate shape.
        void OnSmartDevice(object? sender, SmartDeviceTrigger trigger)
        {
            // Fire-and-forget: PublishDeviceTriggerAsync catches everything internally, so the discarded task never
            // surfaces an unobserved exception. Device triggers are low-volume, so unbounded concurrency is fine.
            _ = PublishDeviceTriggerAsync(key, trigger);
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<StorageMonitorTrigger> delegate shape.
        void OnStorage(object? sender, StorageMonitorTrigger trigger)
        {
            _ = PublishStorageTriggerAsync(key, trigger);
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<ClanChatLine> delegate shape.
        void OnClanMessage(object? sender, ClanChatLine line)
        {
            _ = PublishClanMessageAsync(key, activeSteamId, line);
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<ClanProbeResult> delegate shape.
        void OnClanChanged(object? sender, ClanProbeResult probe)
        {
            _ = PublishClanStateAsync(key, probe);
        }
#pragma warning restore RCS1163

        var tracker = new TeamStateTracker();
        var dims = new DimensionsHolder();

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<TeamInfoSnapshot> delegate shape.
        void OnTeamChanged(object? sender, TeamInfoSnapshot snapshot)
        {
            // Fire-and-forget: PublishTeamStateAsync catches everything internally.
            _ = PublishTeamStateAsync(key, tracker, dims, snapshot);
        }
#pragma warning restore RCS1163

        connection.TeamMessageReceived += OnTeamMessage;
        connection.SmartDeviceTriggered += OnSmartDevice;
        connection.StorageMonitorTriggered += OnStorage;
        connection.ClanMessageReceived += OnClanMessage;
        connection.ClanChanged += OnClanChanged;
        connection.TeamChanged += OnTeamChanged;
        _liveSockets[key] = new LiveSocket(connection, activeSteamId, tracker);
        await PrimeDevicesAsync(key, connection, ct).ConfigureAwait(false);

        // Probe once on connect so clan state is correct after a bot restart, not only after the
        // next in-game change. An Unavailable result publishes too: the consumer preserves state.
        var clanProbe = await connection.GetClanInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        LogClanPrimeProbed(logger, key.Server, clanProbe.Status);
        await PublishClanStateAsync(key, clanProbe).ConfigureAwait(false);

        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var markerPoll = Task.Run(() => PollMarkersAsync(key, connection, dims, pollCts.Token),
            CancellationToken.None);
        var reachabilityPoll = Task.Run(() => PollReachabilityAsync(key, connection, pollCts.Token),
            CancellationToken.None);
        var teamPoll = Task.Run(() => PollTeamAsync(key, connection, tracker, dims, pollCts.Token),
            CancellationToken.None);
        // Race the heartbeat against a liveness watchdog: the Rust+ library raises no event when the SERVER
        // closes the socket, so without the watchdog a silent drop goes unnoticed until the next heartbeat
        // (up to a minute). Whichever signals a reason first wins; the finally cancels and joins the rest.
        var heartbeat = RunHeartbeatLoopAsync(key, connection, credentialId, pollCts.Token);
        var liveness = WatchLivenessAsync(key, connection, pollCts.Token);
        try
        {
#pragma warning disable VSTHRD003 // Suppress: heartbeat and liveness are owned by this connected window and joined below.
            var winner = await Task.WhenAny(heartbeat, liveness).ConfigureAwait(false);
            return await winner.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            await pollCts.CancelAsync().ConfigureAwait(false);
            try
            {
#pragma warning disable VSTHRD003 // Suppress: all five tasks are owned by this connected window and explicitly joined on exit.
                await Task.WhenAll(markerPoll, reachabilityPoll, teamPoll, heartbeat, liveness).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }

            _liveSockets.TryRemove(key, out _);
            connection.TeamMessageReceived -= OnTeamMessage;
            connection.SmartDeviceTriggered -= OnSmartDevice;
            connection.StorageMonitorTriggered -= OnStorage;
            connection.ClanMessageReceived -= OnClanMessage;
            connection.ClanChanged -= OnClanChanged;
            connection.TeamChanged -= OnTeamChanged;
        }
    }

    /// <summary>
    /// Polls <see cref="IRustServerConnection.IsConnected"/> while connected and returns
    /// <see cref="ReconnectReason.Unreachable"/> as soon as the socket is no longer open. This is the only
    /// prompt signal for a server-initiated close, which the Rust+ library does not surface as an event.
    /// </summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="connection">The live connection whose liveness is watched.</param>
    /// <param name="ct">Cancels when the connected window ends.</param>
    /// <returns><see cref="ReconnectReason.Unreachable"/> on a detected drop, else
    /// <see cref="ReconnectReason.Stopped"/> when cancelled.</returns>
    private async Task<ReconnectReason> WatchLivenessAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.LivenessPollInterval, ct).ConfigureAwait(false);
                if (!connection.IsConnected)
                {
                    LogSocketDropped(logger, key.Server);
                    return ReconnectReason.Unreachable;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The connected window is ending (stop/reconnect): Task.Delay throws on cancellation.
            // Return Stopped so the WhenAny winner is a clean reason rather than a faulted task.
        }

        return ReconnectReason.Stopped;
    }

    private async Task<ReconnectReason> RunHeartbeatLoopAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        Guid credentialId,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.HeartbeatInterval, ct).ConfigureAwait(false);
            var beat = await connection.GetInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
            switch (beat.Kind)
            {
                case HeartbeatKind.Ok:
                    await PublishStatusAsync(key, ConnectionStatus.Connected, beat.PlayerCount, credentialId, ct)
                        .ConfigureAwait(false);
                    break;
                case HeartbeatKind.AuthRejected:
                    return ReconnectReason.AuthRejected;
                default:
                    return ReconnectReason.Unreachable;
            }
        }

        return ReconnectReason.Stopped;
    }

    private async Task PollMarkersAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        DimensionsHolder dims,
        CancellationToken ct)
    {
        // Fetch map dimensions and oil-rig positions here, off the critical connect path: these are the two
        // heavy full-map downloads, and on a degraded map endpoint they can stall for seconds. Doing them in
        // this background poll means a slow map no longer delays the connection going live (heartbeat + chat
        // relay start immediately); marker/rig detection simply activates once these resolve. Both degrade
        // safely on timeout (dims -> null, rigs -> empty) without ending the poll.
        var localDims = await connection.GetMapDimensionsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        dims.Value = localDims;
        var rigs = await GetRigPositionsAsync(key.Server, connection, ct).ConfigureAwait(false);

        IReadOnlyList<MapMarkerSnapshot>? previous = null;
        var rigsInRadius = new HashSet<RigKind>();
        while (!ct.IsCancellationRequested)
        {
            var anyCh47 = false;
            try
            {
                var current = await connection.GetMapMarkersAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                anyCh47 = current.Any(m => m.Kind == MarkerKind.Chinook);

                if (previous is null)
                {
                    previous = current; // first poll: silent baseline
                }
                else
                {
                    await PublishMarkerDeltaAsync(key, localDims, previous, current, ct).ConfigureAwait(false);
                    previous = current;
                }

                await DetectRigActivationsAsync(key, current, rigs, localDims, rigsInRadius, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // stopping
            }
#pragma warning disable CA1031 // Broad catch: a failed poll (incl. a per-request timeout) is logged and skipped; the previous snapshot is retained.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // A per-request timeout (OperationCanceledException with ct NOT cancelled) must NOT end the
                // poll loop — that would silently stop marker/rig/AFK detection for the rest of the connection.
                LogMarkerPollFailed(logger, ex, key.Server);
            }

            var delay = anyCh47 ? _options.MarkerPollFastInterval : _options.MarkerPollInterval;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Low-frequency team poll that runs the same <see cref="TeamStateTracker.Diff"/> as the pushed
    /// team_changed handler. Live changes arrive via the push event; this loop exists solely to (a) prime the
    /// baseline on connect and (b) guarantee <c>Diff</c> runs periodically so a still player in a
    /// broadcast-silent team is still flagged AFK. Its first iteration runs immediately (prime), then it waits
    /// <see cref="ConnectionOptions.TeamPollInterval"/> between iterations. Degrades safely: a failed poll is
    /// logged and skipped, never ending the loop.
    /// </summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="connection">The live connection to poll.</param>
    /// <param name="tracker">The shared AFK/online tracker whose baseline this poll also primes/diffs.</param>
    /// <param name="dims">The connected window's dimensions holder, read for the published event.</param>
    /// <param name="ct">Cancels when the connected window ends.</param>
    private async Task PollTeamAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        TeamStateTracker tracker,
        DimensionsHolder dims,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var team = await connection.GetTeamInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                await PublishTeamStateAsync(key, tracker, dims, team).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // stopping
            }
#pragma warning disable CA1031 // Broad catch: a failed team poll is logged and skipped; the loop survives.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTeamPollFailed(logger, ex, key.Server);
            }

            await Task.Delay(_options.TeamPollInterval, ct).ConfigureAwait(false);
        }
    }

    private async Task PublishMarkerDeltaAsync(
        (ulong Guild, Guid Server) key,
        MapDimensions? dims,
        IReadOnlyList<MapMarkerSnapshot> previous,
        IReadOnlyList<MapMarkerSnapshot> current,
        CancellationToken ct)
    {
        var previousById = previous.ToDictionary(p => p.Id);
        var added = new List<MapMarkerSnapshot>();
        var moved = new List<MapMarkerSnapshot>();
        foreach (var c in current)
        {
            if (!previousById.TryGetValue(c.Id, out var p))
            {
                added.Add(c);
            }
#pragma warning disable S1244 // Exact float compare is intentional: a stationary marker round-trips identical floats.
            else if (c.X != p.X || c.Y != p.Y || !Nullable.Equals(c.Rotation, p.Rotation))
#pragma warning restore S1244
            {
                moved.Add(c);
            }
        }

        var removed = previous.Where(p => current.All(c => c.Id != p.Id)).ToList();
        if (added.Count > 0 || removed.Count > 0 || moved.Count > 0)
        {
            await eventBus.PublishAsync(
                    new MapMarkersChangedEvent(key.Guild, key.Server, dims, added, removed, moved), ct)
                .ConfigureAwait(false);
        }
    }

    private async Task PollReachabilityAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        CancellationToken ct)
    {
        var previous = new Dictionary<ulong, DeviceReachability>();
        var seeded = false;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.ReachabilityPollInterval, ct).ConfigureAwait(false);
            Dictionary<ulong, DeviceReachability> current;
            try
            {
                current = await ReadAllReachabilityAsync(key, connection, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a failed reachability sweep (incl. a per-request timeout) is logged and retried next cycle.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // A per-request timeout (OperationCanceledException with ct NOT cancelled) must be retried next
                // cycle, not rethrown — rethrowing would tear the sweep down for the rest of the connection.
                LogReachabilityPollFailed(logger, ex, key.Server);
                continue;
            }

            if (!seeded)
            {
                foreach (var kvp in current)
                {
                    previous[kvp.Key] = kvp.Value;
                }

                seeded = true;
                continue; // first cycle: silent baseline
            }

            foreach (var change in ReachabilitySweep.Diff(previous, current))
            {
                previous[change.Key] = change.Value;
                await eventBus.PublishAsync(
                        new DeviceReachabilityChangedEvent(key.Guild, key.Server, change.Key, change.Value), ct)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads every managed device's reachability for one sweep cycle. Side effect: reachable storage
    /// monitors get their just-read contents republished as <see cref="StorageMonitorTriggeredEvent"/>,
    /// giving embeds a periodic contents refresh independent of broadcasts.
    /// </summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="connection">The live connection used to read device state.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The reachability snapshot keyed by entity id.</returns>
    private async Task<Dictionary<ulong, DeviceReachability>> ReadAllReachabilityAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        CancellationToken ct)
    {
        var result = new Dictionary<ulong, DeviceReachability>();
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var switches = await scope.ServiceProvider.GetRequiredService<ISwitchStore>()
                .ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            var alarms = await scope.ServiceProvider.GetRequiredService<IAlarmStore>()
                .ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            var monitors = await scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>()
                .ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);

            var devices = switches.Select(s => (s.EntityId, SmartDeviceKind.Switch))
                .Concat(alarms.Select(a => (a.EntityId, SmartDeviceKind.Alarm)));
            foreach (var (entityId, kind) in devices)
            {
                var reading = await connection
                    .GetSmartDeviceInfoAsync(entityId, kind, _options.HeartbeatTimeout, ct)
                    .ConfigureAwait(false);
                result[entityId] = reading.Reachability;

                // Republish the state the read already carries as an OBSERVED event so drifted alarm
                // embeds self-correct (the consumer is silent: no ping/relay, no edit when unchanged).
                // Deliberately alarms only: switch embeds sync via actuation replies and broadcasts.
                if (kind == SmartDeviceKind.Alarm && reading is { IsActive: { } isActive })
                {
                    await eventBus.PublishAsync(
                            new SmartDeviceStateObservedEvent(key.Guild, key.Server, entityId, isActive), ct)
                        .ConfigureAwait(false);
                }
            }

#pragma warning disable S3267 // Not a projection: each iteration awaits with per-monitor best-effort error handling.
            foreach (var monitor in monitors)
#pragma warning restore S3267
            {
                var reading = await connection
                    .GetStorageMonitorInfoAsync(monitor.EntityId, _options.HeartbeatTimeout, ct)
                    .ConfigureAwait(false);
                result[monitor.EntityId] = reading.Reachability;

                // The read already carries the contents — republish them so embeds keep tracking in-game
                // changes even when no EntityChanged broadcast arrives (broadcasts alone are unreliable
                // for storage monitors). Storage renders have no ping/relay side effects, so a periodic
                // republish is safe; device (switch/alarm) triggers must NOT be republished here — an
                // active alarm would re-ping on every sweep.
                if (reading is { Reachability: DeviceReachability.Reachable, Contents: { } contents })
                {
                    await eventBus.PublishAsync(
                            new StorageMonitorTriggeredEvent(key.Guild, key.Server, monitor.EntityId, contents), ct)
                        .ConfigureAwait(false);
                }
            }
        }

        return result;
    }

    private async Task DetectRigActivationsAsync(
        (ulong Guild, Guid Server) key,
        IReadOnlyList<MapMarkerSnapshot> current,
        IReadOnlyList<RigPosition> rigs,
        MapDimensions? dims,
        HashSet<RigKind> rigsInRadius,
        CancellationToken ct)
    {
        if (rigs.Count == 0)
        {
            return;
        }

        var radiusSquared = _options.RigRadius * _options.RigRadius;
        var nowInRadius = new HashSet<RigKind>();
        foreach (var rig in rigs)
        {
            foreach (var m in current)
            {
                if (m.Kind != MarkerKind.Chinook)
                {
                    continue;
                }

                var dx = m.X - rig.X;
                var dy = m.Y - rig.Y;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    nowInRadius.Add(rig.Kind);
                    if (!rigsInRadius.Contains(rig.Kind))
                    {
                        await eventBus.PublishAsync(
                                new RigStateChangedEvent(key.Guild, key.Server, rig.Kind, RigEventKind.Activated,
                                    rig.X, rig.Y, dims), ct)
                            .ConfigureAwait(false);
                    }

                    break; // one CH47 in radius is enough for this rig
                }
            }
        }

        rigsInRadius.Clear();
        rigsInRadius.UnionWith(nowInRadius);
    }

    private async Task<IReadOnlyList<RigPosition>> GetRigPositionsAsync(
        Guid serverId,
        IRustServerConnection connection,
        CancellationToken ct)
    {
        try
        {
            var monuments = await connection.GetMonumentsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
            var rigs = new List<RigPosition>();
            foreach (var m in monuments)
            {
                RigKind? kind = m.Token switch
                {
                    "oil_rig_small" => RigKind.Small,
                    "large_oil_rig" => RigKind.Large,
                    _ => null,
                };
                if (kind is { } k)
                {
                    rigs.Add(new RigPosition(k, m.X, m.Y));
                }
            }

            return rigs;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Real shutdown/reconnect of THIS connection: propagate so the loop tears down.
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a monuments-fetch failure (incl. a per-request timeout) just disables rig detection this window.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // A per-request timeout surfaces as OperationCanceledException with ct NOT cancelled; it must
            // degrade rig detection for this window, never terminate the connection loop.
            LogMonumentsFetchFailed(logger, ex, serverId); // rig detection degrades gracefully for this window
            return [];
        }
    }

    private async Task<Prepared?> PrepareAsync((ulong Guild, Guid Server) key, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();

            var server = await servers.GetAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            if (server is null)
            {
                return null;
            }

            while (!ct.IsCancellationRequested)
            {
                var active = await store.GetActiveCredentialAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
                if (active is null)
                {
                    var pool = await store.ListPoolAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
                    var next = pool.FirstOrDefault(c => c.Status == CredentialStatus.Standby);
                    if (next is null)
                    {
                        return null;
                    }

                    await store.PromoteAsync(key.Guild, key.Server, next.Id, ct).ConfigureAwait(false);
                    active = next;
                }

                string token;
                try
                {
                    token = security.Protector.Unprotect(active.ProtectedPlayerToken);
                }
                catch (CryptographicException ex)
                {
                    LogUnreadableToken(logger, ex, active.Id);
                    await store.MarkInvalidAsync(active.Id, ct).ConfigureAwait(false);
                    await security.DmSender.SendAsync(
                            active.OwnerUserId,
                            $"Your Rust+ credential for **{server.Name}** could not be read — reconnect in #setup.",
                            ct)
                        .ConfigureAwait(false);
                    continue;
                }

                return new Prepared(server.Ip, server.Port, server.Name, active.Id, active.OwnerUserId, active.SteamId,
                    token);
            }

            return null;
        }
    }

    private async Task FailoverAsync(Guid credentialId, ulong ownerUserId, string serverName, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            await store.MarkInvalidAsync(credentialId, ct).ConfigureAwait(false);
        }

        await security.DmSender.SendAsync(
                ownerUserId,
                $"Your Rust+ credential for **{serverName}** was rejected — reconnect in #setup to keep it in the pool.",
                ct)
            .ConfigureAwait(false);
    }

    private async Task PublishStatusAsync(
        (ulong Guild, Guid Server) key,
        ConnectionStatus status,
        int? playerCount,
        Guid? activeCredentialId,
        CancellationToken ct)
    {
        bool changed;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            changed = await store
                .UpsertStatusAsync(key.Guild, key.Server, status, playerCount, activeCredentialId, ct)
                .ConfigureAwait(false);
        }

        if (changed)
        {
            var wasConnected = _publishedStatuses.TryGetValue(key, out var previous)
                               && previous == ConnectionStatus.Connected;
            _publishedStatuses[key] = status;
            await eventBus.PublishAsync(
                    new ConnectionStatusChangedEvent(key.Guild, key.Server,
                        status == ConnectionStatus.Connected, wasConnected), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Test seam: true when a live socket is currently registered for the key.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <returns>True when a live socket is registered for (<paramref name="guildId"/>, <paramref name="serverId"/>).</returns>
    internal bool HasLiveSocket(ulong guildId, Guid serverId) => _liveSockets.ContainsKey((guildId, serverId));

    private async Task PublishTeamMessageAsync((ulong Guild, Guid Server) key, ulong activeSteamId, TeamChatLine line)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var evt = new TeamMessageReceivedEvent(
                key.Guild, key.Server, line.SteamId, line.Name, line.Message, line.SteamId == activeSteamId);
            // Use the supervisor-wide shutdown token (not a per-connection ct): an inbound line should publish
            // regardless of one connection's reconnect cycle, stopping only on global shutdown.
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishTeamMessageFailed(logger, ex, key.Server);
        }
    }

    private async Task PublishClanMessageAsync((ulong Guild, Guid Server) key, ulong activeSteamId, ClanChatLine line)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var evt = new ClanMessageReceivedEvent(
                key.Guild, key.Server, line.SteamId, line.Name, line.Message, line.SteamId == activeSteamId);
            // Supervisor-wide shutdown token, not a per-connection ct: an inbound line should publish
            // regardless of one connection's reconnect cycle.
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishClanMessageFailed(logger, ex, key.Server);
        }
    }

    private async Task PublishClanStateAsync((ulong Guild, Guid Server) key, ClanProbeResult probe)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var evt = new ClanStateChangedEvent(key.Guild, key.Server, probe.Status, probe.Snapshot);
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishClanStateFailed(logger, ex, key.Server);
        }
    }

    private async Task PublishTeamStateAsync(
        (ulong Guild, Guid Server) key,
        TeamStateTracker tracker,
        DimensionsHolder dims,
        TeamInfoSnapshot? snapshot)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var transitions = tracker.Diff(snapshot, clock.UtcNow, _options.AfkThreshold, _options.AfkEpsilon);
            if (transitions.Count == 0)
            {
                return;
            }

            var evt = new PlayerStateChangedEvent(key.Guild, key.Server, dims.Value, transitions);
            // Supervisor-wide shutdown token, not a per-connection ct: a pushed team change should publish
            // regardless of one connection's reconnect cycle (mirrors the chat/clan handlers).
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback or poll.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishTeamStateFailed(logger, ex, key.Server);
        }
    }

    private async Task PrimeDevicesAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        CancellationToken ct)
    {
        IReadOnlyList<Domain.Switches.SmartSwitch> switches;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
                switches = await store.ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed switch-list read just skips priming for this connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Best-effort: a store/DB failure must not crash the connected loop or block the heartbeat.
            LogDeviceListFailed(logger, ex, key.Server);
            return;
        }

        await PrimeEntityIdsAsync(key, connection,
            (IReadOnlyList<ulong>)[.. switches.Select(sw => sw.EntityId)],
            SmartDeviceKind.Switch).ConfigureAwait(false);

        IReadOnlyList<Domain.Alarms.SmartAlarm> alarms;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
                alarms = await store.ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed alarm-list read just skips alarm priming for this connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeviceListFailed(logger, ex, key.Server);
            return;
        }

        await PrimeEntityIdsAsync(key, connection,
            (IReadOnlyList<ulong>)[.. alarms.Select(a => a.EntityId)],
            SmartDeviceKind.Alarm).ConfigureAwait(false);

        IReadOnlyList<Domain.StorageMonitors.SmartStorageMonitor> monitors;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
                monitors = await store.ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed monitor-list read just skips storage priming for this connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeviceListFailed(logger, ex, key.Server);
            return;
        }

#pragma warning disable S3267 // Not a projection: each iteration awaits with per-entity best-effort error handling.
        foreach (var monitor in monitors)
#pragma warning restore S3267
        {
            try
            {
                await PublishStoragePrimeAsync(key, connection, monitor.EntityId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a single monitor's prime failure is logged and skipped.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDevicePrimeFailed(logger, ex, monitor.EntityId, key.Server);
            }
        }
    }

    private async Task PrimeEntityIdsAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        IReadOnlyList<ulong> entityIds,
        SmartDeviceKind kind)
    {
#pragma warning disable S3267 // Not a projection: each iteration awaits with per-entity best-effort error handling.
        foreach (var entityId in entityIds)
#pragma warning restore S3267
        {
            try
            {
                await PublishDevicePrimeAsync(key, connection, entityId, kind).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a single entity's prime failure is logged and skipped.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDevicePrimeFailed(logger, ex, entityId, key.Server);
            }
        }
    }

    /// <summary>Trigger path: IsActive is carried on the broadcast arg — no re-read.</summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="trigger">The device trigger carrying the entity id and active state.</param>
    private async Task PublishDeviceTriggerAsync(
        (ulong Guild, Guid Server) key,
        SmartDeviceTrigger trigger)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await eventBus.PublishAsync(
                    new SmartDeviceTriggeredEvent(key.Guild, key.Server, trigger.EntityId, trigger.IsActive),
                    _shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a device publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDevicePublishFailed(logger, ex, trigger.EntityId, key.Server);
        }
    }

    /// <summary>Prime path: read state on connect (also primes the socket's interest), then publish.</summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="connection">The live connection used to read device state.</param>
    /// <param name="entityId">The entity id of the device to prime.</param>
    /// <param name="kind">The paired device kind used for the type-checked Rust+ read.</param>
    private async Task PublishDevicePrimeAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        ulong entityId,
        SmartDeviceKind kind)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var reading = await connection
                .GetSmartDeviceInfoAsync(entityId, kind, _options.HeartbeatTimeout, _shutdown.Token)
                .ConfigureAwait(false);
            await eventBus.PublishAsync(
                    new DeviceReachabilityChangedEvent(key.Guild, key.Server, entityId, reading.Reachability),
                    _shutdown.Token)
                .ConfigureAwait(false);
            if (reading.Reachability == DeviceReachability.Reachable)
            {
                // A prime is an OBSERVATION for alarms: a triggered event would re-ping @everyone on every
                // reconnect while the alarm is active. Switch embeds keep the triggered path (no ping semantics).
                if (kind == SmartDeviceKind.Alarm)
                {
                    await eventBus.PublishAsync(
                            new SmartDeviceStateObservedEvent(key.Guild, key.Server, entityId,
                                reading.IsActive ?? false),
                            _shutdown.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    await eventBus.PublishAsync(
                            new SmartDeviceTriggeredEvent(key.Guild, key.Server, entityId,
                                reading.IsActive ?? false),
                            _shutdown.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a device prime failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDevicePublishFailed(logger, ex, entityId, key.Server);
        }
    }

    /// <summary>Trigger path: contents are carried on the broadcast arg — no re-read.</summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="trigger">The storage trigger carrying the entity id and contents.</param>
    private async Task PublishStorageTriggerAsync((ulong Guild, Guid Server) key, StorageMonitorTrigger trigger)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await eventBus.PublishAsync(
                    new StorageMonitorTriggeredEvent(key.Guild, key.Server, trigger.EntityId, trigger.Contents),
                    _shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a storage publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDevicePublishFailed(logger, ex, trigger.EntityId, key.Server);
        }
    }

    /// <summary>Prime path: read contents on connect (also primes the socket's interest), then publish.</summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="connection">The live connection used to read contents.</param>
    /// <param name="entityId">The storage-monitor entity id to prime.</param>
    private async Task PublishStoragePrimeAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        ulong entityId)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var reading = await connection
                .GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
                .ConfigureAwait(false);
            await eventBus.PublishAsync(
                    new DeviceReachabilityChangedEvent(key.Guild, key.Server, entityId, reading.Reachability),
                    _shutdown.Token)
                .ConfigureAwait(false);
            if (reading.Reachability == DeviceReachability.Reachable && reading.Contents is { } contents)
            {
                await eventBus.PublishAsync(
                        new StorageMonitorTriggeredEvent(key.Guild, key.Server, entityId, contents),
                        _shutdown.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a storage prime failure must not crash the connect path.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDevicePublishFailed(logger, ex, entityId, key.Server);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marker poll for server {ServerId} failed.")]
    private static partial void LogMarkerPollFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Team poll for server {ServerId} failed.")]
    private static partial void LogTeamPollFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing team state for server {ServerId} failed.")]
    private static partial void LogPublishTeamStateFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reachability poll for server {ServerId} failed.")]
    private static partial void LogReachabilityPollFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Socket for server {ServerId} closed by the server; reconnecting.")]
    private static partial void LogSocketDropped(ILogger logger, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message =
            "Fetching monuments for oil-rig detection on server {ServerId} failed; rig detection disabled for this connection.")]
    private static partial void LogMonumentsFetchFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Querying monuments for server {ServerId} failed; returning no monuments for this call.")]
    private static partial void LogMonumentsQueryFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relaying a message to team chat for server {ServerId} failed.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Publishing a received team message for server {ServerId} failed.")]
    private static partial void LogPublishTeamMessageFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing a clan message for server {ServerId} failed.")]
    private static partial void LogPublishClanMessageFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing clan state for server {ServerId} failed.")]
    private static partial void LogPublishClanStateFailed(ILogger logger, Exception exception, Guid serverId);

    // Information, once per connect: an Unavailable prime is deliberately ignored downstream, so
    // without this line a clan-probe failure is completely invisible in the logs.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connect-time clan probe for server {ServerId} returned {Status}.")]
    private static partial void LogClanPrimeProbed(ILogger logger, Guid serverId, ClanProbeStatus status);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Listing smart devices to prime on server {ServerId} failed; priming skipped for this connection.")]
    private static partial void LogDeviceListFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Priming smart device {EntityId} on server {ServerId} failed.")]
    private static partial void
        LogDevicePrimeFailed(ILogger logger, Exception exception, ulong entityId, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Publishing a smart-device state for entity {EntityId} on server {ServerId} failed.")]
    private static partial void LogDevicePublishFailed(ILogger logger,
        Exception exception,
        ulong entityId,
        Guid serverId);

    private TimeSpan NextDelay(TimeSpan delay) =>
        delay < _options.MaxRetryDelay
            ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxRetryDelay.Ticks))
            : _options.MaxRetryDelay;

    private enum ReconnectReason
    {
        Stopped = 0,
        Unreachable = 1,
        AuthRejected = 2,
    }

    private readonly record struct RigPosition(RigKind Kind, float X, float Y);

    private readonly record struct Prepared(
        string Ip,
        int Port,
        string ServerName,
        Guid CredentialId,
        ulong OwnerUserId,
        ulong SteamId,
        string PlayerToken);

    private sealed record LiveSocket(IRustServerConnection Connection, ulong ActiveSteamId, TeamStateTracker Tracker);

    /// <summary>Mutable, thread-visible holder for the per-connected-window map dimensions. The marker poll
    /// resolves these once off the critical path; the team push handler and team poll read them (possibly
    /// null before resolution — PlayerStateChangedEvent tolerates a null and renders without a grid ref).</summary>
    private sealed class DimensionsHolder
    {
        private volatile MapDimensions? _value;

        public MapDimensions? Value
        {
            get => _value;
            set => _value = value;
        }
    }

    private sealed class Handle(CancellationTokenSource cts, Task runTask) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            cts.Dispose();
            return ValueTask.CompletedTask;
        }

        public async Task StopAsync()
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
#pragma warning disable VSTHRD003 // Suppress: task is owned by this Handle and explicitly joined on stop.
                await runTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
        }
    }
}
