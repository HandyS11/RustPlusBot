using System.Globalization;
using System.Text;

namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// Rust map grid math shared by the map renderer and grid-reference formatting.
/// One cell is 146.25 game units (rustplusplus-compatible); rows are numbered from the top.
/// </summary>
public static class MapGrid
{
    /// <summary>Edge length of one grid cell, in game units.</summary>
    public const float CellSize = 146.25f;

    /// <summary>Number of grid cells per axis, including a partial edge cell.</summary>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>The cell count (at least 1).</returns>
    public static int CellCount(uint worldSize) => Math.Max(1, (int)Math.Ceiling(worldSize / CellSize));

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
    /// <param name="x">World X (west→east), clamped into the world.</param>
    /// <param name="y">World Y (south→north), clamped into the world.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>The grid label, rows numbered from the top.</returns>
    public static string LabelFor(float x, float y, uint worldSize)
    {
        var cells = CellCount(worldSize);
        var col = Math.Clamp((int)Math.Floor(Math.Clamp(x, 0f, worldSize - 1) / CellSize), 0, cells - 1);
        var rowFromBottom = Math.Clamp((int)Math.Floor(Math.Clamp(y, 0f, worldSize - 1) / CellSize), 0, cells - 1);
        var row = cells - rowFromBottom - 1;
        return string.Create(CultureInfo.InvariantCulture, $"{ColumnLetters(col)}{row}");
    }
}
