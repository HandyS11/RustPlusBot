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
    [Fact]
    public async Task Services_resolve()
    {
        await using var provider = BuildProvider(out _, out _, out _);

        Assert.NotNull(provider.GetRequiredService<TeamChatRelay>());
        Assert.NotNull(provider.GetRequiredService<TeamChatInboundProcessor>());
        Assert.Contains(provider.GetServices<IHostedService>(), h => h is ChatHostedService);
    }

    [Fact]
    public async Task Relay_and_processor_share_the_dedup_buffer()
    {
        const ulong guild = 10UL;
        const ulong channelId = 777UL;
        var server = Guid.NewGuid();

        await using var provider = BuildProvider(out var locator, out var sender, out var poster);
        locator.ResolveAsync(channelId, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(guild, server));
        locator.GetChannelIdAsync(guild, server, Arg.Any<CancellationToken>()).Returns((ulong?)channelId);
        sender.SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ChatSendResult.Sent);

        var processor = provider.GetRequiredService<TeamChatInboundProcessor>();
        var relay = provider.GetRequiredService<TeamChatRelay>();

        // The processor records the exact relayed line "[Alice] hi" into the dedup buffer it was constructed with.
        await processor.ProcessAsync(new InboundMessage(false, channelId, "Alice", "hi"), CancellationToken.None);

        // The bot player's echo of that same line must be dropped by the relay — which only happens if the
        // relay was constructed with the SAME buffer instance (a per-service buffer would miss and post).
        await relay.RelayAsync(
            new TeamMessageReceivedEvent(guild, server, 555UL, "BotPlayer", "[Alice] hi", FromActivePlayer: true),
            CancellationToken.None);

        await poster.DidNotReceive()
            .PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ServiceProvider BuildProvider(
        out IChatChannelLocator locator,
        out IChatSender sender,
        out ITeamChatWebhookPoster poster)
    {
        locator = Substitute.For<IChatChannelLocator>();
        sender = Substitute.For<IChatSender>();
        poster = Substitute.For<ITeamChatWebhookPoster>();

        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(locator);
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
