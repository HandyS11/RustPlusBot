using System.Globalization;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Assets;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    private static readonly FontFamily Family = LoadFamily();
    private static readonly Font GridLabelFont = Family.CreateFont(MapRenderStyle.GridLabelFontSize);

    private static FontFamily LoadFamily()
    {
        var asm = typeof(MapRenderer).Assembly;
        using var stream = asm.GetManifestResourceStream("RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf")
                           ?? throw new InvalidOperationException(
                               "Embedded map font 'RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf' not found.");
        var collection = new FontCollection();
        return collection.Add(stream, CultureInfo.InvariantCulture);
    }

    /// <summary>Renders the map tile plus the requested overlay layers to PNG bytes.</summary>
    /// <param name="request">The base image, projection, and overlay data to render.</param>
    /// <returns>PNG-encoded bytes of a square image with <see cref="OutputSize"/> pixels on each side.</returns>
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

        using var image = Image.Load<Rgba32>(request.BaseJpeg);
        image.Mutate(ctx => ctx.Resize(OutputSize, OutputSize));

        if (request.Layers.Grid)
        {
            DrawGrid(image, request.Projection, request.GridStyle);
        }

        if (request.Layers.Monuments)
        {
            DrawMonuments(image, request.Monuments);
        }

        if (request.Layers.Tunnels && request.Tunnels is { Count: > 0 })
        {
            DrawMonuments(image, request.Tunnels);
        }

        if (request.Layers.Markers || request.Layers.Vendor)
        {
            DrawTrails(image, request.Markers);
        }

        if (request.Layers.Markers)
        {
            DrawMarkers(image, request.Markers);
        }

        if (request.Layers.Rigs)
        {
            DrawRigs(image, request.Rigs);
        }

        if (request.Layers.Players)
        {
            DrawPlayers(image, request.Players);
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void DrawGrid(Image<Rgba32> image, MapProjection projection, MapGridStyle gridStyle)
    {
        if (projection.WorldSize == 0)
        {
            return;
        }

        var lineColor = Color.FromRgba(255, 255, 255, 80);
        var labelColor = Color.FromRgba(255, 255, 255, 140);
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

        image.Mutate(ctx =>
        {
            for (var k = 0; k <= cells; k++)
            {
                var x = anchorX + (k * stepX);
                ctx.DrawLine(lineColor, OutlinePenWidth, new PointF(x, anchorY), new PointF(x, bottom));
                var y = anchorY + (k * stepY);
                ctx.DrawLine(lineColor, OutlinePenWidth, new PointF(anchorX, y), new PointF(right, y));
            }

            // Every labelled cell (A0 in the north-west corner), each label just inside its cell's
            // top-left corner (companion-app placement).
            for (var col = 0; col < cells; col++)
            {
                for (var row = 0; row < cells; row++)
                {
                    var label = MapGrid.ColumnLetters(col) + row.ToString(CultureInfo.InvariantCulture);
                    ctx.DrawText(new RichTextOptions(GridLabelFont)
                        {
                            Origin = new PointF(anchorX + (col * stepX) + 2f, anchorY + (row * stepY) + 2f)
                        },
                        label, labelColor);
                }
            }
        });
    }

    private static void DrawTrails(Image<Rgba32> image, IReadOnlyList<MarkerPlacement> markers)
    {
        image.Mutate(ctx =>
        {
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
                    // but stays subtle. A dashed pen keeps it from looking like a solid smear.
                    var alpha = MapRenderStyle.TrailMaxAlpha * i / (marker.Trail.Count - 1);
                    var pen = new PatternPen(baseColor.WithAlpha(alpha), MapRenderStyle.TrailWidth,
                        MapRenderStyle.TrailDash);
                    ctx.DrawLine(pen, marker.Trail[i - 1], marker.Trail[i]);
                }
            }
        });
    }

    private static void DrawMarkers(Image<Rgba32> image, IReadOnlyList<MarkerPlacement> markers)
    {
        // One Mutate for the whole layer: each Mutate builds and runs a fresh processing pipeline,
        // so batching all the layer's DrawImage calls into a single Mutate avoids per-item overhead.
        image.Mutate(ctx =>
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
                    using var rotated = icon.Clone(c => c.Rotate(-rotation));
                    ctx.DrawImage(rotated, CenterAt(marker.PixelX, marker.PixelY, rotated), 1f);
                }
                else
                {
                    ctx.DrawImage(icon, CenterAt(marker.PixelX, marker.PixelY, icon), 1f);
                }
            }
        });
    }

    private void DrawMonuments(Image<Rgba32> image, IReadOnlyList<MonumentPlacement> monuments)
    {
        // One Mutate for the whole layer (see DrawMarkers): monuments can be numerous, so a single
        // pipeline beats one Mutate per monument.
        image.Mutate(ctx =>
        {
            foreach (var monument in monuments)
            {
                var icon = monumentIcons.Monument(monument.Token, MapRenderStyle.MonumentIconSize);
                if (icon is not null)
                {
                    ctx.DrawImage(icon, CenterAt(monument.PixelX, monument.PixelY, icon), 1f);
                }
            }
        });
    }

    private void DrawRigs(Image<Rgba32> image, IReadOnlyList<RigPlacement> rigs)
    {
        image.Mutate(ctx =>
        {
            foreach (var rig in rigs)
            {
                var icon = monumentIcons.Rig(rig.Kind, MapRenderStyle.MonumentIconSize);
                if (icon is null)
                {
                    continue;
                }

                ctx.DrawImage(icon, CenterAt(rig.PixelX, rig.PixelY, icon), 1f);

                if (rig.Active)
                {
                    // Active rigs are in their combat window: ring them in red to flag the danger.
                    var radius = (Math.Max(icon.Width, icon.Height) / 2f) + ActiveRingWidth;
                    var ring = new EllipsePolygon(rig.PixelX, rig.PixelY, radius);
                    ctx.Draw(Color.Red, ActiveRingWidth, ring);
                }
            }
        });
    }

    private static void DrawPlayers(Image<Rgba32> image, IReadOnlyList<PlayerPlacement> players)
    {
        image.Mutate(ctx =>
        {
            foreach (var player in players)
            {
                DrawPlayerCross(ctx, player);
            }
        });
    }

    private static void DrawPlayerCross(IImageProcessingContext ctx, PlayerPlacement player)
    {
        const float arm = MapRenderStyle.PlayerCrossArm;
        var alpha = player.IsOnline ? 1f : MapRenderStyle.PlayerOfflineAlpha;
        var color = player.CrossColor.WithAlpha(alpha);
        var halo = Color.FromRgba(0, 0, 0, (byte)(180 * alpha));

        // Alive = '+', dead = 'x'.
        var (a1, a2, b1, b2) = player.IsAlive
            ? (new PointF(player.PixelX - arm, player.PixelY), new PointF(player.PixelX + arm, player.PixelY),
                new PointF(player.PixelX, player.PixelY - arm), new PointF(player.PixelX, player.PixelY + arm))
            : (new PointF(player.PixelX - arm, player.PixelY - arm),
                new PointF(player.PixelX + arm, player.PixelY + arm),
                new PointF(player.PixelX - arm, player.PixelY + arm),
                new PointF(player.PixelX + arm, player.PixelY - arm));

        // Halo first (wider, dark), then the colored strokes on top.
        ctx.DrawLine(halo, MapRenderStyle.PlayerCrossHaloWidth, a1, a2);
        ctx.DrawLine(halo, MapRenderStyle.PlayerCrossHaloWidth, b1, b2);
        ctx.DrawLine(color, MapRenderStyle.PlayerCrossWidth, a1, a2);
        ctx.DrawLine(color, MapRenderStyle.PlayerCrossWidth, b1, b2);
    }

    private static Point CenterAt(float x, float y, Image<Rgba32> icon) =>
        new((int)(x - (icon.Width / 2f)), (int)(y - (icon.Height / 2f)));
}
