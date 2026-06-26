using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line craft-recipe reply.</summary>
internal static class CraftLine
{
    /// <summary>Formats the ingredients and craft time for an item.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Craft"/>).</param>
    /// <param name="names">Resolves ingredient item ids to names.</param>
    public static string Format(ItemRecord item, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(item.Craft);
        var parts = item.Craft.Ingredients
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{names.Resolve(g.ItemId)} ×{g.Quantity}"));
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name}: {string.Join(", ", parts)}");
    }
}
