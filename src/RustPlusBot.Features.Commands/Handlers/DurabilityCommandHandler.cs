using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!durability — lists the explosives needed to destroy a target.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves tool ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class DurabilityCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "durability";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.ResolveRaidTarget(query) switch
        {
            RaidMatch.Found f =>
                localizer.Get("command.durability.ok", context.Culture, DurabilityLine.Format(f.Target, names)),
            RaidMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
