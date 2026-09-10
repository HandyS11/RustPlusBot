using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class ChatRegistrationTests
{
    private const ulong TeamChannel = 777UL;
    private const ulong ClanChannel = 888UL;

    [Fact]
    public async Task Services_resolve()
    {
        await using var provider = BuildProvider(out _, out _, out _, out _);

        Assert.NotNull(provider.GetRequiredService<ChatRelay>());
        Assert.NotNull(provider.GetRequiredService<ChatInboundProcessor>());
        Assert.Contains(provider.GetServices<IHostedService>(), h => h is ChatHostedService);
    }

    [Fact]
    public async Task Relay_serves_both_locator_kinds()
    {
        const ulong guild = 10UL;
        var server = Guid.NewGuid();

        await using var provider = BuildProvider(out var team, out var clan, out _, out var poster);
        team.GetChannelIdAsync(guild, server, Arg.Any<CancellationToken>()).Returns((ulong?)TeamChannel);
        clan.GetChannelIdAsync(guild, server, Arg.Any<CancellationToken>()).Returns((ulong?)ClanChannel);

        var relay = provider.GetRequiredService<ChatRelay>();

        await relay.RelayAsync(
            new RelayedChatLine(ChatChannelKind.Team, guild, server, "Bob", "hi team", FromActivePlayer: false),
            CancellationToken.None);
        await relay.RelayAsync(
            new RelayedChatLine(ChatChannelKind.Clan, guild, server, "Bob", "hi clan", FromActivePlayer: false),
            CancellationToken.None);

        await poster.Received(1)
            .PostAsync(ChatChannelKind.Team, guild, TeamChannel, "Bob", "hi team", Arg.Any<CancellationToken>());
        await poster.Received(1)
            .PostAsync(ChatChannelKind.Clan, guild, ClanChannel, "Bob", "hi clan", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Relay_and_processor_share_the_dedup_buffer()
    {
        const ulong guild = 10UL;
        var server = Guid.NewGuid();

        await using var provider = BuildProvider(out var team, out var clan, out var sender, out var poster);
        team.ResolveAsync(TeamChannel, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(guild, server));
        team.GetChannelIdAsync(guild, server, Arg.Any<CancellationToken>()).Returns((ulong?)TeamChannel);
        clan.ResolveAsync(ClanChannel, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(guild, server));
        clan.GetChannelIdAsync(guild, server, Arg.Any<CancellationToken>()).Returns((ulong?)ClanChannel);
        sender.SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ChatSendResult.Sent);

        var processor = provider.GetRequiredService<ChatInboundProcessor>();
        var relay = provider.GetRequiredService<ChatRelay>();

        // The processor records the exact relayed line "[Alice] hi" into the dedup buffer it was constructed with.
        await processor.ProcessAsync(new InboundMessage(false, TeamChannel, "Alice", "hi"), CancellationToken.None);

        // The bot player's echo of that same line must be dropped by the relay — which only happens if the
        // relay was constructed with the SAME buffer instance (a per-service buffer would miss and post).
        await relay.RelayAsync(
            new RelayedChatLine(ChatChannelKind.Team, guild, server, "BotPlayer", "[Alice] hi",
                FromActivePlayer: true),
            CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ServiceProvider BuildProvider(
        out IChatChannelLocator teamLocator,
        out IChatChannelLocator clanLocator,
        out IChatSender sender,
        out IChatWebhookPoster poster)
    {
        // Kind is stubbed explicitly on both doubles: NSubstitute returns Team (enum 0) by default, so an
        // unstubbed clan double would register as a second team locator and the relay would never route clan.
        teamLocator = Substitute.For<IChatChannelLocator>();
        teamLocator.Kind.Returns(ChatChannelKind.Team);
        clanLocator = Substitute.For<IChatChannelLocator>();
        clanLocator.Kind.Returns(ChatChannelKind.Clan);
        sender = Substitute.For<IChatSender>();
        poster = Substitute.For<IChatWebhookPoster>();

        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(teamLocator);
        services.AddSingleton(clanLocator);
        services.AddSingleton(sender);
        services.AddLogging();

        // IMuteStore is scoped in production; register it scoped here so ValidateScopes catches any captive
        // dependency on the singleton inbound processor (it must resolve the store per message, not capture it).
        services.AddScoped(_ => Substitute.For<IMuteStore>());
        services.AddChat();

        // Override the real webhook poster so the relay's (non-)posting can be asserted.
        services.AddSingleton(poster);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }
}
