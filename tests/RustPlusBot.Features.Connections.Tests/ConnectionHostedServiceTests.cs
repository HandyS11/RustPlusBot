using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Hosting;
using RustPlusBot.Features.Connections.Supervisor;

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
}
