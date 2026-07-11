using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Builds the patrol-heli and CH47 marker icons by compositing a grey body with rotor blade(s):
/// the heli gets one blade on its hub, the CH47 gets two at its tandem-rotor positions. Blades are
/// drawn into a copy of the body canvas, so the composite keeps the body's dimensions and rotates
/// cleanly about its centre in <see cref="MapRenderer"/>. Offsets/scale are tuned against the
/// reference art (verify visually on a rendered tile).
/// </summary>
internal static class MarkerIconComposer
{
    private const string ResourcePrefix = "RustPlusBot.Features.Map.Assets.icons.";

    /// <summary>Main-rotor blade edge as a fraction of the body's shorter edge; spans past the fuselage.</summary>
    private const float HeliBladeScale = 1.15f;

    /// <summary>Each tandem-rotor blade edge as a fraction of the body's shorter edge; smaller than the body.</summary>
    private const float ChinookBladeScale = 0.62f;

    /// <summary>The single main-rotor hub offset, in body pixels (populated on first access).</summary>
    internal static IReadOnlyList<PointF> HeliRotorOffsets { get; } = HeliOffsets();

    /// <summary>The two tandem-rotor offsets (front, rear), in body pixels.</summary>
    internal static IReadOnlyList<PointF> ChinookRotorOffsets { get; } = ChinookOffsets();

    /// <summary>Builds the patrol-heli composite (body + one rotor blade on the hub).</summary>
    /// <returns>The composited heli icon; caller owns disposal.</returns>
    internal static Image<Rgba32> Heli() =>
        Compose("heli", HeliRotorOffsets, HeliBladeScale);

    /// <summary>Builds the CH47 composite (body + two rotor blades at the tandem positions).</summary>
    /// <returns>The composited chinook icon; caller owns disposal.</returns>
    internal static Image<Rgba32> Chinook() =>
        Compose("chinook", ChinookRotorOffsets, ChinookBladeScale);

    private static IReadOnlyList<PointF> HeliOffsets()
    {
        using var body = Load("heli");

        // Hub sits slightly forward of centre.
        return [new PointF(body.Width / 2f, body.Height * 0.44f)];
    }

    private static IReadOnlyList<PointF> ChinookOffsets()
    {
        using var body = Load("chinook");
        var cx = body.Width / 2f;
        return
        [
            new PointF(cx, body.Height * 0.20f), // front rotor
            new PointF(cx, body.Height * 0.80f), // rear rotor
        ];
    }

    private static Image<Rgba32> Compose(string bodyKey, IReadOnlyList<PointF> offsets, float bladeScale)
    {
        var canvas = Load(bodyKey); // returned to caller (cached by MapIcons); not disposed here
        using var blade = Load("blade");
        var edge = (int)(Math.Min(canvas.Width, canvas.Height) * bladeScale);
        using var scaledBlade = blade.Clone(c => c.Resize(edge, edge));

        canvas.Mutate(ctx =>
        {
            foreach (var offset in offsets)
            {
                var topLeft = new Point(
                    (int)(offset.X - (scaledBlade.Width / 2f)),
                    (int)(offset.Y - (scaledBlade.Height / 2f)));
                ctx.DrawImage(scaledBlade, topLeft, 1f);
            }
        });

        return canvas;
    }

    private static Image<Rgba32> Load(string key)
    {
        var asm = typeof(MarkerIconComposer).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + key + ".png")
                           ?? throw new InvalidOperationException($"Embedded marker asset '{key}.png' not found.");
        return Image.Load<Rgba32>(stream);
    }
}
