using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One marker to draw, already projected to pixel coordinates.</summary>
/// <param name="Kind">The marker kind (selects the icon).</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
/// <param name="Rotation">Heading in degrees as sent by the server, or null for no rotation.</param>
/// <param name="Trail">Recent positions in output pixels, oldest first; empty for no trail.</param>
public sealed record MarkerPlacement(
    MarkerKind Kind,
    float PixelX,
    float PixelY,
    float? Rotation,
    IReadOnlyList<PointF> Trail);
