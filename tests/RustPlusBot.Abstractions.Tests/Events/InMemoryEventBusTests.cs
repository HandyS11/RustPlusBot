using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests.Events;

public sealed class InMemoryEventBusTests
{
    private sealed record Ping(string Text);

    [Fact]
    public async Task Subscribe_ReceivesEventPublishedAfterSubscribing()
    {
        var bus = new InMemoryEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var stream = bus.SubscribeAsync<Ping>(cts.Token);
        await bus.PublishAsync(new Ping("hello"), cts.Token);

        var received = await FirstOrDefaultAsync(stream, cts.Token);

        Assert.NotNull(received);
        Assert.Equal("hello", received.Text);
    }

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNotThrow()
    {
        var bus = new InMemoryEventBus();
        var exception = await Record.ExceptionAsync(() => bus.PublishAsync(new Ping("nobody-listening")).AsTask());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Subscribe_OnlyReceivesItsOwnEventType()
    {
        var bus = new InMemoryEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var stream = bus.SubscribeAsync<Ping>(cts.Token);
        await bus.PublishAsync("a string, not a Ping", cts.Token);
        await bus.PublishAsync(new Ping("the-real-one"), cts.Token);

        var received = await FirstOrDefaultAsync(stream, cts.Token);

        Assert.NotNull(received);
        Assert.Equal("the-real-one", received.Text);
    }

    private static async Task<T?> FirstOrDefaultAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        return await enumerator.MoveNextAsync() ? enumerator.Current : default;
    }
}
