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

    /// <summary>Player cross half-arm length, in output pixels.</summary>
    public const float PlayerCrossArm = 7f;

    /// <summary>Player cross colored-stroke width, in output pixels.</summary>
    public const float PlayerCrossWidth = 2.5f;

    /// <summary>Player cross dark-halo stroke width (drawn under the color for contrast).</summary>
    public const float PlayerCrossHaloWidth = 4.5f;

    /// <summary>Opacity for offline players' crosses.</summary>
    public const float PlayerOfflineAlpha = 0.5f;

    /// <summary>Trail polyline stroke width, in output pixels (thin, so it reads as a faint hint).</summary>
    public const float TrailWidth = 1.25f;

    /// <summary>Opacity ceiling for the newest trail segment (older segments fade below this).</summary>
    public const float TrailMaxAlpha = 0.4f;

    /// <summary>Grid cell label font size, in points.</summary>
    public const float GridLabelFontSize = 10f;

    /// <summary>Dash pattern (on, off, on, off …) for the trail, in multiples of the pen width.</summary>
    public static float[] TrailDash { get; } = [3f, 3f];

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
