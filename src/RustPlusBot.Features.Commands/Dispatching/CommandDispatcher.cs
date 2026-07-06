using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>Turns a received in-game line into a parsed, authorized, rate-limited command + reply.</summary>
internal sealed partial class CommandDispatcher
{
    private static readonly HashSet<string> MuteExempt = new(StringComparer.Ordinal)
    {
        "mute", "unmute"
    };

    private readonly CommandCooldown _cooldown;

    private readonly Dictionary<string, ICommandHandler> _handlers;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly IMuteStore _muteStore;
    private readonly IBotTeamChatSender _sender;
    private readonly IWorkspaceStore _workspace;

    /// <summary>Initializes the dispatcher.</summary>
    /// <param name="handlers">The available command handlers.</param>
    /// <param name="cooldown">The per-(server,command) cooldown.</param>
    /// <param name="muteStore">The per-(guild,server) mute and prefix store.</param>
    /// <param name="workspace">The workspace store (for guild culture).</param>
    /// <param name="sender">Relays the reply into in-game team chat.</param>
    /// <param name="logger">The logger.</param>
    public CommandDispatcher(
        IEnumerable<ICommandHandler> handlers,
        CommandCooldown cooldown,
        IMuteStore muteStore,
        IWorkspaceStore workspace,
        IBotTeamChatSender sender,
        ILogger<CommandDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.Ordinal);
        _cooldown = cooldown;
        _muteStore = muteStore;
        _workspace = workspace;
        _sender = sender;
        _logger = logger;
    }

    /// <summary>Dispatches one received team message as a command, if it is one.</summary>
    /// <param name="evt">The received team message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the command has been handled or dropped.</returns>
    public async Task DispatchAsync(TeamMessageReceivedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // Bot-originated lines echo back from the active player carrying the bot prefix; ignoring only
        // those (instead of every active-player line) lets the paired player's own typed commands dispatch.
        if (evt.Message.StartsWith(BotTeamChat.Prefix, StringComparison.Ordinal))
        {
            return;
        }

        var prefix = await _muteStore.GetPrefixAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (!CommandLine.TryParse(prefix, evt.Message, out var line) ||
            !_handlers.TryGetValue(line.Name, out var handler))
        {
            return;
        }

        // Mute is gated before the cooldown so a muted command never consumes a cooldown slot.
        if (!MuteExempt.Contains(line.Name) &&
            await _muteStore.GetMutedAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!_cooldown.TryConsume(evt.ServerId, line.Name))
        {
            return;
        }

        var culture = await _workspace.GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        var context = new CommandContext(evt.GuildId, evt.ServerId, culture, evt.SenderSteamId, evt.SenderName,
            line.Args);

        string? reply;
        try
        {
            reply = await handler.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a faulting command must not crash the dispatch loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogHandlerFaulted(_logger, ex, line.Name);
            return;
        }

        if (!string.IsNullOrEmpty(reply))
        {
            await _sender.SendAsync(evt.GuildId, evt.ServerId, reply, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Command handler '{Command}' faulted.")]
    private static partial void LogHandlerFaulted(ILogger logger, Exception exception, string command);
}
