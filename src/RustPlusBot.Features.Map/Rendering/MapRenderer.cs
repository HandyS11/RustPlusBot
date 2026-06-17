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
    private const float MarkerRadius = 9f;
    private const float OutlinePenWidth = 1f;

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
    /// <param name="layers">Which overlay layers to draw.</param>
    /// <returns>PNG-encoded bytes of a square image with <see cref="OutputSize"/> pixels on each side.</returns>
    /// <remarks>Kept as an instance method so the class can be registered as a DI singleton.</remarks>
#pragma warning disable CA1822, S2325 // Kept as instance method for DI singleton registration
    public byte[] Render(byte[] baseJpeg,
        MapDimensions dims,
        IReadOnlyList<MarkerPlacement> markers,
        MapLayerSet layers)
#pragma warning restore CA1822, S2325
    {
        ArgumentNullException.ThrowIfNull(baseJpeg);
        ArgumentNullException.ThrowIfNull(dims);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(layers);

        using var image = Image.Load<Rgba32>(baseJpeg);
        image.Mutate(ctx => ctx.Resize(OutputSize, OutputSize));

        if (layers.Grid)
        {
            DrawGrid(image, dims);
        }

        if (layers.Markers)
        {
            foreach (var marker in markers)
            {
                DrawMarker(image, marker);
            }
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
            for (var world = 0f; world <= dims.Width; world += GridDiameter)
            {
                var (vx, _) = WorldToPixel.ToPixel(world, 0f, dims, OutputSize);
                ctx.DrawLine(gridColor, OutlinePenWidth, new PointF(vx, 0), new PointF(vx, OutputSize));

                var (_, hy) = WorldToPixel.ToPixel(0f, world, dims, OutputSize);
                ctx.DrawLine(gridColor, OutlinePenWidth, new PointF(0, hy), new PointF(OutputSize, hy));
            }
        });
    }

    private static void DrawMarker(Image<Rgba32> image, MarkerPlacement m)
    {
        var (color, letter) = MarkerGlyphs.For(m.Kind);
        var circle = new EllipsePolygon(m.PixelX, m.PixelY, MarkerRadius);

        image.Mutate(ctx =>
        {
            ctx.Fill(color, circle);
            ctx.Draw(Color.Black, OutlinePenWidth, circle);

            var textOptions = new RichTextOptions(Font)
            {
                Origin = new PointF(m.PixelX, m.PixelY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ctx.DrawText(textOptions, letter, Color.White);
        });
    }
}
