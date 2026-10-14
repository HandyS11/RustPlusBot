using System.Globalization;
using System.Text.Json;
using RustMapsApi.V4;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Rendering;
using SkiaSharp;

if (args.Length < 4
    || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
    || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
{
    await Console.Error.WriteLineAsync("Usage: map-parity <worldSize> <seed> <apiKey> <outDir>").ConfigureAwait(false);
    return 1;
}

var apiKey = args[2];
var outDir = Directory.CreateDirectory(args[3]).FullName;

#pragma warning disable S1075 // Dev-only CLI tool: the RustMaps API root is fixed, not app configuration.
using var http = new HttpClient
{
    BaseAddress = new Uri("https://api.rustmaps.com")
};
#pragma warning restore S1075
http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
var client = new RustMapsClient(http); // verified: RustMapsClient has a single HttpClient primary ctor

var result = await client.GetMapBySeedAndSizeAsync(size, seed, staging: false).ConfigureAwait(false);
if (!result.IsSuccess || result.Data is not { } map)
{
    await Console.Error.WriteLineAsync(
            $"RustMaps lookup failed (status {result.StatusCode}): {result.Error?.Message}")
        .ConfigureAwait(false);
    return 2;
}

await Console.Out.WriteLineAsync(
        $"Map {map.Id}: ImageUrl={map.ImageUrl} RawImageUrl={map.RawImageUrl} monuments={map.TotalMonuments}")
    .ConfigureAwait(false);
var imageBytes = await http.GetByteArrayAsync(new Uri(map.ImageUrl!)).ConfigureAwait(false);
await File.WriteAllBytesAsync(Path.Combine(outDir, "rustmaps-render.png"), imageBytes).ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(outDir, "monuments.json"),
        JsonSerializer.Serialize(map, new JsonSerializerOptions
        {
            WriteIndented = true
        }))
    .ConfigureAwait(false);

// Project every monument through OUR MapProjection onto THEIR render and drop crosshairs.
// If our math is right, every crosshair lands on the matching RustMaps monument icon.
using var overlay = SKBitmap.Decode(imageBytes);
var projection =
    new MapProjection((uint)size, overlay.Width, overlay.Height, OceanMarginPx: 0, OutputSize: overlay.Width);
using (var canvas = new SKCanvas(overlay))
using (var pen = new SKPaint
       {
           IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = SKColors.Magenta
       })
{
    foreach (var monument in map.Monuments ?? [])
    {
        if (monument.Coordinates is not { } c)
        {
            continue;
        }

        // RustMaps coordinates are centred on the map; MapProjection expects Rust+'s corner origin.
        var (px, py) = projection.ToPixel(c.X + (size / 2f), c.Y + (size / 2f));
        canvas.DrawLine(px - 12, py, px + 12, py, pen);
        canvas.DrawLine(px, py - 12, px, py + 12, pen);
        Console.WriteLine(
            $"{monument.Type,-30} world=({c.X,6},{c.Y,6}) "
            + $"grid={MapGrid.LabelFor(c.X + (size / 2f), c.Y + (size / 2f), (uint)size)} px=({px:F0},{py:F0})");
    }
}

using (var png = overlay.Encode(SKEncodedImageFormat.Png, 100))
{
    await File.WriteAllBytesAsync(Path.Combine(outDir, "overlay.png"), png.ToArray()).ConfigureAwait(false);
}

await Console.Out.WriteLineAsync($"Wrote {outDir}/overlay.png — crosshairs must sit on the RustMaps monument markers.")
    .ConfigureAwait(false);
return 0;
