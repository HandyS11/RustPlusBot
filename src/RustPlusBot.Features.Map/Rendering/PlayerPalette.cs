using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One palette entry: the on-map cross color and the matching Discord legend square.</summary>
/// <param name="Rgba">The cross color drawn on the map.</param>
/// <param name="Emoji">The colored-square emoji used for this color in the legend.</param>
public readonly record struct PlayerColor(Color Rgba, string Emoji);

/// <summary>
/// The fixed nine-color player palette. Each color's RGB matches its Discord colored-square emoji so
/// an on-map cross and its legend row read as the same color. Players are assigned by stable index
/// (see the composer), wrapping past nine.
/// </summary>
public static class PlayerPalette
{
    /// <summary>The palette, ordered for maximum distinctiveness in the first slots.</summary>
    public static IReadOnlyList<PlayerColor> Entries { get; } =
    [
        new(Color.ParseHex("E03131"), "🟥"), // red
        new(Color.ParseHex("1971C2"), "🟦"), // blue
        new(Color.ParseHex("2F9E44"), "🟩"), // green
        new(Color.ParseHex("F2CC0C"), "🟨"), // yellow
        new(Color.ParseHex("9C36B5"), "🟪"), // purple
        new(Color.ParseHex("E8590C"), "🟧"), // orange
        new(Color.ParseHex("8B5E34"), "🟫"), // brown
        new(Color.ParseHex("212529"), "⬛"), // black
        new(Color.ParseHex("F1F3F5"), "⬜"), // white
    ];

    /// <summary>Gets the palette entry for a zero-based player index, wrapping past the palette length.</summary>
    /// <param name="index">The stable zero-based player index.</param>
    /// <returns>The palette entry.</returns>
    public static PlayerColor For(int index) => Entries[((index % Entries.Count) + Entries.Count) % Entries.Count];
}
