using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Events.Tests.Fakes;

/// <summary>
/// A real <see cref="InMemoryEventBus"/> that also says when a subscription has been established and keeps
/// every event that was published through it.
/// </summary>
/// <remarks>
/// The bus does not replay, and a hosted service that subscribes from inside a <c>Task.Run</c> is only
/// subscribed once the pool schedules that task. Awaiting <see cref="WhenSubscribedAsync{TEvent}"/> before
/// publishing removes that race, so a test can publish exactly once instead of republishing on a timer until
/// something is observed.
/// </remarks>
internal sealed class SubscriptionAwareBus : IEventBus
{
    private readonly InMemoryEventBus _inner = new();
    private readonly ConcurrentDictionary<Type, TaskCompletionSource> _subscribed = new();

    /// <summary>Every event published through this bus, in order.</summary>
    public ConcurrentQueue<object> Published { get; } = new();

    /// <inheritdoc />
    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        Published.Enqueue(@event);
        return _inner.PublishAsync(@event, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        // InMemoryEventBus registers the channel eagerly, so the subscription is live the moment this
        // returns — signalling here is enough for a caller to publish without losing the event.
        var stream = _inner.SubscribeAsync<TEvent>(cancellationToken);
        Signal(typeof(TEvent)).TrySetResult();
        return stream;
    }

    /// <summary>Completes once something has subscribed to <typeparamref name="TEvent"/>.</summary>
    /// <typeparam name="TEvent">The event type to wait for.</typeparam>
    /// <returns>A task that completes when the subscription exists.</returns>
    public Task WhenSubscribedAsync<TEvent>()
        where TEvent : notnull => Signal(typeof(TEvent)).Task;

    private TaskCompletionSource Signal(Type eventType) =>
        _subscribed.GetOrAdd(eventType,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
