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
/// Stateless; safe to register and use as a singleton.
/// </summary>
public sealed class MapRenderer
{
    /// <summary>The square output edge length in pixels.</summary>
    public const int OutputSize = 1024;

    private const float OutlinePenWidth = 1f;
    private const float ActiveRingWidth = 3f;
    private const float PlayerLabelOffset = 12f;

    private static readonly FontFamily Family = LoadFamily();
    private static readonly Font Font = Family.CreateFont(12f);
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
    /// <param name="baseJpeg">The raw base-map JPEG bytes.</param>
    /// <param name="projection">The world-to-pixel projection (world size, base image dims, ocean margin).</param>
    /// <param name="markers">Marker placements already projected to pixel coordinates.</param>
    /// <param name="monuments">Monument placements already projected to pixel coordinates.</param>
    /// <param name="players">Player placements already projected to pixel coordinates.</param>
    /// <param name="rigs">Oil-rig placements already projected to pixel coordinates.</param>
    /// <param name="layers">Which overlay layers to draw.</param>
    /// <returns>PNG-encoded bytes of a square image with <see cref="OutputSize"/> pixels on each side.</returns>
    /// <remarks>Kept as an instance method so the class can be registered as a DI singleton.</remarks>
#pragma warning disable CA1822, S2325 // Kept as instance method for DI singleton registration
    public byte[] Render(byte[] baseJpeg,
        MapProjection projection,
        IReadOnlyList<MarkerPlacement> markers,
        IReadOnlyList<MonumentPlacement> monuments,
        IReadOnlyList<PlayerPlacement> players,
        IReadOnlyList<RigPlacement> rigs,
        MapLayerSet layers)
#pragma warning restore CA1822, S2325
    {
        ArgumentNullException.ThrowIfNull(baseJpeg);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(monuments);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(rigs);
        ArgumentNullException.ThrowIfNull(layers);

        using var image = Image.Load<Rgba32>(baseJpeg);
        image.Mutate(ctx => ctx.Resize(OutputSize, OutputSize));

        if (layers.Grid)
        {
            DrawGrid(image, projection);
        }

        if (layers.Monuments)
        {
            DrawMonuments(image, monuments);
        }

        if (layers.Markers || layers.Vendor)
        {
            DrawTrails(image, markers);
        }

        if (layers.Markers)
        {
            DrawMarkers(image, markers);
        }

        if (layers.Rigs)
        {
            DrawRigs(image, rigs);
        }

        if (layers.Players)
        {
            DrawPlayers(image, players);
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void DrawGrid(Image<Rgba32> image, MapProjection projection)
    {
        if (projection.WorldSize == 0)
        {
            return;
        }

        var lineColor = Color.FromRgba(255, 255, 255, 80);
        var labelColor = Color.FromRgba(255, 255, 255, 140);
        var correctedSize = MapGrid.CorrectedWorldSize(projection.WorldSize);
        var cells = MapGrid.CellCount(projection.WorldSize);
        var (left, top) = projection.ToPixel(0f, correctedSize);
        var (right, bottom) = projection.ToPixel(correctedSize, 0f);

        image.Mutate(ctx =>
        {
            for (var i = 0; i <= cells; i++)
            {
                // correctedSize is an exact multiple of MapGrid.CellSize, so i * CellSize reaches the
                // edge exactly at i == cells — no clamp needed, and no partial final cell.
                var boundary = i * MapGrid.CellSize;
                var (vx, _) = projection.ToPixel(boundary, 0f);
                ctx.DrawLine(lineColor, OutlinePenWidth, new PointF(vx, top), new PointF(vx, bottom));
                var (_, hy) = projection.ToPixel(0f, boundary);
                ctx.DrawLine(lineColor, OutlinePenWidth, new PointF(left, hy), new PointF(right, hy));
            }

            for (var col = 0; col < cells; col++)
            {
                for (var row = 0; row < cells; row++)
                {
                    // Label sits in the cell's centre (in-game map placement): probe the middle of the cell
                    // and centre the text on it, both axes. Anchoring at the top-left corner instead reads
                    // as "half a row too high" because the text hugs the cell's top edge.
                    var worldX = (col + 0.5f) * MapGrid.CellSize;
                    var worldY = correctedSize - ((row + 0.5f) * MapGrid.CellSize);
                    var (lx, ly) = projection.ToPixel(worldX, worldY);
                    var label = MapGrid.ColumnLetters(col) + row.ToString(CultureInfo.InvariantCulture);
                    ctx.DrawText(new RichTextOptions(GridLabelFont)
                        {
                            Origin = new PointF(lx, ly),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
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
                    // Fade from faint (oldest) to strong (newest) so travel direction reads instantly.
                    var alpha = 0.15f + (0.45f * i / (marker.Trail.Count - 1));
                    ctx.DrawLine(baseColor.WithAlpha(alpha), MapRenderStyle.TrailWidth,
                        marker.Trail[i - 1], marker.Trail[i]);
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

    private static void DrawMonuments(Image<Rgba32> image, IReadOnlyList<MonumentPlacement> monuments)
    {
        // One Mutate for the whole layer (see DrawMarkers): monuments can be numerous, so a single
        // pipeline beats one Mutate per monument.
        image.Mutate(ctx =>
        {
            foreach (var monument in monuments)
            {
                var icon = MapIcons.Monument(monument.Token, MapRenderStyle.MonumentIconSize);
                if (icon is not null)
                {
                    ctx.DrawImage(icon, CenterAt(monument.PixelX, monument.PixelY, icon), 1f);
                }
            }
        });
    }

    private static void DrawRigs(Image<Rgba32> image, IReadOnlyList<RigPlacement> rigs)
    {
        image.Mutate(ctx =>
        {
            foreach (var rig in rigs)
            {
                var icon = MapIcons.Rig(rig.Kind, rig.Active, MapRenderStyle.MonumentIconSize);
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
        var icon = MapIcons.Player(MapRenderStyle.PlayerIconSize);

        image.Mutate(ctx =>
        {
            foreach (var player in players)
            {
                DrawPlayerIcon(ctx, player, icon);
                DrawPlayerLabel(ctx, player);
            }
        });
    }

    private static void DrawPlayerIcon(IImageProcessingContext ctx, PlayerPlacement player, Image<Rgba32>? icon)
    {
        if (icon is null)
        {
            return;
        }

        var isActive = player is { IsAlive: true, IsOnline: true };
        // Dead/offline teammates render dimmed so status reads at a glance (label adds the suffix).
        ctx.DrawImage(icon, CenterAt(player.PixelX, player.PixelY, icon), isActive ? 1f : 0.45f);
    }

    private static void DrawPlayerLabel(IImageProcessingContext ctx, PlayerPlacement player)
    {
        var isActive = player is { IsAlive: true, IsOnline: true };
        var suffix = player.IsAlive ? " (offline)" : " (dead)";
        var label = isActive ? player.Name : player.Name + suffix;
        var labelColor = isActive ? Color.White : Color.Gray;
        var textOptions = new RichTextOptions(Font)
        {
            Origin = new PointF(player.PixelX, player.PixelY + PlayerLabelOffset),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        ctx.DrawText(textOptions, label, labelColor);
    }

    private static Point CenterAt(float x, float y, Image<Rgba32> icon) =>
        new((int)(x - (icon.Width / 2f)), (int)(y - (icon.Height / 2f)));
}
