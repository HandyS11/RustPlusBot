using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class MapGridTests
{
    [Theory]
    [InlineData(3000u, 20)] // remainder 3000 % 146.25 = 75 < 120 -> round down to 2925 = 20 * 146.25
    [InlineData(3500u, 24)] // remainder 3500 % 146.25 = 136.25 >= 120 -> round up to 3510 = 24 * 146.25
    [InlineData(4250u, 29)] // remainder 4250 % 146.25 = 8.75 < 120 -> round down to 4241.25 = 29 * 146.25
    [InlineData(4500u, 30)] // remainder 4500 % 146.25 = 112.5 < 120 -> round down to 4387.5 = 30 * 146.25
    public void CellCount_snaps_to_whole_cells(uint worldSize, int expected) =>
        Assert.Equal(expected, MapGrid.CellCount(worldSize));

    [Theory]
    [InlineData(3000u, 2925f)] // round-down case: remainder 75 < 120
    [InlineData(3500u, 3510f)] // round-up case: remainder 136.25 >= 120
    public void CorrectedWorldSize_snaps_to_a_whole_multiple_of_cell_size(uint worldSize, float expected) =>
        Assert.Equal(expected, MapGrid.CorrectedWorldSize(worldSize));

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
        // 4000 world: remainder 4000 % 146.25 = 51.25 < 120 -> corrected 3948.75 -> 27 cells.
        // World (0,0) = SW corner = column A, bottom row (cells - 1 = 26).
        Assert.Equal("A26", MapGrid.LabelFor(0f, 0f, 4000u));
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
        // 4000 world -> 27 cells (see LabelFor_origin_is_bottom_left_last_row); last column index 26 = "AA".
        Assert.Equal("AA26", MapGrid.LabelFor(4500f, 0f, 4000u));
    }
}
