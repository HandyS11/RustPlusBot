using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line upkeep-cost reply.</summary>
internal static class UpkeepLine
{
    /// <summary>Formats the per-resource upkeep cost for a building block.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Upkeep"/>).</param>
    /// <param name="names">Resolves resource item ids to names.</param>
    public static string Format(ItemRecord item, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(item.Upkeep);

        var parts = item.Upkeep.Entries.Select(e =>
        {
            var quantity = e.QuantityMin == e.QuantityMax
                ? e.QuantityMin.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{e.QuantityMin}–{e.QuantityMax}");
            return string.Create(CultureInfo.InvariantCulture, $"{quantity} {names.Resolve(e.ItemId)}");
        });
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name} upkeep: {string.Join(", ", parts)}");
    }
}
