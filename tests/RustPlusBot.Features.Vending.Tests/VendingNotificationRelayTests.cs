using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Posting;
using RustPlusBot.Features.Vending.Relaying;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Vending;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingNotificationRelay"/>.</summary>
public sealed class VendingNotificationRelayTests
{
    private const uint WorldSize = 4000;
    private const ulong GuildId = 10UL;
    private const int PipeId = 69511070;
    private const int ClothId = -858312878;
    private const int Scrap = -932201673;

    /// <summary>The stored sold-out signature for a machine with only the pipes dry.</summary>
    private const string PipeDry = "69511070";

    /// <summary>The stored sold-out signature for a machine with both lines dry (ids sorted ascending).</summary>
    private const string PipeAndClothDry = "-858312878,69511070";

    /// <summary>Our position; far enough from <see cref="RivalX"/> to land in a different grid cell.</summary>
    private const float MineX = 500f, MineY = 3000f;

    /// <summary>The rival's position; see <see cref="MineX"/>.</summary>
    private const float RivalX = 2000f, RivalY = 1000f;

    private static readonly ListingKey Pipe = new(PipeId, false, Scrap, false);

    private static readonly string MyGrid = MapGrid.LabelFor(MineX, MineY, WorldSize, MapGridStyle.InGame);

    private static VendingOfferSnapshot Sell(int itemId, int cost, int stock) =>
        new(itemId, false, 1, Scrap, false, cost, stock);

    private static VendingMachineSnapshot MyShop(params VendingOfferSnapshot[] offers) =>
        new(1UL, MineX, MineY, "Shop", false, offers);

    private static VendingMachineSnapshot MyMachine(int cost, int stock) => MyShop(Sell(PipeId, cost, stock));

    private static VendingMachineSnapshot RivalMachine(int cost, int stock = 5) =>
        new(2UL, RivalX, RivalY, "Rival", false,
            [new VendingOfferSnapshot(PipeId, false, 1, Scrap, false, cost, stock)]);

    private static VendingNotification Notification(ulong messageId, int refQty, int refCost) => new()
    {
        GuildId = GuildId,
        ItemId = PipeId,
        ItemIsBlueprint = false,
        CurrencyId = Scrap,
        CurrencyIsBlueprint = false,
        MessageId = messageId,
        ReferenceQuantity = refQty,
        ReferenceCostPerOrder = refCost,
    };

    private static VendingStockNotification StockNotification(ulong machineId, ulong messageId, string signature) =>
        new()
        {
            GuildId = GuildId,
            MachineId = machineId,
            MessageId = messageId,
            SoldOutSignature = signature,
        };

    [Fact]
    public async Task FirstUndercut_PostsAndStoresTheMessageId()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Poster.EnsureAsync(default, default, default!, default).ReturnsForAnyArgs(555UL);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 5), RivalMachine(cost: 8)), h.Ct);

        await h.Store.Received(1).UpsertNotificationAsync(
            GuildId, h.ServerId, Pipe, 555UL, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnchangedUndercut_EditsRatherThanReposts()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 5), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.Received(1).EnsureAsync(
            Arg.Any<ulong>(), 555UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task OwnerReprices_DeletesTheMessageRatherThanEditingIt()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        // We dropped to 9; the rival at 8 still beats us, but the stale message must go first.
        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 9, stock: 5), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveNotificationAsync(GuildId, h.ServerId, Pipe, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive().EnsureAsync(
            Arg.Any<ulong>(), 555UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RivalGone_DeletesTheMessage()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 5)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveNotificationAsync(GuildId, h.ServerId, Pipe, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnerRestocks_DeletesTheStockMessage()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListStockNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([StockNotification(machineId: 1UL, messageId: 777UL, signature: PipeDry)]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 5)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 777UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveStockNotificationAsync(
            GuildId, h.ServerId, 1UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PartialRestock_DeletesTheStockMessage()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListStockNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([StockNotification(machineId: 1UL, messageId: 777UL, signature: PipeAndClothDry)]);

        // The cloth is back on the shelf; only the pipes are still dry. The owner acted, so the stale
        // message must go and the survivor be reposted as a new unread.
        await h.Relay.HandleObservedAsync(
            h.Observed(MyShop(Sell(PipeId, cost: 10, stock: 0), Sell(ClothId, cost: 5, stock: 4))), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 777UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveStockNotificationAsync(
            GuildId, h.ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive().EnsureAsync(
            Arg.Any<ulong>(), 777UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FurtherItemSellsOut_EditsRatherThanDeleting()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListStockNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([StockNotification(machineId: 1UL, messageId: 777UL, signature: PipeDry)]);

        // The pipes were already dry and now the cloth has gone too: the world moved, not the owner,
        // so the sold-out set only grew and the message is edited in place.
        await h.Relay.HandleObservedAsync(
            h.Observed(MyShop(Sell(PipeId, cost: 10, stock: 0), Sell(ClothId, cost: 5, stock: 0))), h.Ct);

        await h.Poster.Received(1).EnsureAsync(
            Arg.Any<ulong>(), 777UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task MachineGoesWhollyEmpty_EditsRatherThanDeleting()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListStockNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([StockNotification(machineId: 1UL, messageId: 777UL, signature: PipeDry)]);

        // "*" is the maximal sold-out set, so reaching it is growth: nothing came back.
        await h.Relay.HandleObservedAsync(
            h.Observed(new VendingMachineSnapshot(
                1UL, MineX, MineY, "Shop", true, [Sell(PipeId, cost: 10, stock: 0)])), h.Ct);

        await h.Poster.Received(1).EnsureAsync(
            Arg.Any<ulong>(), 777UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task UntrackedServerWithLiveMessages_SweepsThemOnTheNextPoll()
    {
        // Nothing is tracked any more — the last grid was just removed — but the messages it raised are
        // still sitting in #vending. This pass is what clears them; no other code path will.
        var h = Harness.Create();
        h.Store.ListNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);
        h.Store.ListStockNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([StockNotification(machineId: 1UL, messageId: 777UL, signature: PipeDry)]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 0), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 777UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveNotificationAsync(GuildId, h.ServerId, Pipe, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveStockNotificationAsync(
            GuildId, h.ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().EnsureAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Disconnect_ClearsTheIndexButDeletesNothing()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, Guid.Empty, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);
        h.Index.Replace(GuildId, h.ServerId, WorldSize, [MyMachine(cost: 10, stock: 5)]);
        Assert.True(h.Index.HasData(GuildId, h.ServerId));

        await h.Relay.HandleConnectionStatusAsync(h.Disconnected(), h.Ct);

        Assert.False(h.Index.HasData(GuildId, h.ServerId));
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task NoChannelProvisioned_SkipsWithoutTouchingTheStore()
    {
        var h = Harness.Create(channelId: null);
        h.Store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([MyGrid]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 5), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.DidNotReceiveWithAnyArgs().EnsureAsync(default, default, default!, default);
        await h.Store.DidNotReceiveWithAnyArgs().ListGridsAsync(default, Guid.Empty, default);
        await h.Store.DidNotReceiveWithAnyArgs().ListNotificationsAsync(default, Guid.Empty, default);
        Assert.True(h.Index.HasData(GuildId, h.ServerId));
    }

    /// <summary>The relay under test plus the doubles the assertions inspect.</summary>
    private sealed class Harness
    {
        private Harness(
            VendingNotificationRelay relay, VendingIndex index, IVendingStore store,
            IVendingChannelLocator locator, IVendingChannelPoster poster, Guid serverId)
        {
            Relay = relay;
            Index = index;
            Store = store;
            Locator = locator;
            Poster = poster;
            ServerId = serverId;
        }

        public VendingNotificationRelay Relay { get; }

        public VendingIndex Index { get; }

        public IVendingStore Store { get; }

        public IVendingChannelLocator Locator { get; }

        public IVendingChannelPoster Poster { get; }

        public Guid ServerId { get; }

        public CancellationToken Ct { get; } = CancellationToken.None;

        public static Harness Create(ulong? channelId = 999UL)
        {
            var serverId = Guid.NewGuid();

            var store = Substitute.For<IVendingStore>();
            store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);
            store.ListListingsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);
            store.ListNotificationsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);
            store.ListStockNotificationsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);

            var settings = Substitute.For<IMapSettingsStore>();
            settings.GetAsync(default, Guid.Empty, default).ReturnsForAnyArgs(MapLayerSettings.AllOn);

            var workspace = Substitute.For<IWorkspaceStore>();
            workspace.GetCultureAsync(default, default).ReturnsForAnyArgs("en");

            var services = new ServiceCollection();
            services.AddScoped(_ => store);
            services.AddScoped(_ => settings);
            services.AddScoped(_ => workspace);
            var provider = services.BuildServiceProvider();

            var locator = Substitute.For<IVendingChannelLocator>();
            locator.GetChannelIdAsync(default, Guid.Empty, default).ReturnsForAnyArgs(channelId);
            var poster = Substitute.For<IVendingChannelPoster>();

            var localizer = Substitute.For<ILocalizer>();
            localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
            localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
                .Returns(ci => $"{ci.ArgAt<string>(0)}|{string.Join('|', ci.ArgAt<object[]>(2))}");
            var renderer = new VendingEmbedRenderer(Substitute.For<IItemDatabase>(), localizer);

            var index = new VendingIndex();
            var relay = new VendingNotificationRelay(
                index,
                provider.GetRequiredService<IServiceScopeFactory>(),
                locator,
                poster,
                renderer,
                Options.Create(new VendingOptions()),
                NullLogger<VendingNotificationRelay>.Instance);

            return new Harness(relay, index, store, locator, poster, serverId);
        }

        public VendingMachinesObservedEvent Observed(params VendingMachineSnapshot[] machines) =>
            new(GuildId, ServerId, WorldSize, machines);

        public ConnectionStatusChangedEvent Disconnected() => new(GuildId, ServerId, false, true);
    }
}
