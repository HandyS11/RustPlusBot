using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Default <see cref="IConnectionSupervisor"/>: one connect->heartbeat->failover loop per (guild, server).</summary>
/// <param name="source">Creates sockets (RustPlusApi in production, a fake in tests).</param>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="dmSender">DMs an owner when their credential is rejected.</param>
/// <param name="protector">Unprotects stored tokens before connecting.</param>
/// <param name="eventBus">Publishes ConnectionStatusChangedEvent on state changes.</param>
/// <param name="clock">Wall-clock source used for AFK hysteresis timestamps.</param>
/// <param name="options">Timeouts/backoff/heartbeat settings.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ConnectionSupervisor(
    IRustSocketSource source,
    IServiceScopeFactory scopeFactory,
    IUserDmSender dmSender,
    ICredentialProtector protector,
    IEventBus eventBus,
    IClock clock,
    IOptions<ConnectionOptions> options,
    ILogger<ConnectionSupervisor> logger)
    : IConnectionSupervisor, ITeamChatSender, IRustServerQuery, IAfkState, IAsyncDisposable
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), Handle> _connections = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), LiveSocket> _liveSockets = new();
    private readonly ConnectionOptions _options = options.Value;
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
        // ITeamChatSender, and the concrete type), so the DI container may invoke DisposeAsync more than once.
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
        await _gate.WaitAsync().ConfigureAwait(false);
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
        await _gate.WaitAsync().ConfigureAwait(false);
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
    public async Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return [];
        }

        return await live.Connection.GetMonumentsAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
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

        return await live.Connection.GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> SetSmartSwitchAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return false;
        }

        return await live.Connection
            .SetSmartSwitchValueAsync(entityId, value, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> StrobeSmartSwitchAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        int timeoutMs,
        bool value,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return false;
        }

        return await live.Connection
            .StrobeSmartSwitchAsync(entityId, timeoutMs, value, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TeamChatSendResult> SendAsync(
        ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return TeamChatSendResult.NotConnected;
        }

        try
        {
            await live.Connection.SendTeamMessageAsync(message, cancellationToken).ConfigureAwait(false);
            return TeamChatSendResult.Sent;
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
            return TeamChatSendResult.Failed;
        }
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

        var dims = await connection.GetMapDimensionsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        var rigs = await GetRigPositionsAsync(key.Server, connection, ct).ConfigureAwait(false);

#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<SmartDeviceTrigger> delegate shape.
        void OnSmartDevice(object? sender, SmartDeviceTrigger trigger)
        {
            // Fire-and-forget: PublishDeviceTriggerAsync catches everything internally, so the discarded task never
            // surfaces an unobserved exception. Device triggers are low-volume, so unbounded concurrency is fine.
            _ = PublishDeviceTriggerAsync(key, trigger);
        }
#pragma warning restore RCS1163

        var tracker = new TeamStateTracker();
        connection.TeamMessageReceived += OnTeamMessage;
        connection.SmartDeviceTriggered += OnSmartDevice;
        _liveSockets[key] = new LiveSocket(connection, activeSteamId, tracker);
        await PrimeDevicesAsync(key, connection, ct).ConfigureAwait(false);
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var markerPoll = Task.Run(() => PollMarkersAsync(key, connection, dims, rigs, tracker, pollCts.Token),
            CancellationToken.None);
        try
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
        finally
        {
            await pollCts.CancelAsync().ConfigureAwait(false);
            try
            {
#pragma warning disable VSTHRD003 // Suppress: markerPoll is owned by this connected window and explicitly joined on exit.
                await markerPoll.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }

            _liveSockets.TryRemove(key, out _);
            connection.TeamMessageReceived -= OnTeamMessage;
            connection.SmartDeviceTriggered -= OnSmartDevice;
        }
    }

    private async Task PollMarkersAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        MapDimensions? dims,
        IReadOnlyList<RigPosition> rigs,
        TeamStateTracker tracker,
        CancellationToken ct)
    {
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
                    var added = current.Where(c => previous.All(p => p.Id != c.Id)).ToList();
                    var removed = previous.Where(p => current.All(c => c.Id != p.Id)).ToList();
                    previous = current;
                    if (added.Count > 0 || removed.Count > 0)
                    {
                        await eventBus.PublishAsync(
                                new MapMarkersChangedEvent(key.Guild, key.Server, dims, added, removed), ct)
                            .ConfigureAwait(false);
                    }
                }

                await DetectRigActivationsAsync(key, current, rigs, dims, rigsInRadius, ct).ConfigureAwait(false);

                var team = await connection.GetTeamInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                var transitions = tracker.Diff(team, clock.UtcNow, _options.AfkThreshold, _options.AfkEpsilon);
                if (transitions.Count > 0)
                {
                    await eventBus.PublishAsync(
                            new PlayerStateChangedEvent(key.Guild, key.Server, dims, transitions), ct)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return; // stopping
            }
#pragma warning disable CA1031 // Broad catch: a failed poll is logged and skipped; the previous snapshot is retained.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogMarkerPollFailed(logger, ex, key.Server);
            }

            var delay = anyCh47 ? _options.MarkerPollFastInterval : _options.MarkerPollInterval;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
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
                    "oilrig_1" => RigKind.Small,
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
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a monuments-fetch failure just disables rig detection this window.
        catch (Exception ex)
#pragma warning restore CA1031
        {
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
                    token = protector.Unprotect(active.ProtectedPlayerToken);
                }
                catch (CryptographicException ex)
                {
                    LogUnreadableToken(logger, ex, active.Id);
                    await store.MarkInvalidAsync(active.Id, ct).ConfigureAwait(false);
                    await dmSender.SendAsync(
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

        await dmSender.SendAsync(
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
            await eventBus.PublishAsync(new ConnectionStatusChangedEvent(key.Guild, key.Server), ct)
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

#pragma warning disable S3267 // Not a projection: each iteration awaits with per-switch best-effort error handling.
        foreach (var sw in switches)
#pragma warning restore S3267
        {
            // Best-effort per switch: one failure must not crash the connected loop or block the heartbeat.
            try
            {
                await PublishDevicePrimeAsync(key, connection, sw.EntityId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a single switch's prime failure is logged and skipped.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDevicePrimeFailed(logger, ex, sw.EntityId, key.Server);
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
    private async Task PublishDevicePrimeAsync(
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
            var isActive = await connection
                .GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
                .ConfigureAwait(false);
            await eventBus.PublishAsync(
                    new SmartDeviceTriggeredEvent(key.Guild, key.Server, entityId, isActive ?? false),
                    _shutdown.Token)
                .ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marker poll for server {ServerId} failed.")]
    private static partial void LogMarkerPollFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message =
            "Fetching monuments for oil-rig detection on server {ServerId} failed; rig detection disabled for this connection.")]
    private static partial void LogMonumentsFetchFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relaying a message to team chat for server {ServerId} failed.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Publishing a received team message for server {ServerId} failed.")]
    private static partial void LogPublishTeamMessageFailed(ILogger logger, Exception exception, Guid serverId);

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
