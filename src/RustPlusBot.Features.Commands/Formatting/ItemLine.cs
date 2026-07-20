using System.Globalization;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line item lookup card.</summary>
internal static class ItemLine
{
    /// <summary>Formats name, id, stack size, and despawn time.</summary>
    /// <param name="item">The item to describe.</param>
    public static string Format(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var despawn = item.DespawnSeconds is { } seconds
            ? DurationFormat.Compact(TimeSpan.FromSeconds(seconds))
            : "—";
        return string.Create(CultureInfo.InvariantCulture,
            $"{item.Name} (id {item.Id}) · stack {item.StackSize} · despawn {despawn}");
    }
}
