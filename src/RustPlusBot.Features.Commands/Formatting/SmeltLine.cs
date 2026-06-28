using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the multi-line smelting reply for a smelter, one row per conversion (source order).</summary>
internal static class SmeltLine
{
    /// <summary>Lists each conversion a smelter performs.</summary>
    /// <param name="smelter">The smelter (with at least one conversion).</param>
    /// <param name="names">Resolves input/output item ids to names.</param>
    public static string Format(Smelter smelter, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(smelter);
        ArgumentNullException.ThrowIfNull(names);

        var lines = smelter.Conversions.Select(c => FormatConversion(c, names));
        return string.Create(CultureInfo.InvariantCulture, $"{smelter.Name}:\n{string.Join("\n", lines)}");
    }

    private static string FormatConversion(SmeltConversion conversion, IItemNameResolver names)
    {
        var input = names.Resolve(conversion.InputId);
        var output = names.Resolve(conversion.OutputId);
        var quantity = conversion.OutputQuantity > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{conversion.OutputQuantity}× ")
            : string.Empty;
        var probability = conversion.OutputProbability < 1
            ? string.Create(CultureInfo.InvariantCulture, $" ({conversion.OutputProbability:0.#%})")
            : string.Empty;
        var fuel = conversion.WoodQuantity > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{conversion.WoodQuantity:0.##} wood")
            : "no fuel";
        return string.Create(CultureInfo.InvariantCulture,
            $"{input} → {quantity}{output}{probability} — {DurationFormat.Seconds(conversion.TimeSeconds)} · {fuel}");
    }
}
