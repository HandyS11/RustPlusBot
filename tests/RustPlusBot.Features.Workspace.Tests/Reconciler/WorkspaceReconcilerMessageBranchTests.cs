using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>
/// Characterisation tests pinning the message-reconcile branches the other suites leave open: specs the
/// reconciler filters out before rendering, renderers that come back empty, a message whose channel key
/// moved between deploys, and messages spread over more than one channel.
/// </summary>
public sealed class WorkspaceReconcilerMessageBranchTests
{
    private static readonly Guid ServerId = Guid.Parse("6c1c2c5e-7b0b-4a0f-9f3c-2a5e8f9b1c04");

    [Fact]
    public async Task A_message_declared_in_a_gated_off_channel_is_never_rendered()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name")
            .WithChannel(WorkspaceScope.PerServer, "clanchat", "channel.teamchat.name", 1, "clan")
            .WithCapability("clan", available: false)
            .WithMessage(WorkspaceScope.PerServer, "clan.roster", "clanchat", "roster");
        StubServer(harness);

        await harness.Build().ReconcileServerAsync(1, ServerId);

        Assert.Equal(0, harness.Gateway.PostedMessages);
        Assert.Null(await harness.Store.GetMessageAsync(1, ServerId, "clan.roster"));
    }

    [Fact]
    public async Task A_message_key_no_renderer_answers_for_is_skipped()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithUnrenderedMessage(WorkspaceScope.Global, "information.legacy", "information")
            .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");

        await harness.Build().ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.Null(await harness.Store.GetMessageAsync(1, null, "information.legacy"));
        Assert.NotNull(await harness.Store.GetMessageAsync(1, null, "information.main"));
    }

    [Fact]
    public async Task A_renderer_with_nothing_to_show_posts_nothing_and_records_nothing()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithMutableMessage(WorkspaceScope.Global, "information.main", "information", new TextHolder(null));

        await harness.Build().ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.CreatedChannels); // the channel is still provisioned
        Assert.Equal(0, harness.Gateway.PostedMessages);
        Assert.Null(await harness.Store.GetMessageAsync(1, null, "information.main"));
    }

    [Fact]
    public async Task A_message_that_goes_empty_leaves_the_live_one_untouched()
    {
        var text = new TextHolder("hello");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithMutableMessage(WorkspaceScope.Global, "information.main", "information", text);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var posted = await harness.Store.GetMessageAsync(1, null, "information.main");
        Assert.NotNull(posted);

        text.Current = null;
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.Equal(0, harness.Gateway.EditedMessages);
        Assert.Empty(harness.Gateway.DeletedMessageIds);
        Assert.Equal("hello", harness.Gateway.LivePayload(posted.DiscordMessageId)!.Text);
    }

    [Fact]
    public async Task A_message_moved_to_another_channel_is_reposted_there_and_the_old_one_is_left_behind()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 1)
            .WithMessage(WorkspaceScope.Global, "notice", "information", "notice-text");
        await harness.Build().ReconcileGlobalAsync(1);

        var before = await harness.Store.GetMessageAsync(1, null, "notice");
        Assert.NotNull(before);

        // Next deploy moves the same message key into the other channel, reusing the store and gateway.
        var sut = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 1)
            .WithMessage(WorkspaceScope.Global, "notice", "settings", "notice-text")
            .Build();
        await sut.ReconcileGlobalAsync(1);

        var settings = (await harness.Store.GetChannelsAsync(1, null)).Single(c => c.ChannelKey == "settings");
        var after = await harness.Store.GetMessageAsync(1, null, "notice");
        Assert.NotNull(after);
        Assert.Equal(settings.DiscordChannelId, after.DiscordChannelId);
        Assert.NotEqual(before.DiscordMessageId, after.DiscordMessageId);
        Assert.Equal(2, harness.Gateway.PostedMessages);
        Assert.Empty(harness.Gateway.DeletedMessageIds); // the message left in the old channel is not cleaned up
        Assert.NotNull(harness.Gateway.LivePayload(before.DiscordMessageId));
    }

    [Fact]
    public async Task Messages_in_different_channels_are_each_anchored_to_their_own_channel()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 1)
            .WithMessage(WorkspaceScope.Global, "information.main", "information", "info-text")
            .WithMessage(WorkspaceScope.Global, "settings.main", "settings", "settings-text");
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        var channels = (await harness.Store.GetChannelsAsync(1, null))
            .ToDictionary(c => c.ChannelKey, c => c.DiscordChannelId, StringComparer.Ordinal);
        var info = await harness.Store.GetMessageAsync(1, null, "information.main");
        var settings = await harness.Store.GetMessageAsync(1, null, "settings.main");
        Assert.Equal(channels["information"], info!.DiscordChannelId);
        Assert.Equal(channels["settings"], settings!.DiscordChannelId);
        Assert.Equal(2, harness.Gateway.PostedMessages); // one each, then edited in place
        Assert.Equal(2, harness.Gateway.EditedMessages);
        Assert.Empty(harness.Gateway.DeletedMessageIds);
    }

    private static void StubServer(ReconcilerHarness harness) =>
        harness.Servers.GetAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new RustServer
            {
                Id = ServerId,
                GuildId = 1,
                Name = "Rustopia EU",
                Ip = "1.1.1.1",
                Port = 28015
            });
}
