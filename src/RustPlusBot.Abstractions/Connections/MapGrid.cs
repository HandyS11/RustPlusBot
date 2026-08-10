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

    /// <summary>Tests whether a coordinate falls outside the playable world.</summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>True when either axis is beyond <c>[0, worldSize]</c>; the exact edges count as inside.</returns>
    public static bool IsOutsideWorld(float x, float y, uint worldSize) =>
        x < 0f || y < 0f || x > worldSize || y > worldSize;

    /// <summary>
    /// Tests whether a coordinate sits at the map border — outside the world, or within one grid cell
    /// of any edge. Marker positions are sampled by polling, so a marker that has just crossed the
    /// border is usually still reported slightly inside it; the one-cell band absorbs that lag.
    /// </summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>
    /// True when the coordinate is outside the world or within <see cref="CellSize"/> of an edge. On a
    /// world smaller than <c>2 * CellSize</c> (292.5 units) the north/south and east/west bands overlap
    /// and every position reports as border; Rust's minimum map size is 1000, so this is unreachable in
    /// practice, and it fails safe — it can only misclassify a crash as a departure, never the reverse.
    /// </returns>
    public static bool IsAtOrBeyondBorder(float x, float y, uint worldSize) =>
        IsOutsideWorld(x, y, worldSize)
        || x < CellSize
        || y < CellSize
        || x > worldSize - CellSize
        || y > worldSize - CellSize;

    /// <summary>Bins the bearing from the world centre to a coordinate into an 8-point compass direction.</summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>
    /// The compass sector containing the coordinate. Sectors are 45° wide and centred on each compass
    /// point, so due north spans 337.5°–22.5°. A coordinate exactly at the centre yields
    /// <see cref="MapDirection.North"/>; that cannot arise for a real off-map marker.
    /// </returns>
    public static MapDirection DirectionFrom(float x, float y, uint worldSize)
    {
        var centre = worldSize / 2f;

        // Atan2(east, north) gives a bearing measured clockwise from north, which is the order the
        // MapDirection values are declared in.
        var bearing = MathF.Atan2(x - centre, y - centre) * (180f / MathF.PI);
        if (bearing < 0f)
        {
            bearing += 360f;
        }

        // Shift by half a sector so the bins straddle each compass point rather than starting at it.
        return (MapDirection)(int)MathF.Floor((bearing + 22.5f) % 360f / 45f);
    }
}
