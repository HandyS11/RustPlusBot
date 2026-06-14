using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace RustPlusBot.Abstractions.Events;

/// <summary>An unbounded, in-process <see cref="IEventBus"/> backed by channels per subscriber.</summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, Subscribers> _byType = new();

    /// <inheritdoc />
    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        if (_byType.TryGetValue(typeof(TEvent), out var subscribers))
        {
            subscribers.Publish(@event);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var subscribers = _byType.GetOrAdd(typeof(TEvent), static _ => new Subscribers());
        return subscribers.SubscribeAsync<TEvent>(cancellationToken);
    }

    private sealed class Subscribers
    {
        private readonly ConcurrentDictionary<Guid, Channel<object>> _channels = new();

        public void Publish(object @event)
        {
            foreach (var channel in _channels.Values)
            {
                channel.Writer.TryWrite(@event);
            }
        }

        /// <summary>
        /// Registers the channel eagerly (before the iterator runs) so events published
        /// immediately after SubscribeAsync() are not missed.
        /// </summary>
        /// <typeparam name="TEvent">The event type to stream.</typeparam>
        /// <param name="cancellationToken">Token that ends the subscription when cancelled.</param>
        /// <returns>An async stream of <typeparamref name="TEvent"/> instances.</returns>
        public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<object>();
            _channels[id] = channel;
            return IterateAsync<TEvent>(id, channel, cancellationToken);
        }

        private async IAsyncEnumerable<TEvent> IterateAsync<TEvent>(
            Guid id,
            Channel<object> channel,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return (TEvent)item;
                }
            }
            finally
            {
                _channels.TryRemove(id, out _);
            }
        }
    }
}
