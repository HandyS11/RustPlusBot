using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the multi-line durability (raid-cost) reply for a target.</summary>
internal static class DurabilityLine
{
    /// <summary>Lists each explosive cost for a target, cheapest by sulfur first.</summary>
    /// <param name="target">The raid target (with at least one cost).</param>
    /// <param name="names">Resolves tool item ids to names.</param>
    public static string Format(RaidTarget target, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(names);

        var lines = target.Costs
            .OrderBy(c => c.Sulfur ?? int.MaxValue)
            .ThenBy(c => c.Quantity)
            .Select(c => FormatCost(c, names));
        return string.Create(CultureInfo.InvariantCulture, $"{target.Name}:\n{string.Join("\n", lines)}");
    }

    private static string FormatCost(RaidCost cost, IItemNameResolver names)
    {
        var quantity = (int)Math.Ceiling(cost.Quantity);
        var tool = names.Resolve(cost.ToolId);
        var side = cost.Side is "soft" or "hard"
            ? string.Create(CultureInfo.InvariantCulture, $" ({cost.Side})")
            : string.Empty;
        var sulfur = cost.Sulfur is { } s
            ? string.Create(CultureInfo.InvariantCulture, $" — {s} sulfur")
            : string.Empty;
        var time = cost.TimeSeconds is { } t and > 0
            ? string.Create(CultureInfo.InvariantCulture, $" ({DurationFormat.Seconds(t)})")
            : string.Empty;
        var caption = string.IsNullOrEmpty(cost.Caption)
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" · {cost.Caption}");
        return string.Create(CultureInfo.InvariantCulture, $"{tool} ×{quantity}{side}{sulfur}{time}{caption}");
    }
}
