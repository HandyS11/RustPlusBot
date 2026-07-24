namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Consumption helpers for <see cref="IEventBus"/> subscribers.
/// </summary>
/// <remarks>
/// Every long-running consumer in this solution wraps its <c>await foreach</c> in a broad catch that logs
/// and then <em>exits</em>: an exception escaping a handler ends the subscription for the rest of the
/// process, and the feature it drives goes silently dead until the host restarts. Handlers here talk to
/// Discord and to the Rust+ server, where a timeout or a 5xx is routine rather than exceptional, so that
/// trade is never the one we want. Consuming through <see cref="ConsumeAsync{TEvent}"/> keeps the
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
    public static async Task ConsumeAsync<TEvent>(
        this IEventBus eventBus,
        Func<TEvent, CancellationToken, Task> handle,
        Action<Exception> onHandlerFailure,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(onHandlerFailure);

        await foreach (var evt in eventBus.SubscribeAsync<TEvent>(cancellationToken).ConfigureAwait(false))
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
