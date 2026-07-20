using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class ServerInfoRefreshHostedServiceTests
{
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
}
