using RustPlusBot.Features.Map.Rendering;
using SkiaSharp;

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
    internal static IReadOnlyList<SKPoint> HeliRotorOffsets { get; } = HeliOffsets();

    /// <summary>The two tandem-rotor offsets (front, rear), in body pixels.</summary>
    internal static IReadOnlyList<SKPoint> ChinookRotorOffsets { get; } = ChinookOffsets();

    /// <summary>Builds the patrol-heli composite (body + one rotor blade on the hub).</summary>
    /// <returns>The composited heli icon; caller owns disposal.</returns>
    internal static SKImage Heli() =>
        Compose("heli", HeliRotorOffsets, HeliBladeScale);

    /// <summary>Builds the CH47 composite (body + two rotor blades at the tandem positions).</summary>
    /// <returns>The composited chinook icon; caller owns disposal.</returns>
    internal static SKImage Chinook() =>
        Compose("chinook", ChinookRotorOffsets, ChinookBladeScale);

    private static IReadOnlyList<SKPoint> HeliOffsets()
    {
        using var body = Load("heli");

        // Hub sits slightly forward of centre.
        return [new SKPoint(body.Width / 2f, body.Height * 0.44f)];
    }

    private static IReadOnlyList<SKPoint> ChinookOffsets()
    {
        using var body = Load("chinook");
        var cx = body.Width / 2f;
        return
        [
            new SKPoint(cx, body.Height * 0.20f), // front rotor
            new SKPoint(cx, body.Height * 0.80f), // rear rotor
        ];
    }

    private static SKImage Compose(string bodyKey, IReadOnlyList<SKPoint> offsets, float bladeScale)
    {
        using var body = Load(bodyKey);
        using var blade = Load("blade");
        var edge = (int)(Math.Min(body.Width, body.Height) * bladeScale);

        // Returned to the caller (cached by MapIcons); not disposed here.
        return Raster.Draw(body.Width, body.Height, canvas =>
        {
            canvas.DrawImage(body, 0, 0, SKSamplingOptions.Default);
            foreach (var offset in offsets)
            {
                var left = (int)(offset.X - (edge / 2f));
                var top = (int)(offset.Y - (edge / 2f));
                canvas.DrawImage(blade, SKRect.Create(left, top, edge, edge), Raster.Smooth);
            }
        });
    }

    private static SKImage Load(string key)
    {
        var asm = typeof(MarkerIconComposer).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + key + ".png")
                           ?? throw new InvalidOperationException($"Embedded marker asset '{key}.png' not found.");
        return Raster.Decode(stream);
    }
}
