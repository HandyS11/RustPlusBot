using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Chat.Hosting;

/// <summary>Runs the game→Discord relay loops (team and clan) and the Discord→game chat channel listener.</summary>
/// <param name="client">The Discord socket client (for MessageReceived).</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays received in-game chat lines into Discord.</param>
/// <param name="processor">Processes Discord chat channel messages for relay into the game.</param>
/// <param name="scopeFactory">Opens a scope to record clan sender names via the scoped <see cref="IClanStore"/>.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ChatHostedService(
    DiscordSocketClient client,
    IEventBus eventBus,
    ChatRelay relay,
    ChatInboundProcessor processor,
    IServiceScopeFactory scopeFactory,
    ILogger<ChatHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _teamLoop;
    private Task? _clanLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += OnMessageReceivedAsync;
        _teamLoop = Task.Run(() => ConsumeTeamMessagesAsync(_cts.Token), CancellationToken.None);
        _clanLoop = Task.Run(() => ConsumeClanMessagesAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= OnMessageReceivedAsync;
        await _cts.CancelAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — these are our own loop tasks, joined on stop.
        await JoinLoopAsync(_teamLoop).ConfigureAwait(false);
        await JoinLoopAsync(_clanLoop).ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private static async Task JoinLoopAsync(Task? loop)
    {
        if (loop is null)
        {
            return;
        }

        try
        {
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — this is our own loop task, joined on stop.
            await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ConsumeTeamMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<TeamMessageReceivedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.RelayAsync(
                    new RelayedChatLine(ChatChannelKind.Team, evt.GuildId, evt.ServerId, evt.SenderName,
                        evt.Message, evt.FromActivePlayer), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogTeamRelayLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeClanMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ClanMessageReceivedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                // Clan members arrive as Steam ids only; chat is where we learn their names. A failure here
                // is isolated so it cannot take down the relay loop below: a missed name is cosmetic, a
                // dead relay loop silences clan chat until the process restarts.
                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using (scope.ConfigureAwait(false))
                    {
                        var store = scope.ServiceProvider.GetRequiredService<IClanStore>();
                        await store.RecordNameAsync(evt.GuildId, evt.ServerId, evt.SenderSteamId, evt.SenderName,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
#pragma warning disable CA1031 // Broad catch: a name-recording failure must not kill the clan relay loop.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogClanNameRecordFailed(logger, ex);
                }

                await relay.RelayAsync(
                    new RelayedChatLine(ChatChannelKind.Clan, evt.GuildId, evt.ServerId, evt.SenderName,
                        evt.Message, evt.FromActivePlayer), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogClanRelayLoopFaulted(logger, ex);
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            var displayName = (message.Author as SocketGuildUser)?.DisplayName ?? message.Author.Username;
            var inbound = new InboundMessage(
                message.Author.IsBot || message.Author.IsWebhook,
                message.Channel.Id,
                displayName,
                message.Content);

            var outcome = await processor.ProcessAsync(inbound, _cts.Token).ConfigureAwait(false);
            if (outcome == InboundOutcome.Failed && message is IUserMessage userMessage)
            {
                await userMessage.AddReactionAsync(new Emoji("❌")).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a listener failure must not crash the gateway dispatcher.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogListenerFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Team message relay loop faulted.")]
    private static partial void LogTeamRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clan message relay loop faulted.")]
    private static partial void LogClanRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording the clan sender's display name failed.")]
    private static partial void LogClanNameRecordFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat inbound listener faulted.")]
    private static partial void LogListenerFaulted(ILogger logger, Exception exception);
}
