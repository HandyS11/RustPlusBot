using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class MapGridTests
{
    [Theory]
    [InlineData(1500u, 11)] // 1500 / 146.25 = 10.26 -> 10 whole + 1 partial edge cell = 11 (A-K, rows 0-10)
    [InlineData(3000u, 21)] // 3000 / 146.25 = 20.51 -> 20 whole + 1 partial = 21
    [InlineData(3500u, 24)] // 3500 / 146.25 = 23.93 -> 23 whole + 1 partial = 24
    [InlineData(4250u, 30)] // 4250 / 146.25 = 29.06 -> 29 whole + 1 partial = 30
    [InlineData(4500u, 31)] // 4500 / 146.25 = 30.77 -> 30 whole + 1 partial = 31
    public void CellCount_covers_the_world_including_the_partial_edge_cell(uint worldSize, int expected) =>
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
        // 4000 world: 4000 / 146.25 = 27.35 -> 27 whole + 1 partial = 28 cells.
        // World (0,0) = SW corner = column A, bottom row (cells - 1 = 27).
        Assert.Equal("A27", MapGrid.LabelFor(0f, 0f, 4000u));
    }

    [Fact]
    public void LabelFor_north_west_corner_is_A0()
    {
        Assert.Equal("A0", MapGrid.LabelFor(0f, 3999f, 4000u));
    }

    [Fact]
    public void LabelFor_rows_bin_from_the_north_edge()
    {
        // 1500 world -> 11 cells with a 37.5-unit partial edge cell. y = 1360 is 140 units below the
        // north edge: row 0 with north-anchored rows (in-game / app / RustMaps behaviour); binning from
        // the south would put it in row 1. Locks in the anchoring.
        Assert.Equal("A0", MapGrid.LabelFor(0f, 1360f, 1500u));
    }

    [Fact]
    public void LabelFor_south_edge_falls_in_the_partial_row()
    {
        // 1500 world: the southernmost 37.5 units are the partial row 10.
        Assert.Equal("A10", MapGrid.LabelFor(0f, 10f, 1500u));
    }

    [Fact]
    public void LabelFor_beyond_world_size_clamps_to_last_cell()
    {
        // Regression for the old GridReference bug: clamping against IMAGE pixels, not world units.
        // 4000 world -> 28 cells (see LabelFor_origin_is_bottom_left_last_row); last column index 27 = "AB".
        Assert.Equal("AB27", MapGrid.LabelFor(4500f, 0f, 4000u));
    }

    [Fact]
    public void LabelFor_rustplus_style_shifts_rows_100_units_south()
    {
        // Rust+/RustMaps rows start 100 units below the north edge (measured from RustMaps' own grid
        // tiles). y = 830 on a 1500 world: in-game row = floor((1500-830)/146.25) = 4; Rust+ row =
        // floor((1400-830)/146.25) = 3. Columns are identical in both styles.
        Assert.Equal("A4", MapGrid.LabelFor(0f, 830f, 1500u));
        Assert.Equal("A3", MapGrid.LabelFor(0f, 830f, 1500u, MapGridStyle.RustPlus));
    }

    [Fact]
    public void LabelFor_rustplus_style_clamps_the_top_strip_to_row_0()
    {
        // The 100 units above the Rust+ row anchor still read as row 0 (best effort).
        Assert.Equal("A0", MapGrid.LabelFor(0f, 1450f, 1500u, MapGridStyle.RustPlus));
    }

    [Theory]
    [InlineData(MapGridStyle.InGame, 0f)]
    [InlineData(MapGridStyle.RustPlus, 100f)]
    public void RowInset_is_zero_in_game_and_100_for_rustplus(MapGridStyle style, float expected) =>
        Assert.Equal(expected, MapGrid.RowInset(style));
}
