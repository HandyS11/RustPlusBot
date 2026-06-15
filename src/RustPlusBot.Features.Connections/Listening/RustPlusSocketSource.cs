using System.Globalization;
using Microsoft.Extensions.Logging;
using RustPlusApi;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Real <see cref="IRustSocketSource"/> backed by RustPlusApi. Untested integration shim.</summary>
/// <param name="logger">The logger.</param>
internal sealed partial class RustPlusSocketSource(ILogger<RustPlusSocketSource> logger) : IRustSocketSource
{
    /// <inheritdoc />
    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        if (!int.TryParse(playerToken, CultureInfo.InvariantCulture, out var token))
        {
            LogInvalidToken(logger);
            return new RejectedConnection();
        }

        return new RustPlusServerConnection(ip, port, steamId, token, logger);
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

        public Task SendTeamMessageAsync(string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public event EventHandler<TeamChatLine>? TeamMessageReceived
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

        public RustPlusServerConnection(string ip, int port, ulong steamId, int playerToken, ILogger logger)
        {
            _logger = logger;
            // CONFIRMED: RustPlusConnection(string Server, int Port, ulong PlayerId, int PlayerToken, bool UseFacepunchProxy).
            var connection = new RustPlusConnection(ip, port, steamId, playerToken, UseFacepunchProxy: false);
            _rustPlus = new RustPlus(connection);
            _rustPlus.OnTeamChatReceived += OnTeamChatReceived;
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

        public event EventHandler<TeamChatLine>? TeamMessageReceived;

        public async Task SendTeamMessageAsync(string message, CancellationToken cancellationToken)
        {
            // CONFIRMED: SendTeamMessageAsync(string, CancellationToken) in 2.0.0-beta.1 returns Task<Response<T>>.
            // Awaiting it discards the response; the interface contract is bare Task.
            await _rustPlus.SendTeamMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        private void OnTeamChatReceived(object? sender, RustPlusApi.Data.Events.TeamMessageEventArg e) =>
            TeamMessageReceived?.Invoke(this, new TeamChatLine(e.SteamId, e.Name, e.Message));

        public async ValueTask DisposeAsync()
        {
            _rustPlus.OnTeamChatReceived -= OnTeamChatReceived;
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

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ socket connect failed.")]
        private static partial void LogConnectFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ heartbeat (GetInfo) failed.")]
        private static partial void LogHeartbeatFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ socket teardown failed.")]
        private static partial void LogDisposeFailed(ILogger logger, Exception ex);
    }
}
