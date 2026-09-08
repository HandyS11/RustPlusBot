using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Chat.Tests.Hosting;

/// <summary>
/// Covers the Discord→game half of the bridge: the gateway listener <see cref="ChatHostedService"/> attaches
/// in StartAsync. The socket client is a concrete class with no seam, so MessageReceived is raised by walking
/// its subscriber list — which is also what proves the handler was attached, and detached again on stop.
/// </summary>
public sealed class ChatInboundListenerTests
{
    private const ulong Guild = 10UL;
    private const ulong TeamChannel = 555UL;
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public async Task A_message_in_a_chat_channel_is_relayed_into_the_game()
    {
        var (client, service, sender) = Build();
        await service.StartAsync(default);
        try
        {
            await RaiseMessageReceivedAsync(client, Message("dave", "hello", TeamChannel));
        }
        finally
        {
            await service.StopAsync(default);
        }

        await sender.Received(1)
            .SendAsync(ChatChannelKind.Team, Guild, Server, "[dave] hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_bots_own_relayed_message_is_not_sent_back_into_the_game()
    {
        // The bot posts the in-game lines into the same channel it listens to. Without this gate every
        // relayed line would be echoed straight back, and the two halves of the bridge would feed each other.
        var (client, service, sender) = Build();
        await service.StartAsync(default);
        try
        {
            await RaiseMessageReceivedAsync(client, Message("RustPlusBot", "[bob] hi", TeamChannel, isBot: true));
        }
        finally
        {
            await service.StopAsync(default);
        }

        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_message_in_a_channel_no_locator_claims_is_ignored()
    {
        var (client, service, sender) = Build();
        await service.StartAsync(default);
        try
        {
            await RaiseMessageReceivedAsync(client, Message("dave", "hello", 4242UL));
        }
        finally
        {
            await service.StopAsync(default);
        }

        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_message_the_listener_cannot_read_does_not_stop_it_reading_the_next_one()
    {
        // The handler runs on Discord.Net's gateway dispatcher; letting an exception out of it would take
        // down the dispatch of every later message, not just this one.
        var (client, service, sender) = Build();
        var unreadable = (SocketMessage)RuntimeHelpers.GetUninitializedObject(typeof(SocketUserMessage));

        await service.StartAsync(default);
        try
        {
            await RaiseMessageReceivedAsync(client, unreadable);
            await RaiseMessageReceivedAsync(client, Message("dave", "hello", TeamChannel));
        }
        finally
        {
            await service.StopAsync(default);
        }

        await sender.Received(1)
            .SendAsync(ChatChannelKind.Team, Guild, Server, "[dave] hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task After_StopAsync_the_listener_is_no_longer_attached_to_the_gateway()
    {
        var (client, service, sender) = Build();
        await service.StartAsync(default);
        await service.StopAsync(default);

        await RaiseMessageReceivedAsync(client, Message("dave", "hello", TeamChannel));

        await sender.DidNotReceive().SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_guild_members_server_nickname_is_what_the_game_sees()
    {
        // In game the line is prefixed with who said it, and a guild nickname is how that person is known
        // on this server — their raw account username may be nothing anyone there recognises.
        var (client, service, sender) = Build();
        // A guild member reads its username and bot flag through the shared global user behind it.
        var globalUserType = typeof(SocketUser).Assembly.GetType("Discord.WebSocket.SocketGlobalUser")!;
        var globalUser = RuntimeHelpers.GetUninitializedObject(globalUserType);
        SetAutoProperty(globalUser, "Username", "dave_1998");
        SetAutoProperty(globalUser, "IsBot", false);
        var member = RuntimeHelpers.GetUninitializedObject(typeof(SocketGuildUser));
        SetAutoProperty(member, "GlobalUser", globalUser);
        SetAutoProperty(member, "Nickname", "Dave the Builder");

        await service.StartAsync(default);
        try
        {
            await RaiseMessageReceivedAsync(client, Message(member, "hello", TeamChannel));
        }
        finally
        {
            await service.StopAsync(default);
        }

        await sender.Received(1).SendAsync(ChatChannelKind.Team, Guild, Server, "[Dave the Builder] hello",
            Arg.Any<CancellationToken>());
    }

    private static (DiscordSocketClient Client, ChatHostedService Service, IChatSender Sender) Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);

        var locator = Substitute.For<IChatChannelLocator>();
        locator.Kind.Returns(ChatChannelKind.Team);
        locator.ResolveAsync(TeamChannel, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(Guild, Server));

        var muteStore = Substitute.For<IMuteStore>();
        muteStore.GetMutedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        muteStore.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("!");

        var services = new ServiceCollection();
        services.AddScoped(_ => muteStore);
        services.AddScoped(_ => Substitute.For<IClanStore>());
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var sender = Substitute.For<IChatSender>();
        sender.SendAsync(Arg.Any<ChatChannelKind>(), Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(ChatSendResult.Sent);

        var client = new DiscordSocketClient();
        var service = new ChatHostedService(
            client,
            new InMemoryEventBus(),
            new ChatRelay([locator], Substitute.For<Webhooks.IChatWebhookPoster>(), dedup, scopeFactory,
                NullLogger<ChatRelay>.Instance),
            new ChatInboundProcessor([locator], sender, dedup, scopeFactory),
            scopeFactory,
            NullLogger<ChatHostedService>.Instance);
        return (client, service, sender);
    }

    /// <summary>
    /// Builds a <see cref="SocketMessage"/> without Discord.Net's internal factories: the listener only ever
    /// reads the author, the channel id and the content, and none of those objects can be constructed.
    /// </summary>
    /// <param name="username">The author's username.</param>
    /// <param name="content">The message text.</param>
    /// <param name="channelId">The channel the message was posted in.</param>
    /// <param name="isBot">Whether the author is a bot.</param>
    /// <returns>A message the listener can read.</returns>
    private static SocketMessage Message(string username, string content, ulong channelId, bool isBot = false)
    {
        var author = RuntimeHelpers.GetUninitializedObject(typeof(SocketUnknownUser));
        SetAutoProperty(author, "Username", username);
        SetAutoProperty(author, "IsBot", isBot);
        return Message(author, content, channelId);
    }

    /// <summary>Builds a message from an already-fabricated author.</summary>
    /// <param name="author">The author object to attach.</param>
    /// <param name="content">The message text.</param>
    /// <param name="channelId">The channel the message was posted in.</param>
    /// <returns>A message the listener can read.</returns>
    private static SocketMessage Message(object author, string content, ulong channelId)
    {
        var channel = Substitute.For<ISocketMessageChannel>();
        channel.Id.Returns(channelId);

        var message = RuntimeHelpers.GetUninitializedObject(typeof(SocketUserMessage));
        SetAutoProperty(message, "Author", author);
        SetAutoProperty(message, "Channel", channel);
        SetAutoProperty(message, "Content", content);
        return (SocketMessage)message;
    }

    /// <summary>Invokes every handler attached to the client's MessageReceived event.</summary>
    /// <param name="client">The gateway client whose subscribers to run.</param>
    /// <param name="message">The message to dispatch.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <exception cref="InvalidOperationException">Discord.Net no longer names that backing field.</exception>
    private static async Task RaiseMessageReceivedAsync(DiscordSocketClient client, SocketMessage message)
    {
        var field = client.GetType()
                        .GetField("_messageReceivedEvent", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Discord.Net no longer declares _messageReceivedEvent.");
        var asyncEvent = field.GetValue(client)!;
        foreach (Func<SocketMessage, Task> handler in
                 (IEnumerable)asyncEvent.GetType().GetProperty("Subscriptions")!.GetValue(asyncEvent)!)
        {
            await handler(message);
        }
    }

    /// <summary>Assigns an auto-property's compiler-generated backing field, wherever it is declared.</summary>
    /// <param name="target">The instance to write to.</param>
    /// <param name="name">The auto-property's name.</param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="InvalidOperationException">No such auto-property exists on the type.</exception>
    private static void SetAutoProperty(object target, string name, object value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                field.SetValue(target, value);
                return;
            }
        }

        throw new InvalidOperationException($"Discord.Net no longer declares {name} as an auto-property.");
    }
}
