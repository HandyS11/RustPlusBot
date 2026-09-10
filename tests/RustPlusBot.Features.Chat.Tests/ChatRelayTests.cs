using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class ChatRelayTests
{
    private const ulong TeamChannel = 777UL;
    private const ulong ClanChannel = 888UL;

    private static ulong ChannelFor(ChatChannelKind kind) => kind == ChatChannelKind.Clan ? ClanChannel : TeamChannel;

    private static (ChatRelay Relay, IChatWebhookPoster Poster, RelayDedupBuffer Dedup,
        IChatChannelLocator Team, IChatChannelLocator Clan)
        Build(string prefix = "!")
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var poster = Substitute.For<IChatWebhookPoster>();

        // Both doubles MUST have Kind stubbed explicitly: NSubstitute returns ChatChannelKind.Team (enum 0)
        // by default, so an unstubbed clan double would silently behave as a second team locator.
        var team = Substitute.For<IChatChannelLocator>();
        team.Kind.Returns(ChatChannelKind.Team);
        team.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)TeamChannel);
        var clan = Substitute.For<IChatChannelLocator>();
        clan.Kind.Returns(ChatChannelKind.Clan);
        clan.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)ClanChannel);

        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(prefix);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        scopeProvider.GetService(typeof(IMuteStore)).Returns(muteStore);
        scope.ServiceProvider.Returns(scopeProvider);
        scopeFactory.CreateScope().Returns(scope);
        var relay = new ChatRelay([team, clan], poster, dedup, scopeFactory, NullLogger<ChatRelay>.Instance);
        return (relay, poster, dedup, team, clan);
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Posts_a_normal_message_via_webhook(ChatChannelKind kind)
    {
        var (relay, poster, _, _, _) = Build();
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", "hello", FromActivePlayer: false);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1).PostAsync(kind, 10UL, ChannelFor(kind), "Bob", "hello", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Drops_a_bot_prefixed_line_from_the_active_player(ChatChannelKind kind)
    {
        var (relay, poster, _, _, _) = Build();
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "BotPlayer", "[R+] Cargo Ship entered the map",
            FromActivePlayer: true);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Keeps_a_bot_prefixed_line_from_another_player(ChatChannelKind kind)
    {
        var (relay, poster, _, _, _) = Build();
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", "[R+] hi", FromActivePlayer: false);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1)
            .PostAsync(kind, 10UL, ChannelFor(kind), "Bob", "[R+] hi", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Drops_our_own_echo(ChatChannelKind kind)
    {
        var (relay, poster, dedup, _, _) = Build();
        dedup.Record(kind, (10UL, Guid.Empty), "[Alice] hello");
        var echo = new RelayedChatLine(kind, 10UL, Guid.Empty, "BotPlayer", "[Alice] hello", FromActivePlayer: true);

        await relay.RelayAsync(echo, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Posts_an_active_player_line_that_is_not_an_echo(ChatChannelKind kind)
    {
        var (relay, poster, _, _, _) = Build();
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "BotPlayer", "genuine", FromActivePlayer: true);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1)
            .PostAsync(kind, 10UL, ChannelFor(kind), "BotPlayer", "genuine", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team, true)]
    [InlineData(ChatChannelKind.Team, false)]
    [InlineData(ChatChannelKind.Clan, true)]
    [InlineData(ChatChannelKind.Clan, false)]
    public async Task Drops_a_command_invocation(ChatChannelKind kind, bool fromActivePlayer)
    {
        var (relay, poster, _, _, _) = Build();
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", "!pop", fromActivePlayer);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Ignores_leading_whitespace_when_matching_the_command_prefix(ChatChannelKind kind)
    {
        var (relay, poster, _, _, _) = Build();
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", "   !pop", FromActivePlayer: false);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Honors_a_custom_command_prefix(ChatChannelKind kind)
    {
        var (relay, poster, _, _, _) = Build(prefix: ".");

        await relay.RelayAsync(new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", ".pop", false),
            CancellationToken.None);
        await relay.RelayAsync(new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", "!not a command", false),
            CancellationToken.None);

        await poster.Received(1)
            .PostAsync(kind, 10UL, ChannelFor(kind), "Bob", "!not a command", Arg.Any<CancellationToken>());
        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<string>(),
            ".pop", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ChatChannelKind.Team)]
    [InlineData(ChatChannelKind.Clan)]
    public async Task Does_nothing_when_the_channel_is_not_provisioned(ChatChannelKind kind)
    {
        var (relay, poster, _, team, clan) = Build();
        team.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ulong?)null);
        clan.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ulong?)null);
        var line = new RelayedChatLine(kind, 10UL, Guid.Empty, "Bob", "hello", FromActivePlayer: false);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_team_dedup_entry_does_not_suppress_a_clan_line()
    {
        var (relay, poster, dedup, _, _) = Build();
        dedup.Record(ChatChannelKind.Team, (10UL, Guid.Empty), "[Alice] hello");
        var line = new RelayedChatLine(ChatChannelKind.Clan, 10UL, Guid.Empty, "BotPlayer", "[Alice] hello",
            FromActivePlayer: true);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1).PostAsync(ChatChannelKind.Clan, 10UL, ClanChannel, "BotPlayer", "[Alice] hello",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_clan_dedup_entry_does_not_suppress_a_team_line()
    {
        var (relay, poster, dedup, _, _) = Build();
        dedup.Record(ChatChannelKind.Clan, (10UL, Guid.Empty), "[Alice] hello");
        var line = new RelayedChatLine(ChatChannelKind.Team, 10UL, Guid.Empty, "BotPlayer", "[Alice] hello",
            FromActivePlayer: true);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1).PostAsync(ChatChannelKind.Team, 10UL, TeamChannel, "BotPlayer", "[Alice] hello",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_a_clan_line_to_the_clan_channel_not_the_team_channel()
    {
        var (relay, poster, _, team, _) = Build();
        var line = new RelayedChatLine(ChatChannelKind.Clan, 10UL, Guid.Empty, "Bob", "hello",
            FromActivePlayer: false);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1)
            .PostAsync(ChatChannelKind.Clan, 10UL, ClanChannel, "Bob", "hello", Arg.Any<CancellationToken>());
        await poster.DidNotReceive().PostAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), TeamChannel,
            Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await team.DidNotReceive().GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_a_team_line_to_the_team_channel_not_the_clan_channel()
    {
        var (relay, poster, _, _, clan) = Build();
        var line = new RelayedChatLine(ChatChannelKind.Team, 10UL, Guid.Empty, "Bob", "hello",
            FromActivePlayer: false);

        await relay.RelayAsync(line, CancellationToken.None);

        await poster.Received(1)
            .PostAsync(ChatChannelKind.Team, 10UL, TeamChannel, "Bob", "hello", Arg.Any<CancellationToken>());
        await clan.DidNotReceive().GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
