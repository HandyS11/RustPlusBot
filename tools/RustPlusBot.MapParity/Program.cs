using System.Globalization;
using System.Text.Json;
using RustMapsApi.V4;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length < 4)
{
    await Console.Error.WriteLineAsync("Usage: map-parity <worldSize> <seed> <apiKey> <outDir>").ConfigureAwait(false);
    return 1;
}

var size = int.Parse(args[0], CultureInfo.InvariantCulture);
var seed = int.Parse(args[1], CultureInfo.InvariantCulture);
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
using var overlay = Image.Load<Rgba32>(imageBytes);
var projection =
    new MapProjection((uint)size, overlay.Width, overlay.Height, OceanMarginPx: 0, OutputSize: overlay.Width);
overlay.Mutate(ctx =>
{
    foreach (var monument in map.Monuments ?? [])
    {
        if (monument.Coordinates is not { } c)
        {
            continue;
        }

        var (px, py) = projection.ToPixel(c.X, c.Y);
        ctx.DrawLine(Color.Magenta, 2f, new PointF(px - 12, py), new PointF(px + 12, py));
        ctx.DrawLine(Color.Magenta, 2f, new PointF(px, py - 12), new PointF(px, py + 12));
        Console.WriteLine(
            $"{monument.Type,-30} world=({c.X,6},{c.Y,6}) grid={MapGrid.LabelFor(c.X, c.Y, (uint)size)} px=({px:F0},{py:F0})");
    }
});
await overlay.SaveAsPngAsync(Path.Combine(outDir, "overlay.png")).ConfigureAwait(false);
await Console.Out.WriteLineAsync($"Wrote {outDir}/overlay.png — crosshairs must sit on the RustMaps monument markers.")
    .ConfigureAwait(false);
return 0;
