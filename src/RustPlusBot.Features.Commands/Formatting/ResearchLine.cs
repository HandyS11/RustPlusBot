using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line research-cost reply.</summary>
internal static class ResearchLine
{
    /// <summary>Formats the scrap research cost for an item.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Research"/>).</param>
    public static string Format(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Research);
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name}: {item.Research.Scrap} scrap");
    }
}
