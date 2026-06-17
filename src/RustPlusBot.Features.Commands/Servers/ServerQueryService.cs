using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Servers;

/// <summary>
/// Runs an in-game command handler for the slash-command path. Builds a synthetic context (no in-game
/// caller, no args) and delegates to the existing handler so slash and in-game replies are identical.
/// </summary>
internal sealed class ServerQueryService
{
    private readonly Dictionary<string, ICommandHandler> _handlers;

    /// <summary>Initializes the service from the registered handlers.</summary>
    /// <param name="handlers">The available in-game command handlers.</param>
    public ServerQueryService(IEnumerable<ICommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.Ordinal);
    }

    /// <summary>Runs the named handler against a server and returns its localized reply.</summary>
    /// <param name="commandName">The handler name (e.g. "pop").</param>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reply text, or null if the command is unknown or the handler produced no reply.</returns>
    public async Task<string?> RunAsync(
        string commandName,
        ulong guildId,
        Guid serverId,
        string culture,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(commandName, out var handler))
        {
            return null;
        }

        var context = new CommandContext(guildId, serverId, culture, 0UL, string.Empty, []);
        return await handler.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
