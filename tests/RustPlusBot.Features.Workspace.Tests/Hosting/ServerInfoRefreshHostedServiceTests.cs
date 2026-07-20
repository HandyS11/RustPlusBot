using Microsoft.Extensions.Options;
using RustPlusBot.Features.Workspace.Hosting;

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
    public void Connected_servers_are_tracked_and_dropped_on_disconnect()
    {
        var tracker = new ConnectedServerSet();
        var serverId = Guid.NewGuid();

        tracker.Set(1, serverId, connected: true);
        Assert.Contains((1UL, serverId), tracker.Snapshot());

        tracker.Set(1, serverId, connected: false);
        Assert.DoesNotContain((1UL, serverId), tracker.Snapshot());
    }
}
