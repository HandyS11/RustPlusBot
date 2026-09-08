using Microsoft.Extensions.Logging.Abstractions;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;

namespace RustPlusBot.Abstractions.Tests.Hosting;

public sealed class EventLoopHostedServiceTests
{
    [Fact]
    public async Task StartAsync_SubscribesEagerly_SoEventsPublishedImmediatelyAfterStartAreNotDropped()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);

        // No delay, no polling before publishing: this is the regression this base class exists to prevent.
        await bus.PublishAsync(new Ping(1));

        await WaitForAsync(() => subject.Pings.Count == 1);
        Assert.Equal([1], subject.Pings);
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EveryDeclaredLoop_Runs()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);
        await bus.PublishAsync(new Ping(1));
        await bus.PublishAsync(new Pong(2));

        await WaitForAsync(() => subject.Pings.Count == 1 && subject.Pongs.Count == 1);
        Assert.Equal([1], subject.Pings);
        Assert.Equal([2], subject.Pongs);
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AThrowingHandler_CostsOneEvent_NotTheSubscription()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus)
        {
            OnPing = e => e.Value == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask,
        };
        await subject.StartAsync(CancellationToken.None);

        await bus.PublishAsync(new Ping(1)); // throws
        await bus.PublishAsync(new Ping(2)); // must still be handled

        await WaitForAsync(() => subject.Pings.Count == 1);
        Assert.Equal([2], subject.Pings);
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_JoinsEveryLoop_AndDoesNotThrowOnCancellation()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);

        var stop = subject.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
        subject.Dispose();
    }

    [Fact]
    public async Task StopAsync_IsSafe_WhenStartAsyncWasNeverCalled()
    {
        var subject = new Subject(new InMemoryEventBus());

        var stop = subject.StopAsync(CancellationToken.None);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OnStartingAndOnStopping_AreInvokedExactlyOnce()
    {
        var bus = new InMemoryEventBus();
        var subject = new Subject(bus);
        await subject.StartAsync(CancellationToken.None);
        await subject.StopAsync(CancellationToken.None);
        Assert.Equal(1, subject.StartingCalls);
        Assert.Equal(1, subject.StoppingCalls);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }

    private sealed record Ping(int Value);

    private sealed record Pong(int Value);

    private sealed class Subject(IEventBus bus) : EventLoopHostedService(bus, NullLogger.Instance)
    {
        public List<int> Pings { get; } = [];

        public List<int> Pongs { get; } = [];

        public int StartingCalls { get; private set; }

        public int StoppingCalls { get; private set; }

        public Func<Ping, Task>? OnPing { get; set; }

        protected override IEnumerable<EventLoopRegistration> Loops =>
        [
            Loop<Ping>("ping", async (e, _) =>
            {
                if (OnPing is not null)
                {
                    await OnPing(e).ConfigureAwait(false);
                }

                Pings.Add(e.Value);
            }),
            Loop<Pong>("pong", (e, _) =>
            {
                Pongs.Add(e.Value);
                return Task.CompletedTask;
            }),
        ];

        protected override void OnStarting() => StartingCalls++;

        protected override void OnStopping() => StoppingCalls++;
    }
}
