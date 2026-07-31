namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Consumption helpers for <see cref="IEventBus"/> subscribers.
/// </summary>
/// <remarks>
/// Every long-running consumer in this solution wraps its <c>await foreach</c> in a broad catch that logs
/// and then <em>exits</em>: an exception escaping a handler ends the subscription for the rest of the
/// process, and the feature it drives goes silently dead until the host restarts. Handlers here talk to
/// Discord and to the Rust+ server, where a timeout or a 5xx is routine rather than exceptional, so that
/// trade is never the one we want. Consuming through <c>ConsumeAsync</c> keeps the
/// subscription alive: a failed handler costs its own event and nothing more.
/// </remarks>
public static class EventBusConsumption
{
    /// <summary>
    /// Consumes every published <typeparamref name="TEvent"/>, reporting and skipping the ones whose
    /// handler throws. Only cancellation of <paramref name="cancellationToken"/> ends the subscription.
    /// </summary>
    /// <typeparam name="TEvent">The event type to consume.</typeparam>
    /// <param name="eventBus">The bus to subscribe to.</param>
    /// <param name="handle">Handles one event; its failures are reported, not propagated.</param>
    /// <param name="onHandlerFailure">Reports a handler failure (typically a log call).</param>
    /// <param name="cancellationToken">Ends the subscription when cancelled.</param>
    /// <returns>A task that completes when the subscription ends.</returns>
    public static Task ConsumeAsync<TEvent>(
        this IEventBus eventBus,
        Func<TEvent, CancellationToken, Task> handle,
        Action<Exception> onHandlerFailure,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        return eventBus.SubscribeAsync<TEvent>(cancellationToken)
            .ConsumeAsync(handle, onHandlerFailure, cancellationToken);
    }

    /// <summary>
    /// Consumes an already-established subscription, reporting and skipping the events whose handler throws.
    /// Only cancellation of <paramref name="cancellationToken"/> ends the subscription.
    /// </summary>
    /// <remarks>
    /// Take this overload when the subscription must exist before the caller returns — a hosted service that
    /// subscribes inside its background <c>Task.Run</c> is only subscribed once that task is scheduled, and
    /// every event published in the meantime is dropped. Call
    /// <see cref="IEventBus.SubscribeAsync{TEvent}"/> synchronously in <c>StartAsync</c> (it registers the
    /// subscription eagerly) and hand the stream here.
    /// </remarks>
    /// <typeparam name="TEvent">The event type being consumed.</typeparam>
    /// <param name="events">The subscription stream to drain.</param>
    /// <param name="handle">Handles one event; its failures are reported, not propagated.</param>
    /// <param name="onHandlerFailure">Reports a handler failure (typically a log call).</param>
    /// <param name="cancellationToken">Ends the subscription when cancelled.</param>
    /// <returns>A task that completes when the subscription ends.</returns>
    public static async Task ConsumeAsync<TEvent>(
        this IAsyncEnumerable<TEvent> events,
        Func<TEvent, CancellationToken, Task> handle,
        Action<Exception> onHandlerFailure,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(onHandlerFailure);

        await foreach (var evt in events.ConfigureAwait(false))
        {
            try
            {
                await handle(evt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // A real shutdown: let the caller's loop handler end it.
            }
#pragma warning disable CA1031 // Broad catch: a handler failure must cost one event, never the subscription.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                onHandlerFailure(ex);
            }
        }
    }
}
