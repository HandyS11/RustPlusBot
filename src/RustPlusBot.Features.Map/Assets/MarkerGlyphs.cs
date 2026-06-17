using RustPlusBot.Features.Connections.Listening;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>Maps a marker kind to a draw glyph (colour + single letter). The keyed registry the map renderer draws from; 2b-ii can swap this for image assets without changing the renderer.</summary>
public static class MarkerGlyphs
{
    /// <summary>Gets the glyph colour and letter for a marker kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The colour and a one-character label.</returns>
    public static (Color Color, string Letter) For(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => (Color.DodgerBlue, "C"),
        MarkerKind.PatrolHelicopter => (Color.Red, "H"),
        MarkerKind.Chinook => (Color.Orange, "K"),
        _ => (Color.Gray, "?"),
    };
}
