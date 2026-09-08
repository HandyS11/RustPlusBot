using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class ServerInfoRefreshHostedServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public void Interval_is_clamped_to_a_one_second_floor()
    {
        var options = Options.Create(new WorkspaceOptions
        {
            InfoRefreshInterval = TimeSpan.Zero
        });

        Assert.Equal(TimeSpan.FromSeconds(1), ServerInfoRefreshHostedService.ResolveInterval(options.Value));
    }

    [Fact]
    public void Interval_is_clamped_when_negative()
    {
        var options = Options.Create(new WorkspaceOptions
        {
            InfoRefreshInterval = TimeSpan.FromSeconds(-30)
        });

        Assert.Equal(TimeSpan.FromSeconds(1), ServerInfoRefreshHostedService.ResolveInterval(options.Value));
    }

    [Fact]
    public void Configured_interval_passes_through()
    {
        var options = Options.Create(new WorkspaceOptions
        {
            InfoRefreshInterval = TimeSpan.FromMinutes(5)
        });

        Assert.Equal(TimeSpan.FromMinutes(5), ServerInfoRefreshHostedService.ResolveInterval(options.Value));
    }

    [Fact]
    public async Task RefreshDueServers_refreshes_every_connectable_server_from_the_store()
    {
        var serverA = Guid.NewGuid();
        var serverB = Guid.NewGuid();
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(ulong GuildId, Guid ServerId)>>([(1UL, serverA), (2UL, serverB)]);
        var refresher = Substitute.For<IServerInfoRefresher>();

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => refresher);
        await using var provider = services.BuildServiceProvider();

        var service = new ServerInfoRefreshHostedService(
            Options.Create(new WorkspaceOptions()),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ServerInfoRefreshHostedService>.Instance);

        await service.RefreshDueServersAsync(CancellationToken.None);

        await refresher.Received(1).RefreshAsync(1UL, serverA, Arg.Any<CancellationToken>());
        await refresher.Received(1).RefreshAsync(2UL, serverB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_tick_loop_keeps_refreshing_connectable_servers_with_no_event_to_prompt_it()
    {
        // #info shows in-game time, population and team state, all of which drift continuously while the
        // reconciler stays idle. Only this loop keeps the embeds honest.
        var serverId = Guid.NewGuid();
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(ulong GuildId, Guid ServerId)>>([(7UL, serverId)]);
        var refresher = Substitute.For<IServerInfoRefresher>();
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refresher.When(r => r.RefreshAsync(7UL, serverId, Arg.Any<CancellationToken>()))
            .Do(_ => refreshed.TrySetResult());

        await using var provider = Provider(store, refresher);
        using var service = Service(provider);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await refreshed.Task.WaitAsync(Patience);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await refresher.Received().RefreshAsync(7UL, serverId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_transient_store_failure_costs_one_tick_and_not_the_loop()
    {
        // Enumerating the connectable servers hits the database; a locked or briefly unavailable store must
        // not end the rotation for the rest of the process.
        var serverId = Guid.NewGuid();
        var attempts = 0;
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(ulong GuildId, Guid ServerId)>>(_ =>
                Interlocked.Increment(ref attempts) == 1
                    ? throw new TimeoutException("database is locked")
                    : [(7UL, serverId)]);
        var refresher = Substitute.For<IServerInfoRefresher>();
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refresher.When(r => r.RefreshAsync(7UL, serverId, Arg.Any<CancellationToken>()))
            .Do(_ => refreshed.TrySetResult());

        await using var provider = Provider(store, refresher);
        using var service = Service(provider);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await refreshed.Task.WaitAsync(Patience);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(Volatile.Read(ref attempts) >= 2, "the loop stopped after the first store failure");
    }

    [Fact]
    public async Task One_servers_failed_refresh_does_not_cost_the_servers_behind_it()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(ulong GuildId, Guid ServerId)>>([(1UL, first), (2UL, second)]);
        var refresher = Substitute.For<IServerInfoRefresher>();
        refresher.RefreshAsync(1UL, first, Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Discord did not answer."));

        await using var provider = Provider(store, refresher);
        using var service = Service(provider);

        await service.RefreshDueServersAsync(CancellationToken.None);

        await refresher.Received(1).RefreshAsync(2UL, second, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shutdown_during_a_refresh_ends_the_rotation_instead_of_being_swallowed()
    {
        // A cancelled refresh is shutdown, not a per-server failure: swallowing it would make the loop
        // spin through every remaining server on a host that is already going down.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<(ulong GuildId, Guid ServerId)>>([(1UL, first), (2UL, second)]);
        var refresher = Substitute.For<IServerInfoRefresher>();
        refresher.RefreshAsync(1UL, first, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await using var provider = Provider(store, refresher);
        using var service = Service(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RefreshDueServersAsync(CancellationToken.None));
        await refresher.DidNotReceive().RefreshAsync(2UL, second, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shutdown_while_listing_the_servers_ends_the_tick_instead_of_being_swallowed()
    {
        var store = Substitute.For<IConnectionStore>();
        store.ListConnectableServersAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var refresher = Substitute.For<IServerInfoRefresher>();

        await using var provider = Provider(store, refresher);
        using var service = Service(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RefreshDueServersAsync(CancellationToken.None));
    }

    private static ServiceProvider Provider(IConnectionStore store, IServerInfoRefresher refresher)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => refresher);
        return services.BuildServiceProvider();
    }

    private static ServerInfoRefreshHostedService Service(ServiceProvider provider) =>
        new(Options.Create(new WorkspaceOptions
            {
                InfoRefreshInterval = TimeSpan.FromSeconds(1)
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ServerInfoRefreshHostedService>.Instance);

#pragma warning disable S2699 // The implicit assertion is "no exception is thrown".
    [Fact]
    public async Task StopAsync_without_a_start_completes()
    {
        await using var provider = Provider(Substitute.For<IConnectionStore>(),
            Substitute.For<IServerInfoRefresher>());
        using var service = Service(provider);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_stops_waiting_when_its_own_token_is_already_cancelled()
    {
        // The host passes a shutdown-deadline token; when it has already expired the join must be abandoned
        // rather than propagating out of StopAsync and failing the shutdown.
        await using var provider = Provider(Substitute.For<IConnectionStore>(),
            Substitute.For<IServerInfoRefresher>());
        using var service = Service(provider);
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(new CancellationToken(canceled: true));
    }
#pragma warning restore S2699
}
