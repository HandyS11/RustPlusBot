namespace RustPlusBot.Features.Pairing.Listening;

/// <summary>A live FCM listener for one user's credentials. Pushes pairing notifications to a callback.</summary>
internal interface IPairingListener : IAsyncDisposable
{
    /// <summary>
    /// Connects and waits up to <paramref name="timeout"/> for the first successful check-in. After a
    /// <see cref="PairingConnectOutcome.Connected"/> result the listener keeps delivering notifications
    /// to its callback until disposed.
    /// </summary>
    /// <param name="timeout">How long to wait for the initial check-in.</param>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The initial connect outcome.</returns>
    Task<PairingConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Creates <see cref="IPairingListener"/>s from credentials. The seam that abstracts FCM for tests.</summary>
internal interface IPairingSource
{
    /// <summary>Creates a listener that delivers notifications to <paramref name="onNotification"/>.</summary>
    /// <param name="fcmCredentialsJson">The plaintext FCM credentials JSON.</param>
    /// <param name="onNotification">Invoked for every pairing notification received.</param>
    /// <returns>A not-yet-connected listener.</returns>
    IPairingListener Create(
        string fcmCredentialsJson,
        Func<PairingNotification, CancellationToken, Task> onNotification);
}
