using System.Globalization;
using System.Text;

namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// Rust map grid math shared by the map renderer and grid-reference formatting.
/// One cell is 146.25 game units; the grid is anchored at the world's NORTH-WEST corner
/// (Rust / RustMaps / companion-app behaviour): columns are lettered west→east from A, rows are
/// numbered north→south from 0, and the partial edge cell (when the world size is not a whole
/// multiple of the cell size) sits along the south and east edges.
/// </summary>
public static class MapGrid
{
    /// <summary>Edge length of one (whole) grid cell, in game units.</summary>
    public const float CellSize = 146.25f;

    /// <summary>
    /// How far south of the world's north edge the Rust+/RustMaps grid rows start, in game units.
    /// Measured exactly (100.0) from RustMaps' own pre-rendered grid tiles across map sizes
    /// 1500–4500; the in-game (F1) map uses no inset. Columns have no inset in either style.
    /// </summary>
    public const float RustPlusRowInset = 100f;

    /// <summary>Gets the row inset (south of the world's north edge) for a grid style.</summary>
    /// <param name="style">The grid style.</param>
    /// <returns>The inset in game units.</returns>
    public static float RowInset(MapGridStyle style) =>
        style == MapGridStyle.RustPlus ? RustPlusRowInset : 0f;

    /// <summary>
    /// Number of grid cells per axis, covering the whole world size — <c>ceil(worldSize / CellSize)</c>.
    /// The last cell (east-most column / south-most row) is a partial edge cell whenever the world size
    /// is not a whole multiple of <see cref="CellSize"/>; it is still a labelled cell, matching how Rust,
    /// the Rust+ companion app and RustMaps draw the grid. (A 1500 world → 11 cells: A–K, rows 0–10.)
    /// </summary>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>The cell count (at least 1).</returns>
    public static int CellCount(uint worldSize)
    {
        var full = (int)(worldSize / CellSize);
        var hasPartial = worldSize - (full * CellSize) > 0.5f;
        return Math.Max(1, hasPartial ? full + 1 : full);
    }

    /// <summary>Formats a spreadsheet-style column label (0→A … 25→Z, 26→AA …).</summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The column letters.</returns>
    public static string ColumnLetters(int index)
    {
        var sb = new StringBuilder();
        var n = index;
        do
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        } while (n >= 0);

        return sb.ToString();
    }

    /// <summary>Formats the grid label ("D7") for a world coordinate.</summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against (defaults to the in-game map).</param>
    /// <returns>The grid label, rows numbered from the top; out-of-world coordinates clamp to the edge cell.</returns>
    public static string LabelFor(float x, float y, uint worldSize, MapGridStyle style = MapGridStyle.InGame)
    {
        // Rows bin from the style's row anchor (north edge in-game; 100 units south of it for
        // Rust+/RustMaps), not the south — with a partial edge cell the two directions disagree.
        var cells = CellCount(worldSize);
        var col = Math.Clamp((int)MathF.Floor(x / CellSize), 0, cells - 1);
        var row = Math.Clamp((int)MathF.Floor((worldSize - RowInset(style) - y) / CellSize), 0, cells - 1);
        return string.Create(CultureInfo.InvariantCulture, $"{ColumnLetters(col)}{row}");
    }
}
