using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventRelayTests
{
    private readonly IEventChannelLocator _locator = Substitute.For<IEventChannelLocator>();
    private readonly IPlayerChannelPoster _poster = Substitute.For<IPlayerChannelPoster>();
    private readonly IBotTeamChatSender _sender = Substitute.For<IBotTeamChatSender>();
    private readonly IWorkspaceStore _workspace = Substitute.For<IWorkspaceStore>();

    private PlayerEventRelay BuildRelay()
    {
        var scopeFactory = BuildScopeFactory(_workspace);
        _workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");
        return new PlayerEventRelay(
            new PlayerEventRenderer(new ResxLocalizer()),
            _locator, _poster, _sender, scopeFactory);
    }

    private static IServiceScopeFactory BuildScopeFactory(IWorkspaceStore workspace)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => workspace);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static PlayerStateChangedEvent Evt(params PlayerTransition[] ts)
        => new(1UL, Guid.NewGuid(), new MapDimensions(3000, 3000, 0, WorldSize: 3000), ts);

    [Fact]
    public async Task Sends_ingame_line_for_each_transition()
    {
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Connect, 1, "Bob", null)), CancellationToken.None);

        await _sender.Received(1).SendAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Is<string>(s => s.Contains("Bob")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_discord_post_when_no_channel_but_still_sends_ingame()
    {
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Disconnect, 1, "Bob", null)), CancellationToken.None);

        await _poster.DidNotReceive()
            .PostAsync(Arg.Any<ulong>(), Arg.Any<global::Discord.Embed>(), Arg.Any<CancellationToken>());
        await _sender.ReceivedWithAnyArgs(1).SendAsync(default, Guid.Empty, default!, default);
    }

    [Fact]
    public async Task Posts_embed_when_channel_resolves()
    {
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)555);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Death, 1, "Bob", (10f, 10f))), CancellationToken.None);

        await _poster.Received(1).PostAsync(555UL, Arg.Any<global::Discord.Embed>(), Arg.Any<CancellationToken>());
    }
}
