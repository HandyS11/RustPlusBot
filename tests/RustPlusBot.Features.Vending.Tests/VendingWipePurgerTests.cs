using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Posting;
using RustPlusBot.Features.Vending.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingWipePurger"/>.</summary>
public sealed class VendingWipePurgerTests
{
    private static VendingNotification Notification(ulong messageId) => new()
    {
        GuildId = 10UL,
        ItemId = 1,
        ItemIsBlueprint = false,
        CurrencyId = 2,
        CurrencyIsBlueprint = false,
        MessageId = messageId,
    };

    private static VendingStockNotification StockNotification(ulong messageId) => new()
    {
        GuildId = 10UL, MachineId = 1UL, MessageId = messageId,
    };

    [Fact]
    public async Task Wipe_DeletesEveryNotificationMessage()
    {
        var h = Harness.Create(
            notifications: [Notification(555UL)],
            stockNotifications: [StockNotification(777UL)]);

        await h.Purger.HandleServerWipedAsync(h.Wiped(), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 777UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wipe_ClearsGridsButKeepsManualListings()
    {
        var h = Harness.Create();

        await h.Purger.HandleServerWipedAsync(h.Wiped(), h.Ct);

        // The base is gone, so the cell registration is meaningless — but an item+price pair a player
        // typed in is still exactly as valid next wipe.
        await h.Store.Received(1).PurgeGridsAsync(10UL, h.ServerId, Arg.Any<CancellationToken>());
        await h.Store.DidNotReceiveWithAnyArgs().RemoveListingAsync(default, Guid.Empty, default, default);
    }

    [Fact]
    public async Task Wipe_WithNothingTracked_IsANoOp()
    {
        var h = Harness.Create(notifications: [], stockNotifications: []);

        await h.Purger.HandleServerWipedAsync(h.Wiped(), h.Ct);

        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    /// <summary>The purger under test plus the doubles the assertions inspect.</summary>
    private sealed class Harness
    {
        private Harness(
            VendingWipePurger purger,
            IVendingStore store,
            IVendingChannelLocator locator,
            IVendingChannelPoster poster,
            Guid serverId)
        {
            Purger = purger;
            Store = store;
            Locator = locator;
            Poster = poster;
            ServerId = serverId;
        }

        public VendingWipePurger Purger { get; }

        public IVendingStore Store { get; }

        public IVendingChannelLocator Locator { get; }

        public IVendingChannelPoster Poster { get; }

        public Guid ServerId { get; }

        public CancellationToken Ct { get; } = CancellationToken.None;

        public static Harness Create(
            IReadOnlyList<VendingNotification>? notifications = null,
            IReadOnlyList<VendingStockNotification>? stockNotifications = null,
            ulong? channelId = 999UL)
        {
            var serverId = Guid.NewGuid();

            var store = Substitute.For<IVendingStore>();
            store.ListNotificationsAsync(10UL, serverId, Arg.Any<CancellationToken>())
                .Returns(notifications ?? [Notification(555UL)]);
            store.ListStockNotificationsAsync(10UL, serverId, Arg.Any<CancellationToken>())
                .Returns(stockNotifications ?? [StockNotification(777UL)]);

            var services = new ServiceCollection();
            services.AddScoped(_ => store);
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var locator = Substitute.For<IVendingChannelLocator>();
            locator.GetChannelIdAsync(10UL, serverId, Arg.Any<CancellationToken>()).Returns(channelId);
            var poster = Substitute.For<IVendingChannelPoster>();

            var index = new VendingIndex();
            var purger = new VendingWipePurger(
                scopeFactory, index, locator, poster, NullLogger<VendingWipePurger>.Instance);

            return new Harness(purger, store, locator, poster, serverId);
        }

        public ServerWipedEvent Wiped() => new(10UL, ServerId, null, null, 1u, 3500u);
    }
}
