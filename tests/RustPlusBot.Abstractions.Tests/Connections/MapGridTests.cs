using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class MapGridTests
{
    [Theory]
    [InlineData(3000u, 21)] // 3000 / 146.25 = 20.51 -> 21 (partial edge cell counts)
    [InlineData(3500u, 24)] // 23.93 -> 24
    [InlineData(4250u, 30)] // 29.06 -> 30
    [InlineData(4500u, 31)] // 30.77 -> 31
    public void CellCount_ceils_partial_edge_cells(uint worldSize, int expected) =>
        Assert.Equal(expected, MapGrid.CellCount(worldSize));

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    public void ColumnLetters_is_spreadsheet_style(int index, string expected) =>
        Assert.Equal(expected, MapGrid.ColumnLetters(index));

    [Fact]
    public void LabelFor_origin_is_bottom_left_last_row()
    {
        // 4000 world -> 28 cells (27.35 ceil). World (0,0) = SW corner = column A, bottom row 27.
        Assert.Equal("A27", MapGrid.LabelFor(0f, 0f, 4000u));
    }

    [Fact]
    public void LabelFor_north_west_corner_is_A0()
    {
        Assert.Equal("A0", MapGrid.LabelFor(0f, 3999f, 4000u));
    }

    [Fact]
    public void LabelFor_beyond_world_size_clamps_to_last_cell()
    {
        // Regression for the old GridReference bug: clamping against IMAGE pixels, not world units.
        Assert.Equal("AB27", MapGrid.LabelFor(4500f, 0f, 4000u)); // col 27 = "AB"
    }
}
