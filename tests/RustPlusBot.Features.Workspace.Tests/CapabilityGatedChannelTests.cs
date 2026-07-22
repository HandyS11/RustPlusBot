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

    [Fact]
    public async Task Pins_a_pinned_message_only_when_it_is_newly_posted()
    {
        var harness = NewHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithMessage(WorkspaceScope.PerServer, "clan.overview", "info", "overview", pinned: true);
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);

        var (pinnedChannelId, pinnedMessageId) = Assert.Single(harness.Gateway.PinnedMessages);
        var record = await harness.Store.GetMessageAsync(1, ServerId, "clan.overview");
        Assert.NotNull(record);
        Assert.Equal(record!.DiscordMessageId, pinnedMessageId);
        Assert.Equal(record.DiscordChannelId, pinnedChannelId);

        // Second reconcile: the message is still live, so it is edited — and not pinned again.
        await sut.ReconcileServerAsync(1, ServerId);

        Assert.Single(harness.Gateway.PinnedMessages);
        Assert.Equal(1, harness.Gateway.EditedMessages);
    }

    [Fact]
    public async Task Does_not_pin_messages_that_are_not_marked_pinned()
    {
        var harness = NewHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithMessage(WorkspaceScope.PerServer, "server.info", "info", "info-text");
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);

        Assert.Empty(harness.Gateway.PinnedMessages);
    }

    [Fact]
    public async Task A_failed_pin_does_not_fail_the_reconcile()
    {
        var harness = NewHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithMessage(WorkspaceScope.PerServer, "clan.overview", "info", "overview", pinned: true);
        harness.Gateway.ThrowOnPin = true;
        var sut = harness.Build();

        await sut.ReconcileServerAsync(1, ServerId);

        Assert.Empty(harness.Gateway.PinnedMessages);
        Assert.NotNull(await harness.Store.GetMessageAsync(1, ServerId, "clan.overview"));
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
