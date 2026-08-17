using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!vending &lt;item&gt; — the three best offers for an item, in stock first.</summary>
/// <param name="readModel">The live vending index.</param>
/// <param name="items">The bundled item database, for name resolution.</param>
/// <param name="names">Resolves currency ids to display names.</param>
/// <param name="mapSettings">Supplies the server's grid convention.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class VendingCommandHandler(
    IVendingReadModel readModel,
    IItemDatabase items,
    IItemNameResolver names,
    IMapSettingsStore mapSettings,
    ILocalizer localizer) : ICommandHandler
{
    private const int MaxOffers = 3;

    /// <inheritdoc />
    public string Name => "vending";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Args.Count == 0)
        {
            return localizer.Get("command.vending.usage", context.Culture);
        }

        var match = items.Resolve(string.Join(' ', context.Args));
        if (match is ItemMatch.NotFound)
        {
            return localizer.Get("command.item.notfound", context.Culture, string.Join(' ', context.Args));
        }

        if (match is ItemMatch.Ambiguous ambiguous)
        {
            return localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", ambiguous.Candidates.Select(c => c.Name)));
        }

        var item = ((ItemMatch.Found)match).Item;

        // No index entry means no poll has landed for this server since the socket last came up.
        if (!readModel.HasData(context.GuildId, context.ServerId))
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        var settings = await mapSettings.GetAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        var offers = readModel.Search(context.GuildId, context.ServerId, item.Id, settings.GridStyle);
        if (offers.Count == 0)
        {
            return localizer.Get("command.vending.none", context.Culture, item.Name);
        }

        var lines = offers
            .Take(MaxOffers)
            .Select(o => VendingLine.Format(o, names, localizer, context.Culture));
        return localizer.Get("command.vending.ok", context.Culture, item.Name, string.Join(" · ", lines));
    }
}
