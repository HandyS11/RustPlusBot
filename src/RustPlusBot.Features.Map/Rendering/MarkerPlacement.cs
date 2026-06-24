using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One marker to draw, already projected to pixel coordinates.</summary>
/// <param name="Kind">The marker kind (selects the glyph).</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
public sealed record MarkerPlacement(MarkerKind Kind, float PixelX, float PixelY);
