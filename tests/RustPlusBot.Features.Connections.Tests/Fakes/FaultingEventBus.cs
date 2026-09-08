using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Connections.Tests.Fakes;

/// <summary>
/// An <see cref="IEventBus"/> that fails the publish of selected event types and delegates everything else
/// to a real <see cref="InMemoryEventBus"/>. Models a consumer-side bus failure, so tests can pin that a
/// socket callback logs and swallows it instead of letting it escape into the callback.
/// </summary>
/// <param name="shouldFail">Decides, per event type, whether the publish fails.</param>
/// <param name="fault">Produces the exception a failing publish reports.</param>
internal sealed class FaultingEventBus(Func<Type, bool> shouldFail, Func<Exception> fault) : IEventBus
{
    private readonly InMemoryEventBus _inner = new();

    /// <inheritdoc />
    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : notnull =>
        shouldFail(typeof(TEvent))
            ? ValueTask.FromException(fault())
            : _inner.PublishAsync(@event, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull => _inner.SubscribeAsync<TEvent>(cancellationToken);
}
