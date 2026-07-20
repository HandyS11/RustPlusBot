using Discord;
using NSubstitute;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class ServerInfoRefresherTests
{
    private const ulong GuildId = 1;
    private const ulong ChannelId = 500;
    private const ulong MessageId = 900;
    private static readonly Guid ServerId = Guid.NewGuid();

    private static IWorkspaceStore StoreWith(params string[] keys)
    {
        var store = Substitute.For<IWorkspaceStore>();
        store.GetCultureAsync(GuildId, Arg.Any<CancellationToken>()).Returns("en");
        foreach (var key in keys)
        {
            store.GetMessageAsync(GuildId, ServerId, key, Arg.Any<CancellationToken>())
                .Returns(new ProvisionedMessage
                {
                    GuildId = GuildId,
                    RustServerId = ServerId,
                    MessageKey = key,
                    DiscordChannelId = ChannelId,
                    DiscordMessageId = MessageId,
                });
        }

        return store;
    }

    [Fact]
    public async Task First_refresh_edits_the_message()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await gateway.Received(1).EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unchanged_render_is_not_sent_twice()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);
        await refresher.RefreshAsync(GuildId, ServerId, default);

        Assert.Equal(2, renderer.Calls);
        await gateway.Received(1).EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changed_render_is_sent_again()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);
        renderer.Title = "v2";
        await refresher.RefreshAsync(GuildId, ServerId, default);

        await gateway.Received(2).EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_message_row_escalates_to_full_reconcile()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var store = Substitute.For<IWorkspaceStore>();
        store.GetCultureAsync(GuildId, Arg.Any<CancellationToken>()).Returns("en");
        store.GetMessageAsync(GuildId, ServerId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProvisionedMessage?)null);
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(store, gateway, [renderer], new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await reconciler.Received(1).ReconcileServerAsync(GuildId, ServerId, Arg.Any<CancellationToken>());
        await gateway.DidNotReceive().EditMessageAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<MessagePayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_failure_escalates_and_invalidates_the_gate()
    {
        var renderer = new StubRenderer(WorkspaceMessageKeys.ServerInfo, "v1");
        var gateway = Substitute.For<IWorkspaceGateway>();
        gateway.EditMessageAsync(GuildId, ChannelId, MessageId, Arg.Any<MessagePayload>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("404")));
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var gate = new RenderGate();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer], gate,
            reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await reconciler.Received(1).ReconcileServerAsync(GuildId, ServerId, Arg.Any<CancellationToken>());
        // The gate must not remember a render that never landed.
        Assert.True(gate.ShouldSend(MessageId, "anything"));
    }

    [Fact]
    public async Task Empty_payload_is_skipped_without_editing()
    {
        var renderer = new EmptyPayloadRenderer(WorkspaceMessageKeys.ServerInfo);
        var gateway = Substitute.For<IWorkspaceGateway>();
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var refresher = new ServerInfoRefresher(StoreWith(WorkspaceMessageKeys.ServerInfo), gateway, [renderer],
            new RenderGate(), reconciler);

        await refresher.RefreshAsync(GuildId, ServerId, default);

        await gateway.DidNotReceive().EditMessageAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<MessagePayload>(), Arg.Any<CancellationToken>());
        await reconciler.DidNotReceive().ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refreshes_all_three_info_messages()
    {
        var renderers = new IMessageRenderer[]
        {
            new StubRenderer(WorkspaceMessageKeys.ServerInfo, "a"),
            new StubRenderer(WorkspaceMessageKeys.ServerEvents, "b"),
            new StubRenderer(WorkspaceMessageKeys.ServerTeam, "c"),
        };
        var store = StoreWith(WorkspaceMessageKeys.ServerInfo, WorkspaceMessageKeys.ServerEvents,
            WorkspaceMessageKeys.ServerTeam);
        var gateway = Substitute.For<IWorkspaceGateway>();
        var refresher = new ServerInfoRefresher(store, gateway, renderers, new RenderGate(),
            Substitute.For<IWorkspaceReconciler>());

        await refresher.RefreshAsync(GuildId, ServerId, default);

        Assert.All(renderers.Cast<StubRenderer>(), r => Assert.Equal(1, r.Calls));
    }

    private sealed class StubRenderer(string key, string title) : IMessageRenderer
    {
        public int Calls { get; private set; }

        public string Title { get; set; } = title;
        public string MessageKey => key;

        public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
        {
            Calls++;
            var embed = new EmbedBuilder().WithTitle(Title).Build();
            return ValueTask.FromResult(new MessagePayload(null, embed, null));
        }
    }

    private sealed class EmptyPayloadRenderer(string key) : IMessageRenderer
    {
        public string MessageKey => key;

        public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new MessagePayload(null, null, null));
    }
}
