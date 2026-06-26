using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!item — looks up an item's name, id, stack size, and despawn time.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class ItemCommandHandler(IItemDatabase database, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "item";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found f => localizer.Get("command.item.ok", context.Culture, ItemLine.Format(f.Item)),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
