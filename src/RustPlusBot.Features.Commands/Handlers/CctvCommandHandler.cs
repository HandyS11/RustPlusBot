using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!cctv — lists a monument's Computer Station CCTV camera codes.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class CctvCommandHandler(IItemDatabase database, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "cctv";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.ResolveCctv(query) switch
        {
            CctvMatch.Found f => Render(f.Monument, localizer, context.Culture),
            CctvMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }

    private static string Render(CctvMonument monument, ILocalizer localizer, string culture)
    {
        var body = localizer.Get("command.cctv.ok", culture, CctvLine.Format(monument));
        return monument.Dynamic
            ? $"{body}\n\n{localizer.Get("command.cctv.note", culture)}"
            : body;
    }
}
