using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Hosting;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Connections.Tests.Fakes;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ConnectionHostedServiceTests
{
    [Fact]
    public async Task Startup_CallsStartAll_And_ServerRegistered_EnsuresAConnection()
    {
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var bus = new InMemoryEventBus();
        var service = new ConnectionHostedService(supervisor, bus, NullLogger<ConnectionHostedService>.Instance);

        await service.StartAsync(default);

        var serverId = Guid.NewGuid();
        // The consumer subscribes on a background loop after StartAllAsync; the bus does not replay,
        // so re-publish until the consumer has processed at least one delivery (or the deadline hits).
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !supervisor.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IConnectionSupervisor.EnsureConnectionAsync)))
        {
            await bus.PublishAsync(new ServerRegisteredEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await supervisor.Received().StartAllAsync(Arg.Any<CancellationToken>());
        await supervisor.Received().EnsureConnectionAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
        await supervisor.Received(1).StopAllAsync();
    }

    [Fact]
    public async Task ServerCredentialsChanged_EnsuresAConnection()
    {
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var bus = new InMemoryEventBus();
        var service = new ConnectionHostedService(supervisor, bus, NullLogger<ConnectionHostedService>.Instance);

        await service.StartAsync(default);

        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !supervisor.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IConnectionSupervisor.EnsureConnectionAsync)))
        {
            await bus.PublishAsync(new ServerCredentialsChangedEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await supervisor.Received().EnsureConnectionAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }

    [Fact]
    public async Task A_failing_EnsureConnection_costs_its_own_event_and_not_the_subscription()
    {
        // Connecting talks to the Rust+ server, where a refused or timed-out socket is routine. Letting that
        // escape the consumer would end the subscription, and every later /server add would silently do
        // nothing until the bot restarted.
        var attempts = 0;
        var supervisor = Substitute.For<IConnectionSupervisor>();
        supervisor.EnsureConnectionAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempts) == 1
                ? throw new TimeoutException("The Rust+ server did not answer.")
                : Task.CompletedTask);

        var bus = new InMemoryEventBus();
        using var service = new ConnectionHostedService(supervisor, bus,
            NullLogger<ConnectionHostedService>.Instance);
        await service.StartAsync(default);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref attempts) < 2)
        {
            await bus.PublishAsync(new ServerRegisteredEvent(10UL, Guid.NewGuid()));
            await Task.Delay(20);
        }

        await service.StopAsync(default);

        Assert.True(Volatile.Read(ref attempts) >= 2,
            $"the consumer stopped after the first connect threw (attempts: {attempts})");
    }

    [Fact]
    public async Task A_failing_credentials_reconnect_costs_its_own_event_and_not_the_subscription()
    {
        var attempts = 0;
        var supervisor = Substitute.For<IConnectionSupervisor>();
        supervisor.EnsureConnectionAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempts) == 1
                ? throw new TimeoutException("The Rust+ server did not answer.")
                : Task.CompletedTask);

        var bus = new InMemoryEventBus();
        using var service = new ConnectionHostedService(supervisor, bus,
            NullLogger<ConnectionHostedService>.Instance);
        await service.StartAsync(default);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref attempts) < 2)
        {
            await bus.PublishAsync(new ServerCredentialsChangedEvent(10UL, Guid.NewGuid()));
            await Task.Delay(20);
        }

        await service.StopAsync(default);

        Assert.True(Volatile.Read(ref attempts) >= 2,
            $"the consumer stopped after the first reconnect threw (attempts: {attempts})");
    }

    [Fact]
    public async Task A_failing_StartAll_is_logged_and_still_lets_the_host_shut_down()
    {
        // StartAllAsync runs on a background task nothing awaits, so an escaping exception would surface
        // only as an unobserved fault — with the host reporting a clean start.
        var boom = new InvalidOperationException("The credential store is unavailable.");
        var supervisor = Substitute.For<IConnectionSupervisor>();
        supervisor.StartAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(boom);

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        await using var provider = services.BuildServiceProvider();

        using var service = new ConnectionHostedService(supervisor, new InMemoryEventBus(),
            provider.GetRequiredService<ILogger<ConnectionHostedService>>());
        await service.StartAsync(default);
        await service.StopAsync(default);

        Assert.Contains(logs.Records, r => ReferenceEquals(r.Exception, boom));
        await supervisor.Received(1).StopAllAsync();
    }

#pragma warning disable S2699 // The implicit assertion is "no exception is thrown and nothing is logged".
    [Fact]
    public async Task Cancellation_during_startup_is_shutdown_rather_than_a_fault()
    {
        var supervisor = Substitute.For<IConnectionSupervisor>();
        supervisor.StartAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        await using var provider = services.BuildServiceProvider();

        using var service = new ConnectionHostedService(supervisor, new InMemoryEventBus(),
            provider.GetRequiredService<ILogger<ConnectionHostedService>>());
        await service.StartAsync(default);
        await service.StopAsync(default);

        Assert.DoesNotContain(logs.Records, r => r.Level == LogLevel.Error);
    }
#pragma warning restore S2699

    [Fact]
    public async Task A_subscription_that_ends_does_not_stop_the_host_from_shutting_down()
    {
        var bus = Substitute.For<IEventBus>();
        StubStreams(bus, static () => AsyncEnumerable.Empty<object>());
        using var service = new ConnectionHostedService(Substitute.For<IConnectionSupervisor>(), bus,
            NullLogger<ConnectionHostedService>.Instance);
        await service.StartAsync(default);

        var stop = service.StopAsync(default);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_faulting_bus_ends_the_loops_without_faulting_the_host()
    {
        // Both consumers are joined by Task.WhenAll; one broken stream must not take the other, or the
        // host's shutdown, down with it.
        var boom = new InvalidOperationException("the subscription broke.");
        var bus = Substitute.For<IEventBus>();
        StubStreams(bus, () => throw boom);

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        await using var provider = services.BuildServiceProvider();

        using var service = new ConnectionHostedService(Substitute.For<IConnectionSupervisor>(), bus,
            provider.GetRequiredService<ILogger<ConnectionHostedService>>());
        await service.StartAsync(default);
        var stop = service.StopAsync(default);
        await stop;

        Assert.True(stop.IsCompletedSuccessfully);
        Assert.Contains(logs.Records, r => ReferenceEquals(r.Exception, boom));
    }

    /// <summary>Stubs every stream this service subscribes to with the same factory.</summary>
    /// <param name="bus">The substituted bus.</param>
    /// <param name="stream">Produces the stream, or throws to fault it.</param>
    private static void StubStreams(IEventBus bus, Func<IAsyncEnumerable<object>> stream)
    {
        bus.SubscribeAsync<ServerRegisteredEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<ServerRegisteredEvent>());
        bus.SubscribeAsync<ServerCredentialsChangedEvent>(Arg.Any<CancellationToken>())
            .Returns(_ => stream().Cast<ServerCredentialsChangedEvent>());
    }
}
