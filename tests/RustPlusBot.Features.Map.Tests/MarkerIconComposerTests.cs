using RustPlusBot.Features.Map.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MarkerIconComposerTests
{
    [Fact]
    public void Heli_has_one_rotor()
    {
        Assert.Single(MarkerIconComposer.HeliRotorOffsets);
        using var heli = MarkerIconComposer.Heli();
        Assert.True(heli.Width > 0 && heli.Height > 0);
    }

    [Fact]
    public void Chinook_has_two_rotors_placed_apart()
    {
        Assert.Equal(2, MarkerIconComposer.ChinookRotorOffsets.Count);
        using var chinook = MarkerIconComposer.Chinook();

        // One rotor in the top half of the body, one in the bottom half (tandem placement).
        var midY = chinook.Height / 2f;
        Assert.Contains(MarkerIconComposer.ChinookRotorOffsets, p => p.Y < midY);
        Assert.Contains(MarkerIconComposer.ChinookRotorOffsets, p => p.Y > midY);
    }

    [Fact]
    public void Composited_chinook_adds_blade_pixels_to_the_bare_body()
    {
        // The composite must differ from the bare body — proving blades were actually drawn on.
        using var composite = MarkerIconComposer.Chinook();
        using var body = LoadEmbedded("chinook");

        Assert.Equal(body.Width, composite.Width);
        Assert.Equal(body.Height, composite.Height);
        var changed = 0;
        for (var y = 0; y < body.Height; y++)
        {
            for (var x = 0; x < body.Width; x++)
            {
                if (body[x, y] != composite[x, y])
                {
                    changed++;
                }
            }
        }

        Assert.True(changed > 0, "composite identical to bare body — no blades drawn");
    }

    private static Image<Rgba32> LoadEmbedded(string key)
    {
        var asm = typeof(MarkerIconComposer).Assembly;
        using var stream = asm.GetManifestResourceStream($"RustPlusBot.Features.Map.Assets.icons.{key}.png")!;
        return Image.Load<Rgba32>(stream);
    }
}
