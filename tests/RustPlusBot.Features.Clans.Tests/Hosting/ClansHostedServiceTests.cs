using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Clans.Hosting;
using RustPlusBot.Features.Clans.Messages;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Clans.Posting;
using RustPlusBot.Features.Clans.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Clans.Tests.Hosting;

public sealed class ClansHostedServiceTests
{
    private const ulong Guild = 42UL;

    private static readonly Guid Poison = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid Healthy = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task A_faulted_state_change_does_not_end_the_state_loop()
    {
        // The loop is the only thing that ever tears the clan channels down again, so one bad event
        // ending it would leave a player who has left their clan with the channels forever.
        var store = Substitute.For<IClanStore>();
        store.GetAsync(Guild, Poison, Arg.Any<CancellationToken>())
            .Returns<ClanSnapshot?>(_ => throw new InvalidOperationException("transient store failure"));
        store.GetAsync(Guild, Healthy, Arg.Any<CancellationToken>()).Returns((ClanSnapshot?)null);

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.When(s => s.SaveAsync(Guild, Healthy, Arg.Any<ClanSnapshot>(), Arg.Any<CancellationToken>()))
            .Do(_ => applied.TrySetResult());

        var bus = new InMemoryEventBus();
        var service = new ClansHostedService(bus, BuildStateService(store),
            NullLogger<ClansHostedService>.Instance);
        await service.StartAsync(CancellationToken.None);

        try
        {
            // Republished until observed: the subscription is established asynchronously by StartAsync.
            // The poison event is always enqueued first, so the healthy one can only be applied by a
            // loop that survived it.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!applied.Task.IsCompleted && DateTimeOffset.UtcNow < deadline)
            {
                await bus.PublishAsync(
                    new ClanStateChangedEvent(Guild, Poison, ClanProbeStatus.HasClan, Snapshot()));
                await bus.PublishAsync(
                    new ClanStateChangedEvent(Guild, Healthy, ClanProbeStatus.HasClan, Snapshot()));
                await Task.Delay(20);
            }

            Assert.True(applied.Task.IsCompleted,
                "The healthy clan state change was never applied: the loop died on the failing one.");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    private static ClanSnapshot Snapshot() =>
        new(7L, "Wolves", DateTimeOffset.UnixEpoch, 1UL, null, null, null, null, null, null, null, [], [], []);

    private static ClanStateService BuildStateService(IClanStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped(_ => Substitute.For<IClanNameResolver>());
        services.AddScoped(_ => Substitute.For<IWorkspaceReconciler>());
        services.AddScoped(_ => Substitute.For<IRustServerQuery>());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var clock = new FixedClock();

        return new ClanStateService(
            scopeFactory,
            Substitute.For<IClanInfoChannelLocator>(),
            Substitute.For<IClanFeedPoster>(),
            new ClanChangeRenderer(Substitute.For<ILocalizer>()),
            new ClanCapabilityProvider(scopeFactory, clock),
            clock,
            NullLogger<ClanStateService>.Instance);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}
