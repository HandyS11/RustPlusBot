using RustPlusBot.Localization;

namespace RustPlusBot.Abstractions.Vending;

/// <summary>
/// The one place that decides how a blueprint listing is told apart from the plain item. <see
/// cref="ListingKey"/> treats "the blueprint of X" and "X" as different listings, so a rival selling the
/// blueprint of a Metal Pipe for 5 scrap and one selling the pipe itself for 40 must never render as two
/// identical rows — the blueprint would read as an unmissable bargain. Every surface that shows a
/// listing goes through here; four verbatim copies of this rule is exactly what let two of them drift.
/// </summary>
public static class ListingDisplay
{
    /// <summary>The localization key of the blueprint indicator, a single-argument prefix format.</summary>
    public const string BlueprintKey = "vending.listing.blueprint";

    /// <summary>
    /// Marks a rendered listing fragment as being for the item's blueprint. Callers normally pass the
    /// already-resolved item name — which keeps this helper independent of the two different name
    /// resolvers the Vending and Commands assemblies use — but a surface with no item-name slot of its
    /// own (the in-game <c>!vending</c> reply names the item once, above the rows) passes the whole
    /// fragment instead, so the marker still lands somewhere the reader will see it.
    /// </summary>
    /// <param name="text">The already-rendered text to mark: usually the plain item name.</param>
    /// <param name="isBlueprint">True when the listing is for the item's blueprint.</param>
    /// <param name="localizer">The shared localizer.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>
    /// <paramref name="text"/> unchanged when this is not a blueprint listing, otherwise the localized
    /// blueprint indicator applied to it. Currency is never marked: it is never a blueprint by ruling.
    /// </returns>
    public static string MarkBlueprint(string text, bool isBlueprint, ILocalizer localizer, string culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return isBlueprint ? localizer.Get(BlueprintKey, culture, text) : text;
    }
}
