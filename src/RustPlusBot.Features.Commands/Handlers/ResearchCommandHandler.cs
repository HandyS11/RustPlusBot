using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!research — shows the scrap research cost for an item.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class ResearchCommandHandler(IItemDatabase database, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "research";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found { Item.Research: not null } f =>
                localizer.Get("command.research.ok", context.Culture, ResearchLine.Format(f.Item)),
            ItemMatch.Found f => localizer.Get("command.research.none", context.Culture, f.Item.Name),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
