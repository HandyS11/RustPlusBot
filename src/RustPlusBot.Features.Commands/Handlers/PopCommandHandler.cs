using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!pop — reports current population, slot cap and queue.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class PopCommandHandler(IRustServerQuery query, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "pop";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var info = await query.GetServerInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (info is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        return localizer.Get("command.pop.ok", context.Culture, info.Players, info.MaxPlayers, info.QueuedPlayers);
    }
}
