using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>
/// Covers channel-order enforcement: a capability-gated channel created after the rest of the
/// category (Discord appends it at the bottom) must be moved back into spec order, and a category
/// already in spec order must not trigger any reorder call.
/// </summary>
public sealed class WorkspaceReconcilerOrderTests
{
    private static readonly Guid ServerId = Guid.Parse("8f0f0d3e-2b16-4a3e-8f5a-1d1c9f2a7b41");

    [Fact]
    public async Task Moves_a_late_created_gated_channel_into_spec_order()
    {
        var harness = OrderedHarness(clanAvailable: false);
        await harness.Build().ReconcileServerAsync(1, ServerId);

        // The capability turns on afterwards: the clan channel is created last, so Discord appends
        // it at the bottom of the category. The reconciler must restore the declared order.
        var sut = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithChannel(WorkspaceScope.PerServer, "clanchat", "channel.clanchat.name", 1, "clan")
            .WithChannel(WorkspaceScope.PerServer, "events", "channel.events.name", 2)
            .WithCapability("clan", available: true)
            .Build();
        await sut.ReconcileServerAsync(1, ServerId);

        var channels = await harness.Store.GetChannelsAsync(1, ServerId);
        var byKey = channels.ToDictionary(c => c.ChannelKey, c => c.DiscordChannelId, StringComparer.Ordinal);
        var categoryId = Assert.Single(harness.Gateway.CategoryIds);
        Assert.Equal([byKey["info"], byKey["clanchat"], byKey["events"]],
            harness.Gateway.ChannelOrder(categoryId));
    }

    [Fact]
    public async Task Does_not_reorder_a_category_that_is_already_in_spec_order()
    {
        var harness = OrderedHarness(clanAvailable: true);
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);
        await sut.ReconcileServerAsync(1, ServerId);

        Assert.Equal(0, harness.Gateway.ReorderCalls);
    }

    [Fact]
    public async Task Reorders_only_once_for_a_late_created_channel()
    {
        var harness = OrderedHarness(clanAvailable: false);
        await harness.Build().ReconcileServerAsync(1, ServerId);

        var sut = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithChannel(WorkspaceScope.PerServer, "clanchat", "channel.clanchat.name", 1, "clan")
            .WithChannel(WorkspaceScope.PerServer, "events", "channel.events.name", 2)
            .WithCapability("clan", available: true)
            .Build();

        await sut.ReconcileServerAsync(1, ServerId);
        await sut.ReconcileServerAsync(1, ServerId);

        Assert.Equal(1, harness.Gateway.ReorderCalls);
    }

    private static ReconcilerHarness OrderedHarness(bool clanAvailable)
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithChannel(WorkspaceScope.PerServer, "clanchat", "channel.clanchat.name", 1, "clan")
            .WithChannel(WorkspaceScope.PerServer, "events", "channel.events.name", 2)
            .WithCapability("clan", clanAvailable);
        harness.Servers.GetAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = ServerId,
                GuildId = 1,
                Name = "Rustopia EU",
                Ip = "1.1.1.1",
                Port = 28015
            });
        return harness;
    }
}
