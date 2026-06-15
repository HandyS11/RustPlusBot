using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Features.Pairing.Supervisor;

/// <summary>Default <see cref="IPairingSupervisor"/>: one connect-retry loop per active registration.</summary>
/// <param name="source">Creates listeners (FCM in production, a fake in tests).</param>
/// <param name="scopeFactory">Opens scopes for the scoped store and handler.</param>
/// <param name="notifier">Notifies owners when their credentials expire.</param>
/// <param name="protector">Unprotects stored credentials before connecting.</param>
/// <param name="options">Probe timeout and backoff settings.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PairingSupervisor(
    IPairingSource source,
    IServiceScopeFactory scopeFactory,
    IOwnerNotifier notifier,
    ICredentialProtector protector,
    IOptions<PairingOptions> options,
    ILogger<PairingSupervisor> logger) : IPairingSupervisor, IAsyncDisposable
{
    private readonly ConcurrentDictionary<(ulong Guild, ulong Owner), Handle> _listeners = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PairingOptions _options = options.Value;

    private sealed class Handle : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _runTask;

        public Handle(CancellationTokenSource cts, Task runTask)
        {
            _cts = cts;
            _runTask = runTask;
        }

        public async Task StopAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
#pragma warning disable VSTHRD003 // Suppress: task is owned by this Handle and explicitly joined on stop.
                await _runTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
        }

        public ValueTask DisposeAsync()
        {
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Listener loop for owner {OwnerId} faulted.")]
    private static partial void LogListenerLoopFaulted(ILogger logger, Exception exception, ulong ownerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle pairing notification for owner {OwnerId}.")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception, ulong ownerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored FCM credentials for owner {OwnerId} are unreadable.")]
    private static partial void LogUnreadableCredentials(ILogger logger, Exception exception, ulong ownerId);

    /// <inheritdoc />
    public async Task<PairingConnectOutcome> EnsureListenerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var key = (guildId, ownerUserId);
        await StopListenerAsync(key).ConfigureAwait(false);

        FcmRegistration? registration;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            registration = await store.GetAsync(guildId, ownerUserId, cancellationToken).ConfigureAwait(false);
        }

        if (registration is null || registration.Status == FcmRegistrationStatus.Disabled)
        {
            return PairingConnectOutcome.Rejected;
        }

        string credentialsJson;
        try
        {
            credentialsJson = protector.Unprotect(registration.ProtectedFcmCredentials);
        }
        catch (CryptographicException ex)
        {
            LogUnreadableCredentials(logger, ex, ownerUserId);
            await MarkExpiredAsync(key, registration.Id).ConfigureAwait(false);
            return PairingConnectOutcome.Rejected;
        }

        if (_shutdown.IsCancellationRequested)
        {
            return PairingConnectOutcome.Timeout;
        }

        var firstOutcome = new TaskCompletionSource<PairingConnectOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _listeners[key] = new Handle(
            cts,
            Task.Run(
                () => RunAsync(key, registration.Id, credentialsJson, firstOutcome, cts.Token),
                CancellationToken.None));
        return await firstOutcome.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAllActiveAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FcmRegistration> active;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            active = await store.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var registration in active)
        {
            await EnsureListenerAsync(registration.GuildId, registration.OwnerUserId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        foreach (var key in _listeners.Keys.ToList())
        {
            await StopListenerAsync(key).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task StopListenerAsync((ulong Guild, ulong Owner) key)
    {
        if (!_listeners.TryRemove(key, out var handle))
        {
            return;
        }

        await handle.StopAsync().ConfigureAwait(false);
        await handle.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(
        (ulong Guild, ulong Owner) key,
        Guid registrationId,
        string credentialsJson,
        TaskCompletionSource<PairingConnectOutcome> firstOutcome,
        CancellationToken cancellationToken)
    {
        var delay = _options.InitialRetryDelay;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var listener = source.Create(
                    credentialsJson,
                    (notification, ct) => HandleNotificationAsync(key, notification, ct));

                PairingConnectOutcome outcome;
                try
                {
                    outcome = await listener.ConnectAsync(_options.ProbeTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await listener.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                if (outcome == PairingConnectOutcome.Connected)
                {
                    firstOutcome.TrySetResult(outcome);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Stopping.
                    }
                    finally
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }

                    return;
                }

                await listener.DisposeAsync().ConfigureAwait(false);

                if (outcome == PairingConnectOutcome.Rejected)
                {
                    await MarkExpiredAsync(key, registrationId).ConfigureAwait(false);
                    firstOutcome.TrySetResult(outcome);
                    return;
                }

                // Timeout: signal the caller so they get the outcome immediately, then retry in background.
                firstOutcome.TrySetResult(outcome);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = delay < _options.MaxRetryDelay
                    ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxRetryDelay.Ticks))
                    : _options.MaxRetryDelay;
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
#pragma warning disable CA1031 // Broad catch is intentional: a faulting listener loop must not crash the host.
        catch (Exception ex)
        {
            LogListenerLoopFaulted(logger, ex, key.Owner);
        }
#pragma warning restore CA1031
        finally
        {
            firstOutcome.TrySetResult(PairingConnectOutcome.Timeout);
        }
    }

    private async Task HandleNotificationAsync(
        (ulong Guild, ulong Owner) key,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var notificationScope = scopeFactory.CreateAsyncScope();
            await using (notificationScope.ConfigureAwait(false))
            {
                var handler = notificationScope.ServiceProvider.GetRequiredService<IPairingHandler>();
                await handler.HandleAsync(key.Guild, key.Owner, notification, cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Broad catch is intentional: one bad notification must not kill the listener.
        catch (Exception ex)
        {
            LogNotificationFailed(logger, ex, key.Owner);
        }
#pragma warning restore CA1031
    }

    private async Task MarkExpiredAsync((ulong Guild, ulong Owner) key, Guid registrationId)
    {
        var expireScope = scopeFactory.CreateAsyncScope();
        await using (expireScope.ConfigureAwait(false))
        {
            var store = expireScope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            await store.SetStatusAsync(registrationId, FcmRegistrationStatus.Expired).ConfigureAwait(false);
        }

        await notifier.NotifyCredentialsExpiredAsync(key.Guild, key.Owner).ConfigureAwait(false);
    }
}
