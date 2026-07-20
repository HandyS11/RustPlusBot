using System.Globalization;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line decay reply.</summary>
internal static class DecayLine
{
    /// <summary>Formats base decay, any present environment variant, and HP for an item.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Decay"/>).</param>
    public static string Format(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Decay);
        var decay = item.Decay;

        var parts = new List<string>();
        Add(parts, null, decay.Seconds);
        Add(parts, "outside", decay.OutsideSeconds);
        Add(parts, "inside", decay.InsideSeconds);
        Add(parts, "underwater", decay.UnderwaterSeconds);

        var decayPart = parts.Count > 0 ? string.Join(", ", parts) : "—";
        var hp = decay.Hp is { } h
            ? string.Create(CultureInfo.InvariantCulture, $" · {h} HP")
            : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name} decays in {decayPart}{hp}");

        static void Add(List<string> into, string? label, int? seconds)
        {
            if (seconds is not { } s)
            {
                return;
            }

            var compact = DurationFormat.Compact(TimeSpan.FromSeconds(s));
            into.Add(label is null
                ? compact
                : string.Create(CultureInfo.InvariantCulture, $"{label} {compact}"));
        }
    }
}
