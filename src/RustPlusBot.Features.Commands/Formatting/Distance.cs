namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Computes planar distances between map coordinates for in-game replies.</summary>
internal static class Distance
{
    /// <summary>Euclidean distance between two map points, rounded to whole metres.</summary>
    /// <param name="x1">First point's horizontal coordinate.</param>
    /// <param name="y1">First point's vertical coordinate.</param>
    /// <param name="x2">Second point's horizontal coordinate.</param>
    /// <param name="y2">Second point's vertical coordinate.</param>
    /// <returns>The distance rounded to the nearest whole number.</returns>
    public static int Between(float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), MidpointRounding.AwayFromZero);
    }
}
