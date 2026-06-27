using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!upkeep — shows a building block's upkeep cost.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves resource ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class UpkeepCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "upkeep";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found { Item.Upkeep: not null } f =>
                localizer.Get("command.upkeep.ok", context.Culture, UpkeepLine.Format(f.Item, names)),
            ItemMatch.Found f => localizer.Get("command.upkeep.none", context.Culture, f.Item.Name),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
