using System.Globalization;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Assets;
using SkiaSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>
/// Renders the base map tile plus overlay layers to PNG bytes.
/// Stateless per render; safe to register and use as a singleton.
/// </summary>
/// <param name="monumentIcons">Serves monument and rig icons from the RustMaps asset package.</param>
public sealed class MapRenderer(MonumentIconSource monumentIcons)
{
    /// <summary>The square output edge length in pixels.</summary>
    public const int OutputSize = 1024;

    private const float OutlinePenWidth = 1f;
    private const float ActiveRingWidth = 3f;

    private static readonly SKTypeface Typeface = LoadTypeface();

    private static SKTypeface LoadTypeface()
    {
        var asm = typeof(MapRenderer).Assembly;
        using var stream = asm.GetManifestResourceStream("RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf")
                           ?? throw new InvalidOperationException(
                               "Embedded map font 'RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf' not found.");
        return SKTypeface.FromStream(stream)
               ?? throw new InvalidOperationException("Embedded map font could not be loaded.");
    }

    /// <summary>Renders the map tile plus the requested overlay layers to PNG bytes.</summary>
    /// <param name="request">The base image, projection, and overlay data to render.</param>
    /// <returns>PNG-encoded bytes of a square image with <see cref="OutputSize"/> pixels on each side.</returns>
    /// <exception cref="InvalidOperationException">The base image bytes are not a decodable image.</exception>
    public byte[] Render(MapRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BaseJpeg);
        ArgumentNullException.ThrowIfNull(request.Projection);
        ArgumentNullException.ThrowIfNull(request.Markers);
        ArgumentNullException.ThrowIfNull(request.Monuments);
        ArgumentNullException.ThrowIfNull(request.Players);
        ArgumentNullException.ThrowIfNull(request.Rigs);
        ArgumentNullException.ThrowIfNull(request.Layers);

        using var surface = SKSurface.Create(new SKImageInfo(OutputSize, OutputSize));
        var canvas = surface.Canvas;
        using (var baseImage = SKImage.FromEncodedData(request.BaseJpeg)
                               ?? throw new InvalidOperationException("The base map image could not be decoded."))
        {
            canvas.DrawImage(baseImage, SKRect.Create(OutputSize, OutputSize), Raster.Smooth);
        }

        if (request.Layers.Grid)
        {
            DrawGrid(canvas, request.Projection, request.GridStyle);
        }

        if (request.Layers.Monuments)
        {
            DrawMonuments(canvas, request.Monuments);
        }

        if (request.Layers.Tunnels && request.Tunnels is { Count: > 0 })
        {
            DrawMonuments(canvas, request.Tunnels);
        }

        if (request.Layers.Markers || request.Layers.Vendor)
        {
            DrawTrails(canvas, request.Markers);
        }

        if (request.Layers.Markers)
        {
            DrawMarkers(canvas, request.Markers);
        }

        if (request.Layers.Rigs)
        {
            DrawRigs(canvas, request.Rigs);
        }

        if (request.Layers.Players)
        {
            DrawPlayers(canvas, request.Players);
        }

        using var snapshot = surface.Snapshot();
        return Raster.EncodePng(snapshot);
    }

    private static void DrawGrid(SKCanvas canvas, MapProjection projection, MapGridStyle gridStyle)
    {
        if (projection.WorldSize == 0)
        {
            return;
        }

        var cells = MapGrid.CellCount(projection.WorldSize);

        // The lattice is anchored at the west edge and at the style's row anchor (the world's north
        // edge for the in-game style; 100 game-units south of it for Rust+/RustMaps), and is clipped
        // to the LABELLED cell block only (A0 … the last ceil-count cell) — no lines out over the
        // ocean margin. The partial edge cell sits at the south/east and simply bleeds past the world
        // edge, so every rendered cell looks full-size.
        var anchorWorldY = projection.WorldSize - MapGrid.RowInset(gridStyle);
        var (anchorX, anchorY) = projection.ToPixel(0f, anchorWorldY);
        var (cellEndX, cellEndY) = projection.ToPixel(MapGrid.CellSize, anchorWorldY - MapGrid.CellSize);
        var stepX = cellEndX - anchorX;
        var stepY = cellEndY - anchorY;
        if (stepX <= 0f || stepY <= 0f)
        {
            return; // Degenerate projection (no drawable world area).
        }

        var right = anchorX + (cells * stepX);
        var bottom = anchorY + (cells * stepY);

        using var linePaint = Stroke(new SKColor(255, 255, 255, 80), OutlinePenWidth);
        for (var k = 0; k <= cells; k++)
        {
            var x = anchorX + (k * stepX);
            canvas.DrawLine(x, anchorY, x, bottom, linePaint);
            var y = anchorY + (k * stepY);
            canvas.DrawLine(anchorX, y, right, y, linePaint);
        }

        // Every labelled cell (A0 in the north-west corner), each label just inside its cell's
        // top-left corner (companion-app placement). DrawText takes the baseline, so drop each
        // label by the font's ascent to put its top edge at the corner offset.
        // Unhinted, subpixel-positioned glyphs keep the small labels light rather than pixel-snapped bold.
        using var font = new SKFont(Typeface, MapRenderStyle.GridLabelFontSize)
        {
            Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.None, Subpixel = true,
        };
        using var labelPaint = new SKPaint
        {
            IsAntialias = true, Color = new SKColor(255, 255, 255, 140)
        };
        var ascent = -font.Metrics.Ascent;
        for (var col = 0; col < cells; col++)
        {
            for (var row = 0; row < cells; row++)
            {
                var label = MapGrid.ColumnLetters(col) + row.ToString(CultureInfo.InvariantCulture);
                canvas.DrawText(label, anchorX + (col * stepX) + 2f, anchorY + (row * stepY) + 2f + ascent,
                    SKTextAlign.Left, font, labelPaint);
            }
        }
    }

    private static void DrawTrails(SKCanvas canvas, IReadOnlyList<MarkerPlacement> markers)
    {
        // A dashed pen keeps the trail from looking like a solid smear; the dash pattern is in
        // multiples of the pen width.
        using var dash = SKPathEffect.CreateDash(
            [.. MapRenderStyle.TrailDash.Select(d => d * MapRenderStyle.TrailWidth)], 0f);
        using var paint = Stroke(SKColors.Transparent, MapRenderStyle.TrailWidth);
        paint.PathEffect = dash;

        foreach (var marker in markers)
        {
            if (marker.Trail.Count < 2)
            {
                continue;
            }

            var baseColor = MapRenderStyle.TrailColor(marker.Kind);
            for (var i = 1; i < marker.Trail.Count; i++)
            {
                // Fade from faint (oldest) to the alpha ceiling (newest) so direction still reads,
                // but stays subtle.
                var alpha = MapRenderStyle.TrailMaxAlpha * i / (marker.Trail.Count - 1);
                paint.Color = baseColor.WithAlpha(ToByte(alpha));
                canvas.DrawLine(marker.Trail[i - 1], marker.Trail[i], paint);
            }
        }
    }

    private static void DrawMarkers(SKCanvas canvas, IReadOnlyList<MarkerPlacement> markers)
    {
        foreach (var marker in markers)
        {
            var icon = MapIcons.Marker(marker.Kind, MapRenderStyle.MarkerIconSize(marker.Kind));
            if (icon is null)
            {
                continue;
            }

            if (marker.Rotation is { } rotation && Math.Abs(rotation) > 0.01f)
            {
                // Counter-clockwise by the server heading, about the icon's centre.
                canvas.Save();
                canvas.Translate(marker.PixelX, marker.PixelY);
                canvas.RotateDegrees(-rotation);
                canvas.DrawImage(icon, -icon.Width / 2f, -icon.Height / 2f, Raster.Smooth);
                canvas.Restore();
            }
            else
            {
                DrawCentered(canvas, icon, marker.PixelX, marker.PixelY);
            }
        }
    }

    private void DrawMonuments(SKCanvas canvas, IReadOnlyList<MonumentPlacement> monuments)
    {
        foreach (var monument in monuments)
        {
            var icon = monumentIcons.Monument(monument.Token, MapRenderStyle.MonumentIconSize);
            if (icon is not null)
            {
                DrawCentered(canvas, icon, monument.PixelX, monument.PixelY);
            }
        }
    }

    private void DrawRigs(SKCanvas canvas, IReadOnlyList<RigPlacement> rigs)
    {
        using var ringPaint = Stroke(SKColors.Red, ActiveRingWidth);
        foreach (var rig in rigs)
        {
            var icon = monumentIcons.Rig(rig.Kind, MapRenderStyle.MonumentIconSize);
            if (icon is null)
            {
                continue;
            }

            DrawCentered(canvas, icon, rig.PixelX, rig.PixelY);

            if (rig.Active)
            {
                // Active rigs are in their combat window: ring them in red to flag the danger.
                var radius = (Math.Max(icon.Width, icon.Height) / 2f) + ActiveRingWidth;
                canvas.DrawCircle(rig.PixelX, rig.PixelY, radius, ringPaint);
            }
        }
    }

    private static void DrawPlayers(SKCanvas canvas, IReadOnlyList<PlayerPlacement> players)
    {
        using var paint = Stroke(SKColors.Transparent, MapRenderStyle.PlayerCrossWidth);
        foreach (var player in players)
        {
            DrawPlayerCross(canvas, paint, player);
        }
    }

    private static void DrawPlayerCross(SKCanvas canvas, SKPaint paint, PlayerPlacement player)
    {
        const float arm = MapRenderStyle.PlayerCrossArm;
        var alpha = player.IsOnline ? 1f : MapRenderStyle.PlayerOfflineAlpha;
        var color = player.CrossColor.WithAlpha(ToByte(alpha));
        var halo = new SKColor(0, 0, 0, (byte)(180 * alpha));

        // Alive = '+', dead = 'x'.
        var (a1, a2, b1, b2) = player.IsAlive
            ? (new SKPoint(player.PixelX - arm, player.PixelY), new SKPoint(player.PixelX + arm, player.PixelY),
                new SKPoint(player.PixelX, player.PixelY - arm), new SKPoint(player.PixelX, player.PixelY + arm))
            : (new SKPoint(player.PixelX - arm, player.PixelY - arm),
                new SKPoint(player.PixelX + arm, player.PixelY + arm),
                new SKPoint(player.PixelX - arm, player.PixelY + arm),
                new SKPoint(player.PixelX + arm, player.PixelY - arm));

        // Halo first (wider, dark), then the colored strokes on top.
        paint.Color = halo;
        paint.StrokeWidth = MapRenderStyle.PlayerCrossHaloWidth;
        canvas.DrawLine(a1, a2, paint);
        canvas.DrawLine(b1, b2, paint);
        paint.Color = color;
        paint.StrokeWidth = MapRenderStyle.PlayerCrossWidth;
        canvas.DrawLine(a1, a2, paint);
        canvas.DrawLine(b1, b2, paint);
    }

    /// <summary>Draws an icon centred on a point, snapped to whole pixels so the 1:1 blit stays crisp.</summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="icon">The icon, already at its on-map size.</param>
    /// <param name="x">The centre X in output pixels.</param>
    /// <param name="y">The centre Y in output pixels.</param>
    private static void DrawCentered(SKCanvas canvas, SKImage icon, float x, float y) =>
        canvas.DrawImage(icon, (int)(x - (icon.Width / 2f)), (int)(y - (icon.Height / 2f)),
            SKSamplingOptions.Default);

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, Color = color,
    };

    private static byte ToByte(float alpha) => (byte)Math.Round(Math.Clamp(alpha, 0f, 1f) * 255f);
}
