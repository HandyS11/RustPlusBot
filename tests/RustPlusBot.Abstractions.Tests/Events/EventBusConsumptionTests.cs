using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests.Events;

public sealed class EventBusConsumptionTests
{
    [Fact]
    public async Task A_failing_handler_costs_its_own_event_only()
    {
        var handled = new List<int>();
        var failures = new List<Exception>();
        using var cts = new CancellationTokenSource();

        var consume = new ScriptedBus(new Ping(1), new Ping(2), new Ping(3)).ConsumeAsync<Ping>(
            (evt, _) =>
            {
                handled.Add(evt.Id);
                return evt.Id == 1 ? throw new TimeoutException("The operation has timed out.") : Task.CompletedTask;
            },
            failures.Add,
            cts.Token);

        while (handled.Count < 3)
        {
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consume);
        Assert.Equal([1, 2, 3], handled);
        Assert.Single(failures);
        Assert.IsType<TimeoutException>(failures[0]);
    }

    [Fact]
    public async Task Cancellation_ends_the_subscription()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var consume = new ScriptedBus(new Ping(1)).ConsumeAsync<Ping>(
            (_, _) => Task.CompletedTask, _ => Assert.Fail("no handler failure expected"), cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consume);
    }

    private sealed record Ping(int Id);

    /// <summary>A bus whose subscription replays a fixed sequence, then blocks until cancelled.</summary>
    /// <param name="events">The events to yield, in order.</param>
    private sealed class ScriptedBus(params Ping[] events) : IEventBus
    {
        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : notnull => ValueTask.CompletedTask;

        public async IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            foreach (var evt in events.Cast<TEvent>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return evt;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
    }
}
