using System.Globalization;
using System.Text;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.Formatting;

/// <summary>Converts world coordinates to a Rust map grid reference (e.g. "D7"), rustplusplus-compatible.</summary>
public static class GridReference
{
    private const float GridDiameter = 146.25f;

    /// <summary>Formats a grid reference, or raw rounded coordinates when <paramref name="dims"/> is null.</summary>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <returns>A grid reference like "D7", or "(x, y)" when dimensions are unavailable.</returns>
    public static string From(float x, float y, MapDimensions? dims)
    {
        if (dims is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"({Math.Round(x)}, {Math.Round(y)})");
        }

        var mapSize = dims.Width;
        var columns = (int)Math.Ceiling(mapSize / GridDiameter);
        var col = (int)Math.Floor(Math.Clamp(x, 0f, mapSize - 1) / GridDiameter);
        // Rows are numbered from the TOP; world Y increases upward, so invert.
        var rowFromBottom = (int)Math.Floor(Math.Clamp(y, 0f, mapSize - 1) / GridDiameter);
        var row = Math.Max(0, columns - rowFromBottom - 1);

        return string.Create(CultureInfo.InvariantCulture, $"{ColumnLetters(col)}{row}");
    }

    private static string ColumnLetters(int index)
    {
        // 0->A .. 25->Z, 26->AA .. (spreadsheet-style, matching rustplusplus past Z).
        var sb = new StringBuilder();
        var n = index;
        do
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        } while (n >= 0);

        return sb.ToString();
    }
}
