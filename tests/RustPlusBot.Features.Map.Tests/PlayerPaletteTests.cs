using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

public sealed class PlayerPaletteTests
{
    [Fact]
    public void Has_nine_distinct_colors_with_emoji()
    {
        Assert.Equal(9, PlayerPalette.Entries.Count);
        Assert.Equal(9, PlayerPalette.Entries.Select(e => e.Emoji).Distinct().Count());
        Assert.All(PlayerPalette.Entries, e => Assert.False(string.IsNullOrEmpty(e.Emoji)));
    }

    [Fact]
    public void For_wraps_past_the_palette_length()
    {
        Assert.Equal(PlayerPalette.For(0), PlayerPalette.For(9));
        Assert.Equal(PlayerPalette.For(1), PlayerPalette.For(10));
    }
}
