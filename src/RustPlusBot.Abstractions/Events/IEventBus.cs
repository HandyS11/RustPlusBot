namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// A lightweight in-process publish/subscribe backbone. Producers (the connection
/// manager, FCM listener) publish; feature modules subscribe. Swappable for an
/// external broker later without touching producers or consumers.
/// </summary>
public interface IEventBus
{
    /// <summary>Publishes an event to every live subscriber of <typeparamref name="TEvent"/>.</summary>
    /// <typeparam name="TEvent">The event type to publish.</typeparam>
    /// <param name="event">The event instance to broadcast.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    /// Subscribes to <typeparamref name="TEvent"/>. The returned stream yields every event
    /// published after this call until <paramref name="cancellationToken"/> is cancelled or
    /// enumeration stops.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="cancellationToken">Token that ends the subscription when cancelled.</param>
    /// <returns>An async stream of <typeparamref name="TEvent"/> instances.</returns>
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
