using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>All on-map sizing and styling constants, expressed in pixels at the 1024px output.</summary>
public static class MapRenderStyle
{
    /// <summary>Monument icon box edge, in output pixels (~3% of the output edge).</summary>
    public const int MonumentIconSize = 30;

    /// <summary>Default event-marker icon box edge, in output pixels.</summary>
    public const int EventIconSize = 36;

    /// <summary>Cargo-ship icon box edge, in output pixels (slightly larger — it is a big target).</summary>
    public const int CargoIconSize = 40;

    /// <summary>Player icon box edge, in output pixels.</summary>
    public const int PlayerIconSize = 20;

    /// <summary>Trail polyline stroke width, in output pixels.</summary>
    public const float TrailWidth = 2f;

    /// <summary>Grid cell label font size, in points.</summary>
    public const float GridLabelFontSize = 10f;

    /// <summary>Gets the icon box edge for a marker kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The box edge length in output pixels.</returns>
    public static int MarkerIconSize(MarkerKind kind) =>
        kind == MarkerKind.CargoShip ? CargoIconSize : EventIconSize;

    /// <summary>Gets the trail color for a marker kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The base (fully opaque) trail color.</returns>
    public static Color TrailColor(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => Color.ParseHex("4FC3F7"),
        MarkerKind.PatrolHelicopter => Color.ParseHex("EF5350"),
        MarkerKind.Chinook => Color.ParseHex("FFB74D"),
        MarkerKind.TravellingVendor => Color.ParseHex("81C784"),
        _ => Color.White,
    };
}
