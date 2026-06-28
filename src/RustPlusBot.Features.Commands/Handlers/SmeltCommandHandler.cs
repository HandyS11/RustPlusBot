using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!smelt — lists what a smelter converts and its fuel/time cost.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves input/output item ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class SmeltCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "smelt";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.ResolveSmelter(query) switch
        {
            SmeltMatch.Found f =>
                localizer.Get("command.smelt.ok", context.Culture, SmeltLine.Format(f.Smelter, names)),
            SmeltMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
