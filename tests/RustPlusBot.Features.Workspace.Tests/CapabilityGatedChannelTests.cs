using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Tests.Reconciler;

namespace RustPlusBot.Features.Workspace.Tests;

public sealed class CapabilityGatedChannelTests
{
    private static readonly Guid ServerId = Guid.Parse("8f0f0d3e-2b16-4a3e-8f5a-1d1c9f2a7b41");

    [Fact]
    public async Task Creates_a_capability_gated_channel_when_the_capability_is_available()
    {
        var harness = GatedHarness(available: true);
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);

        var channels = await harness.Store.GetChannelsAsync(1, ServerId);
        Assert.Contains(channels, c => c.ChannelKey == "clanchat");
        var record = channels.Single(c => c.ChannelKey == "clanchat");
        Assert.True(harness.Gateway.ChannelExists(1, record.DiscordChannelId));
    }

    [Fact]
    public async Task Skips_and_deletes_a_capability_gated_channel_when_unavailable()
    {
        var harness = GatedHarness(available: true);
        await harness.Build().ReconcileServerAsync(1, ServerId);
        var provisioned = (await harness.Store.GetChannelsAsync(1, ServerId)).Single(c => c.ChannelKey == "clanchat");

        // The capability goes away: same store + gateway, provider now reports unavailable.
        var sut = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithChannel(WorkspaceScope.PerServer, "clanchat", "channel.teamchat.name", 1, "clan")
            .WithCapability("clan", available: false)
            .Build();

        await sut.ReconcileServerAsync(1, ServerId);

        Assert.False(harness.Gateway.ChannelExists(1, provisioned.DiscordChannelId));
        Assert.DoesNotContain(await harness.Store.GetChannelsAsync(1, ServerId), c => c.ChannelKey == "clanchat");
        Assert.Contains(await harness.Store.GetChannelsAsync(1, ServerId), c => c.ChannelKey == "info");
    }

    [Fact]
    public async Task Does_not_create_a_capability_gated_channel_that_was_never_provisioned()
    {
        var harness = GatedHarness(available: false);
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);

        var channels = await harness.Store.GetChannelsAsync(1, ServerId);
        Assert.DoesNotContain(channels, c => c.ChannelKey == "clanchat");
        Assert.Equal(1, harness.Gateway.CreatedChannels); // only the ungated #info channel
    }

    [Fact]
    public void Fails_fast_when_a_gated_channel_has_no_registered_capability_provider()
    {
        // "No provider" reads as "unavailable", and unavailable means the reconciler deletes the
        // channel and its history. A misconfigured host must fail to start, not destroy data.
        var harness = NewHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithChannel(WorkspaceScope.PerServer, "ghostly", "channel.teamchat.name", 1, "ghost");

        var ex = Assert.Throws<InvalidOperationException>(harness.Build);

        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_ungated_channels_untouched()
    {
        var harness = GatedHarness(available: false);
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);

        var channels = await harness.Store.GetChannelsAsync(1, ServerId);
        var info = Assert.Single(channels);
        Assert.Equal("info", info.ChannelKey);
        Assert.True(harness.Gateway.ChannelExists(1, info.DiscordChannelId));
    }

    private static ReconcilerHarness GatedHarness(bool available) => NewHarness()
        .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
        .WithChannel(WorkspaceScope.PerServer, "clanchat", "channel.teamchat.name", 1, "clan")
        .WithCapability("clan", available);

    private static ReconcilerHarness NewHarness()
    {
        var harness = new ReconcilerHarness();
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
