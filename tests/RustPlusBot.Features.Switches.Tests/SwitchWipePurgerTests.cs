using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Switches.Tests;

/// <summary>Unit tests for <see cref="SwitchWipePurger"/>.</summary>
public sealed class SwitchWipePurgerTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private static Harness Create(IReadOnlyList<SmartSwitch> switches, ulong? channelId = 777UL)
    {
        var store = Substitute.For<ISwitchStore>();
        store.ListByServerAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(switches);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<ISwitchChannelLocator>();
        locator.GetChannelIdAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(channelId);
        var poster = Substitute.For<ISwitchChannelPoster>();

        var purger = new SwitchWipePurger(scopeFactory, locator, poster,
            NullLogger<SwitchWipePurger>.Instance);
        return new Harness(purger, store, locator, poster);
    }

    private static SmartSwitch Switch(ulong entityId, ulong? messageId) => new()
    {
        GuildId = 10UL,
        ServerId = ServerId,
        EntityId = entityId,
        Name = $"Switch {entityId}",
        MessageId = messageId
    };

    private static ServerWipedEvent Event() => new(10UL, ServerId, null, null, 1u, 3500u);

    [Fact]
    public async Task Removes_rows_and_deletes_messages()
    {
        var h = Create([Switch(1UL, 100UL), Switch(2UL, 200UL)]);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 2UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 100UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 200UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switch_without_message_is_removed_without_delete_call()
    {
        var h = Create([Switch(1UL, messageId: null)]);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task Missing_channel_still_removes_rows()
    {
        var h = Create([Switch(1UL, 100UL)], channelId: null);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task No_switches_is_a_noop()
    {
        var h = Create([]);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().RemoveAsync(default, Guid.Empty, default, default);
        await h.Locator.DidNotReceiveWithAnyArgs().GetChannelIdAsync(default, Guid.Empty, default);
    }

    private sealed record Harness(
        SwitchWipePurger Purger,
        ISwitchStore Store,
        ISwitchChannelLocator Locator,
        ISwitchChannelPoster Poster);
}
