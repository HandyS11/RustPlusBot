using System.Globalization;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Events.Formatting;

/// <summary>Converts world coordinates to a Rust map grid reference (e.g. "D7"), rustplusplus-compatible.</summary>
public static class GridReference
{
    /// <summary>Formats a grid reference, or raw rounded coordinates when <paramref name="dims"/> is null.</summary>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <returns>A grid reference like "D7", or "(x, y)" when dimensions are unavailable.</returns>
    public static string From(float x, float y, MapDimensions? dims)
    {
        if (dims is null || dims.WorldSize == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"({Math.Round(x)}, {Math.Round(y)})");
        }

        return MapGrid.LabelFor(x, y, dims.WorldSize);
    }
}
