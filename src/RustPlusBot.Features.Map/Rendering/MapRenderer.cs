using System.Globalization;
using RustPlusBot.Features.Connections.Listening;
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

    private const float GridDiameter = 146.25f;
    private const float PlayerRadius = 6f;
    private const float OutlinePenWidth = 1f;
    private const float ActiveRingWidth = 3f;
    private const float PlayerLabelOffset = 12f;

    private static readonly Font Font = LoadFont();

    private static Font LoadFont()
    {
        var asm = typeof(MapRenderer).Assembly;
        using var stream = asm.GetManifestResourceStream("RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf")
                           ?? throw new InvalidOperationException(
                               "Embedded map font 'RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf' not found.");
        var collection = new FontCollection();
        var family = collection.Add(stream, CultureInfo.InvariantCulture);
        return family.CreateFont(12f);
    }

    /// <summary>Renders the map tile plus the requested overlay layers to PNG bytes.</summary>
    /// <param name="baseJpeg">The raw base-map JPEG bytes.</param>
    /// <param name="dims">The map dimensions (world size + ocean margin).</param>
    /// <param name="markers">Marker placements already projected to pixel coordinates.</param>
    /// <param name="monuments">Monument placements already projected to pixel coordinates.</param>
    /// <param name="players">Player placements already projected to pixel coordinates.</param>
    /// <param name="rigs">Oil-rig placements already projected to pixel coordinates.</param>
    /// <param name="layers">Which overlay layers to draw.</param>
    /// <returns>PNG-encoded bytes of a square image with <see cref="OutputSize"/> pixels on each side.</returns>
    /// <remarks>Kept as an instance method so the class can be registered as a DI singleton.</remarks>
#pragma warning disable CA1822, S2325 // Kept as instance method for DI singleton registration
    public byte[] Render(byte[] baseJpeg,
        MapDimensions dims,
        IReadOnlyList<MarkerPlacement> markers,
        IReadOnlyList<MonumentPlacement> monuments,
        IReadOnlyList<PlayerPlacement> players,
        IReadOnlyList<RigPlacement> rigs,
        MapLayerSet layers)
#pragma warning restore CA1822, S2325
    {
        ArgumentNullException.ThrowIfNull(baseJpeg);
        ArgumentNullException.ThrowIfNull(dims);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(monuments);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(rigs);
        ArgumentNullException.ThrowIfNull(layers);

        using var image = Image.Load<Rgba32>(baseJpeg);
        image.Mutate(ctx => ctx.Resize(OutputSize, OutputSize));

        if (layers.Grid)
        {
            DrawGrid(image, dims);
        }

        if (layers.Monuments)
        {
            DrawMonuments(image, monuments);
        }

        if (layers.Markers)
        {
            foreach (var marker in markers)
            {
                DrawMarker(image, marker);
            }
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

    private static void DrawGrid(Image<Rgba32> image, MapDimensions dims)
    {
        var gridColor = Color.FromRgba(255, 255, 255, 80);

        image.Mutate(ctx =>
        {
            // Vertical lines step across the X axis (width); horizontal lines step across the Y axis
            // (height). Driving each axis from its own dimension keeps the grid correct on a
            // non-square map (Rust maps are square today, so Width == Height in practice).
            for (var worldX = 0f; worldX <= dims.Width; worldX += GridDiameter)
            {
                var (vx, _) = WorldToPixel.ToPixel(worldX, 0f, dims, OutputSize);
                ctx.DrawLine(gridColor, OutlinePenWidth, new PointF(vx, 0), new PointF(vx, OutputSize));
            }

            for (var worldY = 0f; worldY <= dims.Height; worldY += GridDiameter)
            {
                var (_, hy) = WorldToPixel.ToPixel(0f, worldY, dims, OutputSize);
                ctx.DrawLine(gridColor, OutlinePenWidth, new PointF(0, hy), new PointF(OutputSize, hy));
            }
        });
    }

    private static void DrawMarker(Image<Rgba32> image, MarkerPlacement m)
    {
        var icon = MapIcons.Marker(m.Kind);
        if (icon is null)
        {
            return;
        }

        image.Mutate(ctx => ctx.DrawImage(icon, CenterAt(m.PixelX, m.PixelY, icon), 1f));
    }

    private static void DrawMonuments(Image<Rgba32> image, IReadOnlyList<MonumentPlacement> monuments)
    {
        foreach (var monument in monuments)
        {
            var icon = MapIcons.Monument(monument.Token);
            if (icon is null)
            {
                continue;
            }

            image.Mutate(ctx => ctx.DrawImage(icon, CenterAt(monument.PixelX, monument.PixelY, icon), 1f));
        }
    }

    private static void DrawRigs(Image<Rgba32> image, IReadOnlyList<RigPlacement> rigs)
    {
        foreach (var rig in rigs)
        {
            var icon = MapIcons.Rig(rig.Kind, rig.Active);
            if (icon is null)
            {
                continue;
            }

            image.Mutate(ctx =>
            {
                ctx.DrawImage(icon, CenterAt(rig.PixelX, rig.PixelY, icon), 1f);

                if (rig.Active)
                {
                    // Active rigs are in their combat window: ring them in red to flag the danger.
                    var radius = (Math.Max(icon.Width, icon.Height) / 2f) + ActiveRingWidth;
                    var ring = new EllipsePolygon(rig.PixelX, rig.PixelY, radius);
                    ctx.Draw(Color.Red, ActiveRingWidth, ring);
                }
            });
        }
    }

    private static void DrawPlayers(Image<Rgba32> image, IReadOnlyList<PlayerPlacement> players)
    {
        var icon = MapIcons.Player();

        foreach (var player in players)
        {
            var isActive = player is { IsAlive: true, IsOnline: true };

            image.Mutate(ctx =>
            {
                if (icon is not null)
                {
                    ctx.DrawImage(icon, CenterAt(player.PixelX, player.PixelY, icon), 1f);
                }
                else
                {
                    var dotColor = isActive ? Color.LimeGreen : Color.Gray;
                    var dot = new EllipsePolygon(player.PixelX, player.PixelY, PlayerRadius);
                    ctx.Fill(dotColor, dot);
                    ctx.Draw(Color.Black, OutlinePenWidth, dot);
                }

                var label = isActive ? player.Name : player.Name + " (dead)";
                var labelColor = isActive ? Color.White : Color.Gray;
                var textOptions = new RichTextOptions(Font)
                {
                    Origin = new PointF(player.PixelX, player.PixelY + PlayerLabelOffset),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                ctx.DrawText(textOptions, label, labelColor);
            });
        }
    }

    private static Point CenterAt(float x, float y, Image<Rgba32> icon) =>
        new((int)(x - (icon.Width / 2f)), (int)(y - (icon.Height / 2f)));
}
