using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Events.Tests.Formatting;

public sealed class GridReferenceTests
{
    [Fact]
    public void Origin_is_top_left_cell()
    {
        // x near 0, y near top => column A, top row.
        var dims = new MapDimensions(4000u, 4000u, 500);
        var grid = GridReference.From(10f, 3990f, dims);

        Assert.StartsWith("A", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_dimensions_fall_back_to_raw_coordinates()
    {
        var grid = GridReference.From(1234.6f, 5678.4f, dims: null);

        Assert.Equal("(1235, 5678)", grid);
    }

    [Theory]
    [InlineData(0f, 4000f, "A")]
    [InlineData(150f, 4000f, "B")]
    public void Column_letter_advances_every_cell(float x, float y, string expectedColumnPrefix)
    {
        var dims = new MapDimensions(4000u, 4000u, 500);
        var grid = GridReference.From(x, y, dims);
        Assert.StartsWith(expectedColumnPrefix, grid, StringComparison.Ordinal);
    }
}
