using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line recycler-yield reply.</summary>
internal static class RecycleLine
{
    /// <summary>Formats the recycler outputs for an item, or a "not recyclable" sentinel via the caller.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Recycle"/>).</param>
    /// <param name="names">Resolves output item ids to names.</param>
    public static string Format(ItemRecord item, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(item.Recycle);
        var parts = item.Recycle.Recycler
            .Select(y => string.Create(CultureInfo.InvariantCulture, $"{names.Resolve(y.ItemId)} ×{y.Quantity}"));
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name} → {string.Join(", ", parts)}");
    }
}
