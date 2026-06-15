using System.Text.Json;
using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace RustPlusBot.Features.Pairing.Listening;

/// <summary>Real <see cref="IPairingSource"/> backed by RustPlusApi.Fcm. Untested by unit tests (integration shim).</summary>
/// <param name="logger">The logger.</param>
internal sealed partial class RustPlusFcmPairingSource(ILogger<RustPlusFcmPairingSource> logger) : IPairingSource
{
    /// <inheritdoc />
    public IPairingListener Create(
        string fcmCredentialsJson,
        Func<PairingNotification, CancellationToken, Task> onNotification) =>
        new RustPlusFcmListener(fcmCredentialsJson, onNotification, logger);

    private sealed partial class RustPlusFcmListener : IPairingListener
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new() { PropertyNameCaseInsensitive = true };

        private readonly RustPlusFcm _fcm;
        private readonly Func<PairingNotification, CancellationToken, Task> _onNotification;
        private readonly ILogger _logger;

        [LoggerMessage(Level = LogLevel.Warning, Message = "FCM credentials rejected or connection failed.")]
        private static partial void LogConnectionFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Exception while dispatching pairing notification; it is swallowed to keep the listener alive.")]
        private static partial void LogNotificationDispatchFailed(ILogger logger, Exception ex);

        public RustPlusFcmListener(
            string fcmCredentialsJson,
            Func<PairingNotification, CancellationToken, Task> onNotification,
            ILogger logger)
        {
            var credentials = JsonSerializer.Deserialize<Credentials>(fcmCredentialsJson, JsonOptions)
                ?? throw new ArgumentException("FCM credentials JSON deserialized to null.", nameof(fcmCredentialsJson));

            _onNotification = onNotification;
            _logger = logger;
            _fcm = new RustPlusFcm(credentials, persistentIds: null, options: null, loggerFactory: null);
            _fcm.OnServerPairing += OnServerPairing;
        }

        /// <inheritdoc />
        public async Task<PairingConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await _fcm.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
                return PairingConnectOutcome.Connected;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out waiting for the connection — not the caller's CancellationToken.
                return PairingConnectOutcome.Timeout;
            }
#pragma warning disable CA1031 // Broad catch is intentional: any non-cancellation exception (auth, TLS, protocol error) maps to Rejected.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogConnectionFailed(_logger, ex);
                return PairingConnectOutcome.Rejected;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _fcm.OnServerPairing -= OnServerPairing;
            await _fcm.DisposeAsync().ConfigureAwait(false);
        }

        private void OnServerPairing(object? sender, Notification<ServerEvent?> e)
        {
            if (e?.Data is null)
            {
                return;
            }

            var notification = new PairingNotification(
                Kind: PairingKind.Server,
                ServerName: e.Data.Name ?? string.Empty,
                Ip: e.Data.Ip ?? string.Empty,
                Port: e.Data.Port,
                PlayerId: e.PlayerId,
                PlayerToken: e.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Fire-and-forget bridge from synchronous event to async callback.
            // Exception must never propagate back into the package's event dispatcher.
#pragma warning disable CA2008 // Task.Run without TaskScheduler: acceptable here; default is ThreadPool.
            _ = Task.Run(async () =>
#pragma warning restore CA2008
            {
                try
                {
                    await _onNotification(notification, CancellationToken.None).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Broad catch is intentional: an unhandled exception here would silently fault an unobserved task, crashing the process.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogNotificationDispatchFailed(_logger, ex);
                }
            });
        }
    }
}
