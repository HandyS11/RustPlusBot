using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Assets;
using SixLabors.ImageSharp;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MarkerGlyphsTests
{
    [Theory]
    [InlineData(MarkerKind.CargoShip, "C")]
    [InlineData(MarkerKind.PatrolHelicopter, "H")]
    [InlineData(MarkerKind.Chinook, "K")]
    public void Known_kinds_have_distinct_letters(MarkerKind kind, string expected)
    {
        var (_, letter) = MarkerGlyphs.For(kind);
        Assert.Equal(expected, letter);
    }

    [Fact]
    public void Unknown_kind_falls_back_to_question_mark()
    {
        var (color, letter) = MarkerGlyphs.For(MarkerKind.Other);
        Assert.Equal("?", letter);
        Assert.NotEqual(default, color);
    }
}
