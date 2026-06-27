using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!decay — shows an item's decay time and HP.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class DecayCommandHandler(IItemDatabase database, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "decay";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found { Item.Decay: not null } f =>
                localizer.Get("command.decay.ok", context.Culture, DecayLine.Format(f.Item)),
            ItemMatch.Found f => localizer.Get("command.decay.none", context.Culture, f.Item.Name),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
