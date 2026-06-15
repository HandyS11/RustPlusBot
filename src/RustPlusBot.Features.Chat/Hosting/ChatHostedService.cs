using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;

namespace RustPlusBot.Features.Chat.Hosting;

/// <summary>Runs the game→Discord relay loop and the Discord→game #teamchat listener.</summary>
/// <param name="client">The Discord socket client (for MessageReceived).</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays received team messages into Discord.</param>
/// <param name="processor">Processes Discord #teamchat messages for relay into the game.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ChatHostedService(
    DiscordSocketClient client,
    IEventBus eventBus,
    TeamChatRelay relay,
    TeamChatInboundProcessor processor,
    ILogger<ChatHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _relayLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += OnMessageReceivedAsync;
        _relayLoop = Task.Run(() => ConsumeTeamMessagesAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= OnMessageReceivedAsync;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_relayLoop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — this is our own loop task, joined on stop.
                await _relayLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ConsumeTeamMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<TeamMessageReceivedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.RelayAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogRelayLoopFaulted(logger, ex);
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
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Team chat inbound listener faulted.")]
    private static partial void LogListenerFaulted(ILogger logger, Exception exception);
}
