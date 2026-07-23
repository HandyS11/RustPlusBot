using System.Globalization;
using Microsoft.Extensions.Logging;
using RustPlusApi;
using RustPlusApi.Data.Events;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Real <see cref="IRustSocketSource"/> backed by RustPlusApi. Untested integration shim.</summary>
/// <param name="logger">The logger.</param>
/// <param name="loggerFactory">Routes RustPlusApi's own diagnostics (e.g. mapping failures) into the
/// host's logging stack; without it the library logs to a <c>NullLogger</c> and every client-side
/// failure it downgrades to a failed response is invisible.</param>
internal sealed partial class RustPlusSocketSource(
    ILogger<RustPlusSocketSource> logger,
    ILoggerFactory loggerFactory) : IRustSocketSource
{
    /// <inheritdoc />
    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        if (!int.TryParse(playerToken, CultureInfo.InvariantCulture, out var token))
        {
            LogInvalidToken(logger);
            return new RejectedConnection();
        }

        return new RustPlusServerConnection(ip, port, steamId, token, logger, loggerFactory);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Player token is not a valid numeric token; treating the credential as rejected.")]
    private static partial void LogInvalidToken(ILogger logger);

    /// <summary>Returned when the player token is unusable; reports the credential as rejected and does nothing else.</summary>
    private sealed class RejectedConnection : IRustServerConnection
    {
        public Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(SocketConnectOutcome.AuthRejected);

        public Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(HeartbeatResult.AuthRejected);

        public Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<ServerInfoSnapshot?>(null);

        public Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<ServerTimeSnapshot?>(null);

        public Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<TeamInfoSnapshot?>(null);

        public Task SendTeamMessageAsync(string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(ClanProbeResult.Unavailable);

        public Task SendClanMessageAsync(string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId,
            SmartDeviceKind kind,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceReading(null, DeviceReachability.NoResponse));

        public Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StorageReading(null, DeviceReachability.NoResponse));

        public Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeviceReachability.NoResponse);

        public Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId,
            int timeoutMs,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeviceReachability.NoResponse);

        public Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MapMarkerSnapshot>>([]);

        public Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MapDimensions?>(null);

        public Task<WorldSnapshot?> GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorldSnapshot?>(null);

        public Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MonumentSnapshot>>([]);

        public Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public event EventHandler<TeamChatLine>? TeamMessageReceived
        {
            add { _ = value; }
            remove { _ = value; }
        }

        public event EventHandler<ClanChatLine>? ClanMessageReceived
        {
            add { _ = value; }
            remove { _ = value; }
        }

        public event EventHandler<ClanProbeResult>? ClanChanged
        {
            add { _ = value; }
            remove { _ = value; }
        }

        public event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered
        {
            add { _ = value; }
            remove { _ = value; }
        }

        public event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered
        {
            add { _ = value; }
            remove { _ = value; }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// One live Rust+ connection backed by <see cref="RustPlus"/>.
    /// <para>API notes (2.0.0-beta.1): constructor takes <see cref="RustPlusConnection"/> instead of
    /// individual parameters; <see cref="RustPlusSocket"/> is <see cref="IAsyncDisposable"/>;
    /// <c>ConnectAsync</c> accepts a <see cref="System.Threading.CancellationToken"/> and throws on failure;
    /// <c>GetInfoAsync</c> returns <c>Response&lt;ServerInfo?&gt;</c> with <c>ServerInfo.PlayerCount</c>
    /// typed as <c>uint?</c>.</para>
    /// </summary>
    private sealed partial class RustPlusServerConnection : IRustServerConnection
    {
        private readonly ILogger _logger;
        private readonly RustPlus _rustPlus;

        public RustPlusServerConnection(string ip,
            int port,
            ulong steamId,
            int playerToken,
            ILogger logger,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            // CONFIRMED: RustPlusConnection(string Server, int Port, ulong PlayerId, int PlayerToken, bool UseFacepunchProxy).
            var connection = new RustPlusConnection(ip, port, steamId, playerToken, UseFacepunchProxy: false);
            _rustPlus = new RustPlus(connection, loggerFactory: loggerFactory);
            _rustPlus.OnTeamChatReceived += OnTeamChatReceived;
            _rustPlus.OnSmartDeviceTriggered += OnSmartDeviceTriggered;
            _rustPlus.OnStorageMonitorTriggered += OnStorageMonitorTriggered;
            _rustPlus.OnClanChatReceived += OnClanChatReceived;
            _rustPlus.OnClanChanged += OnClanChanged;
        }

        public async Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnConnected(object? sender, EventArgs e) => connected.TrySetResult();

            // CONFIRMED: event EventHandler? Connected — fires synchronously inside ConnectAsync before it returns.
            _rustPlus.Connected += OnConnected;
            try
            {
                // CONFIRMED: ConnectAsync(CancellationToken) accepts a token; throws WebSocketException on
                // failure (v1.x swallowed into ErrorOccurred; v2.x throws). The Connected event fires
                // synchronously inside ConnectAsync on success, so connected.Task may already be completed.
                await _rustPlus.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
                await connected.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return SocketConnectOutcome.Connected;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return SocketConnectOutcome.Unreachable;
            }
#pragma warning disable CA1031 // Broad catch: any non-cancellation connect failure maps to Unreachable; auth is probed by the first heartbeat.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogConnectFailed(_logger, ex);
                return SocketConnectOutcome.Unreachable;
            }
            finally
            {
                _rustPlus.Connected -= OnConnected;
            }
        }

        public async Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED: GetInfoAsync(CancellationToken) returns Task<Response<ServerInfo?>> in 2.0.0-beta.1.
                // Response<T>.IsSuccess and Response<T>.Data are the accessors.
                // CONFIRMED: ServerInfo.PlayerCount is uint? (not int, not .Players).
                // .WaitAsync guarantees we return within the timeout even if GetInfoAsync doesn't internally honor the token (unverified beta).
                var response = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    // VERIFY: detect the specific auth/token-rejected error shape from response or error message
                    // and return HeartbeatResult.AuthRejected once confirmed. Conservative default: Unreachable.
                    return HeartbeatResult.Unreachable;
                }

                var playerCount = (int)(response.Data?.PlayerCount ?? 0u);
                return HeartbeatResult.Ok(playerCount);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HeartbeatResult.Unreachable;
            }
#pragma warning disable CA1031 // Broad catch: classify an info failure. VERIFY: detect the auth/token-rejected error and return AuthRejected.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogHeartbeatFailed(_logger, ex);
                // Conservative default: treat as unreachable. Map the specific "not authorized / invalid token"
                // RustPlusApi error to HeartbeatResult.AuthRejected once its shape is confirmed.
                return HeartbeatResult.Unreachable;
            }
        }

        public async Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetInfoAsync returns Task<Response<ServerInfo?>>; Response.IsSuccess/.Data.
                // ServerInfo getters: PlayerCount/MaxPlayerCount/QueuedPlayerCount are uint?; WipeTime is DateTime?
                // documented as "UTC time of the last forced wipe" (the AppInfo->ServerInfo mapper yields Kind=Utc).
                var response = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    return null;
                }

                var info = response.Data;
                DateTimeOffset? wipeTime = info.WipeTime is { } wipe
                    ? new DateTimeOffset(DateTime.SpecifyKind(wipe, DateTimeKind.Utc))
                    : null;
                return new ServerInfoSnapshot(
                    (int)(info.PlayerCount ?? 0u),
                    (int)(info.MaxPlayerCount ?? 0u),
                    (int)(info.QueuedPlayerCount ?? 0u),
                    wipeTime);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any info-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }

        public async Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetTimeAsync returns Task<Response<TimeInfo?>>; Response.IsSuccess/.Data.
                // TimeInfo getters Time/Sunrise/Sunset are float. 'Time' is the current in-game time of day.
                var response = await _rustPlus.GetTimeAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    return null;
                }

                var time = response.Data;
                return new ServerTimeSnapshot(time.Time, time.Sunrise, time.Sunset);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any time-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }

        public async Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetTeamInfoAsync returns Task<Response<TeamInfo?>>; Response.IsSuccess/.Data.
                // TeamInfo.LeaderSteamId (ulong), TeamInfo.Members (IEnumerable<MemberInfo>?).
                // MemberInfo: SteamId/Name?/X/Y/IsOnline/IsAlive/LastSpawnTime(DateTime,UTC)/LastDeathTime(DateTime,UTC).
                var response = await _rustPlus.GetTeamInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    return null;
                }

                var members = (response.Data.Members ?? [])
                    .Select(m => new TeamMemberSnapshot(
                        m.SteamId,
                        m.Name ?? string.Empty,
                        m.X,
                        m.Y,
                        m.IsOnline,
                        m.IsAlive,
                        new DateTimeOffset(DateTime.SpecifyKind(m.LastSpawnTime, DateTimeKind.Utc)),
                        new DateTimeOffset(DateTime.SpecifyKind(m.LastDeathTime, DateTimeKind.Utc))))
                    .ToList();
                var deathNote = response.Data.DeathNote is { } dn
                    ? ((float X, float Y)?)(dn.X, dn.Y)
                    : null;
                return new TeamInfoSnapshot(response.Data.LeaderSteamId, members, deathNote);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any team-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }

        public event EventHandler<TeamChatLine>? TeamMessageReceived;

        public event EventHandler<ClanChatLine>? ClanMessageReceived;

        public event EventHandler<ClanProbeResult>? ClanChanged;

        public async Task SendTeamMessageAsync(string message, CancellationToken cancellationToken)
        {
            // CONFIRMED: SendTeamMessageAsync(string, CancellationToken) in 2.0.0-beta.1 returns Task<Response<T>>.
            // Awaiting it discards the response; the interface contract is bare Task.
            // Intentional: send failures propagate to the caller (the supervisor classifies them), unlike the broad-catch probes.
            await _rustPlus.SendTeamMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.4): GetClanInfoAsync returns Task<Response<ClanInfo>>, exposing
                // IsSuccess, Data, and Error?.Code as the response accessors.
                // .WaitAsync guards the timeout even if the beta call doesn't internally honor the token
                // (matching the other probes): on a dead socket this must never block the connect path.
                var response = await _rustPlus.GetClanInfoAsync(timeoutCts.Token)
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return ClanMapping.FromResponse(response.IsSuccess, response.Error?.Code, response.Data);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ClanProbeResult.Unavailable;
            }
#pragma warning disable CA1031 // Broad catch: a failed clan probe must degrade to Unavailable, never crash the caller.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return ClanProbeResult.Unavailable;
            }
        }

        public async Task SendClanMessageAsync(string message, CancellationToken cancellationToken)
        {
            // Intentional: send failures propagate to the caller (the supervisor classifies them).
            await _rustPlus.SendClanMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                var response = await _rustPlus.SetClanMotdAsync(motd, timeoutCts.Token).ConfigureAwait(false);
                return response.IsSuccess;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
#pragma warning disable CA1031 // Broad catch: a failed MOTD write is reported to the user, not thrown.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return false;
            }
        }

        public async Task<bool> PromoteToLeaderAsync(ulong steamId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): PromoteToLeaderAsync(ulong, CancellationToken) returns a payload-free
                // Task<Response>; Response.IsSuccess indicates the outcome.
                var response = await _rustPlus.PromoteToLeaderAsync(steamId, timeoutCts.Token)
                    .WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                return response.IsSuccess;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
#pragma warning disable CA1031 // Broad catch: any promote failure maps to false; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return false;
            }
        }

        public event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;

        public event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;

        public async Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId,
            SmartDeviceKind kind,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.3): GetSmartSwitchInfoAsync/GetAlarmInfoAsync(ulong, CancellationToken)
                // return Task of Response of SmartDeviceInfo; Response.IsSuccess and Response.Data are the accessors
                // and SmartDeviceInfo.IsActive is a bool. The call also primes the socket's interest in this
                // entity, so OnSmartDeviceTriggered fires for it thereafter.
                // CONFIRMED: both mappers throw InvalidOperationException when the server-reported entity type
                // does not match the method (AppEntityInfoToModel type checks), so the paired kind must pick
                // the matching read — a switch read against an alarm surfaces as NoResponse otherwise.
                // CONFIRMED: Response.Error is ErrorMessage? (null on success), so response.Error?.Code is correct.
                var response = kind == SmartDeviceKind.Alarm
                    ? await _rustPlus.GetAlarmInfoAsync(entityId, timeoutCts.Token)
                        .WaitAsync(timeoutCts.Token).ConfigureAwait(false)
                    : await _rustPlus.GetSmartSwitchInfoAsync(entityId, timeoutCts.Token)
                        .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                var reachability = ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
                var isActive = response is { IsSuccess: true, Data: { } info } ? info.IsActive : (bool?)null;
                return new DeviceReading(isActive, reachability);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new DeviceReading(null, DeviceReachability.NoResponse);
            }
#pragma warning disable CA1031 // Broad catch: a failed read maps to NoResponse; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return new DeviceReading(null, DeviceReachability.NoResponse);
            }
        }

        /// <inheritdoc />
        public async Task<StorageReading> GetStorageMonitorInfoAsync(
            ulong entityId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.3): GetStorageMonitorInfoAsync(ulong, CancellationToken) returns
                // Task<Response<StorageMonitorInfo?>>; the read also primes the entity so OnStorageMonitorTriggered
                // fires for it thereafter.
                // CONFIRMED: Response.Error is ErrorMessage? (null on success), so response.Error?.Code is correct.
                var response = await _rustPlus.GetStorageMonitorInfoAsync(entityId, timeoutCts.Token)
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                var reachability = ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
                var contents = response is { IsSuccess: true, Data: { } info } ? MapContents(info) : null;
                return new StorageReading(contents, reachability);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new StorageReading(null, DeviceReachability.NoResponse);
            }
#pragma warning disable CA1031 // Broad catch: a failed read maps to NoResponse; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return new StorageReading(null, DeviceReachability.NoResponse);
            }
        }

        public async Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.3): SetSmartSwitchValueAsync(ulong, bool, CancellationToken) returns
                // Task of Response of SmartDeviceInfo; Response.IsSuccess indicates the outcome.
                // CONFIRMED: Response.Error is ErrorMessage? (null on success), so response.Error?.Code is correct.
                var response = await _rustPlus.SetSmartSwitchValueAsync(entityId, value, timeoutCts.Token)
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DeviceReachability.NoResponse;
            }
#pragma warning disable CA1031 // Broad catch: a failed set maps to NoResponse; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return DeviceReachability.NoResponse;
            }
        }

        public async Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId,
            int timeoutMs,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.3): StrobeSmartSwitchAsync(ulong, int timeoutMs, bool value, CancellationToken)
                // returns Task of Response of SmartDeviceInfo; Response.IsSuccess indicates the outcome.
                // CONFIRMED: Response.Error is ErrorMessage? (null on success), so response.Error?.Code is correct.
                var response = await _rustPlus.StrobeSmartSwitchAsync(entityId, timeoutMs, value, timeoutCts.Token)
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DeviceReachability.NoResponse;
            }
#pragma warning disable CA1031 // Broad catch: a failed strobe maps to NoResponse; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return DeviceReachability.NoResponse;
            }
        }

        public async Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            // CONFIRMED (2.0.0-beta.1): GetMapMarkersAsync returns Task<Response<RustPlusApi.Data.MapMarkers>>.
            // MapMarkers has typed dictionaries CargoShipMarkers/PatrolHelicopterMarkers/Ch47Markers/TravellingVendorMarkers
            // (Dictionary<ulong, XMarker>); each marker exposes Nullable<ulong> Id, Nullable<float> X/Y.
            // No flat list, no Type field, NO crate bucket (the game stopped sending crate markers) → core-3.
            // Surfaced buckets: CargoShip, PatrolHelicopter, Chinook, TravellingVendor.
            var response = await _rustPlus.GetMapMarkersAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                throw new InvalidOperationException("GetMapMarkers returned no data.");
            }

            var data = response.Data;
            var markers = new List<MapMarkerSnapshot>();
            // RustPlusApi 2.0.0-beta.4 declares Rotation independently on each concrete marker record
            // (CargoShipMarker/PatrolHelicopterMarker/Ch47Marker/TravellingVendorMarker) rather than on
            // the shared base Marker, so the selector is resolved per call site via type inference.
            AddMarkers(markers, data.CargoShipMarkers, MarkerKind.CargoShip, m => m.Rotation);
            AddMarkers(markers, data.PatrolHelicopterMarkers, MarkerKind.PatrolHelicopter, m => m.Rotation);
            AddMarkers(markers, data.Ch47Markers, MarkerKind.Chinook, m => m.Rotation);
            AddMarkers(markers, data.TravellingVendorMarkers, MarkerKind.TravellingVendor, m => m.Rotation);
            return markers;
        }

        public async Task<MapDimensions?> GetMapDimensionsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetMapAsync returns Task<Response<RustPlusApi.Data.ServerMap>>.
                // ServerMap has Nullable<uint> Width/Height, Nullable<int> OceanMargin, JpgImage, Monuments.
                // 2a uses dims only; if any dim is null, treat the whole thing as unavailable (return null).
                var response = await _rustPlus.GetMapAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    return null;
                }

                var map = response.Data;
                if (map.Width is not { } width || map.Height is not { } height || map.OceanMargin is not { } margin)
                {
                    return null;
                }

                var infoResponse = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!infoResponse.IsSuccess || infoResponse.Data?.MapSize is not { } worldSize)
                {
                    return null;
                }

                return new MapDimensions(width, height, margin, worldSize);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any map-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }

        public async Task<WorldSnapshot?> GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                var response = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is not { MapSize: { } size, Seed: { } seed })
                {
                    return null;
                }

                return new WorldSnapshot(size, seed);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any map-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }

        public async Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            // CONFIRMED (2.0.0-beta.1): GetMapAsync returns Task<Response<RustPlusApi.Data.ServerMap>>.
            // ServerMap.Monuments is List<ServerMapMonument> with Name (= protobuf token, e.g. "oil_rig_small"),
            // Nullable<float> X/Y. We surface (token, x, y) and skip monuments with incomplete coordinates.
            var response = await _rustPlus.GetMapAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                throw new InvalidOperationException("GetMap returned no data.");
            }

            var monuments = new List<MonumentSnapshot>();
            foreach (var m in response.Data.Monuments ?? [])
            {
                if (m.Name is null || m.X is not { } x || m.Y is not { } y)
                {
                    continue;
                }

                monuments.Add(new MonumentSnapshot(m.Name, x, y));
            }

            return monuments;
        }

        public async Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetMapAsync -> Response<ServerMap>; ServerMap.JpgImage is byte[] (raw JPEG bytes).
                var response = await _rustPlus.GetMapAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                return response.IsSuccess ? response.Data?.JpgImage : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any map-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _rustPlus.OnTeamChatReceived -= OnTeamChatReceived;
            _rustPlus.OnSmartDeviceTriggered -= OnSmartDeviceTriggered;
            _rustPlus.OnStorageMonitorTriggered -= OnStorageMonitorTriggered;
            _rustPlus.OnClanChatReceived -= OnClanChatReceived;
            _rustPlus.OnClanChanged -= OnClanChanged;
            try
            {
                // CONFIRMED: RustPlusSocket implements IAsyncDisposable in 2.0.0-beta.1.
                // DisposeAsync() is preferred over DisconnectAsync() for teardown.
                await _rustPlus.DisposeAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Broad catch: teardown failures must not throw from disposal.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDisposeFailed(_logger, ex);
            }
        }

        private void OnSmartDeviceTriggered(object? sender, RustPlusApi.Data.Events.SmartDeviceEventArg e) =>
            SmartDeviceTriggered?.Invoke(this, new SmartDeviceTrigger(e.Id, e.IsActive));

        private void OnStorageMonitorTriggered(object? sender, RustPlusApi.Data.Events.StorageMonitorEventArg e) =>
            StorageMonitorTriggered?.Invoke(this, new StorageMonitorTrigger(e.Id, MapContents(e)));

        private static StorageContentsSnapshot MapContents(RustPlusApi.Data.Entities.StorageMonitorInfo info)
        {
            var items = info.Items is null
                ? (IReadOnlyList<StorageItemSnapshot>)[]
                :
                [
                    .. info.Items.Select(i =>
                        new StorageItemSnapshot(i.Id, i.Quantity ?? 0, i.IsItemBlueprint ?? false))
                ];

            DateTimeOffset? expiry = info.HasProtection == true
                ? new DateTimeOffset(DateTime.SpecifyKind(info.ProtectionExpiry, DateTimeKind.Utc))
                : null;

            return new StorageContentsSnapshot(info.Capacity, info.HasProtection, expiry, items);
        }

        private static void AddMarkers<TMarker>(
            List<MapMarkerSnapshot> into,
            IReadOnlyDictionary<ulong, TMarker> source,
            MarkerKind kind,
            Func<TMarker, float?> rotationSelector)
            where TMarker : RustPlusApi.Data.Markers.Marker
        {
            foreach (var (id, marker) in source)
            {
                // Skip markers with incomplete coordinates rather than placing a phantom at the origin,
                // which would otherwise diff as a spurious spawn/despawn at (0, 0).
                if (marker.X is not { } x || marker.Y is not { } y)
                {
                    continue;
                }

                into.Add(new MapMarkerSnapshot(id, kind, x, y, Name: null, Rotation: rotationSelector(marker)));
            }
        }

        private void OnTeamChatReceived(object? sender, RustPlusApi.Data.Events.TeamMessageEventArg e) =>
            TeamMessageReceived?.Invoke(this, new TeamChatLine(e.SteamId, e.Name, e.Message));

        private void OnClanChatReceived(object? sender, ClanMessageEventArg e) =>
            ClanMessageReceived?.Invoke(this,
                new ClanChatLine(e.SteamId, e.Name ?? string.Empty, e.Message ?? string.Empty,
                    new DateTimeOffset(DateTime.SpecifyKind(e.Time, DateTimeKind.Utc), TimeSpan.Zero)));

        private void OnClanChanged(object? sender, ClanChangedEventArg e) =>
            ClanChanged?.Invoke(this,
                e.ClanInfo is { } info ? ClanProbeResult.From(ClanMapping.ToSnapshot(info)) : ClanProbeResult.NoClan);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ socket connect failed.")]
        private static partial void LogConnectFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ heartbeat (GetInfo) failed.")]
        private static partial void LogHeartbeatFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ server query failed.")]
        private static partial void LogQueryFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ socket teardown failed.")]
        private static partial void LogDisposeFailed(ILogger logger, Exception ex);
    }
}
