using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class ChatRegistrationTests
{
    [Fact]
    public async Task Services_Resolve_And_Share_A_Single_Dedup_Buffer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<ITeamChatChannelLocator>());
        services.AddSingleton(Substitute.For<ITeamChatSender>());
        services.AddLogging();
        services.AddChat();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var relay = provider.GetRequiredService<TeamChatRelay>();
        var processor = provider.GetRequiredService<TeamChatInboundProcessor>();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.NotNull(relay);
        Assert.NotNull(processor);
        Assert.Contains(hostedServices, h => h is ChatHostedService);

        // The relay and the inbound processor MUST share the same dedup buffer instance for the
        // record/consume echo pairing to work; the registration uses a single AddSingleton.
        Assert.Same(
            provider.GetRequiredService<RelayDedupBuffer>(),
            provider.GetRequiredService<RelayDedupBuffer>());
    }
}
