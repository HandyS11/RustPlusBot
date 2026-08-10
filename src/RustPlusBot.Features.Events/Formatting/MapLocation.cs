using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Formatting;

/// <summary>A rendered marker location, and whether it names a compass direction rather than a grid cell.</summary>
/// <param name="IsDirection">
///     True when <paramref name="Text"/> is a compass direction. False for a grid cell and for the
///     raw-coordinate fallback — it answers "does this text name a direction", which is the question
///     the <c>.dir</c> message-key suffix asks.
/// </param>
/// <param name="Text">The localized location text.</param>
public readonly record struct MapLocationText(bool IsDirection, string Text);

/// <summary>
///     Describes a marker position as a grid cell when it is on the map, and as a compass direction
///     when it is not. Markers spawn and despawn in the ocean outside the playable world, where a grid
///     reference would name a cell the marker is not in.
/// </summary>
public static class MapLocation
{
    /// <summary>Describes a position, preferring a grid cell and falling back to a direction off-map.</summary>
    /// <param name="localizer">Resolves the direction word.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <returns>A direction off-map, a grid cell on-map, or raw coordinates when dimensions are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is null.</exception>
    public static MapLocationText Describe(
        ILocalizer localizer,
        string culture,
        float x,
        float y,
        MapDimensions? dims,
        MapGridStyle style = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (dims is null || dims.WorldSize == 0)
        {
            return new MapLocationText(false, GridReference.From(x, y, dims, style));
        }

        return MapGrid.IsOutsideWorld(x, y, dims.WorldSize)
            ? new MapLocationText(true, Word(localizer, culture, x, y, dims.WorldSize))
            : new MapLocationText(false, GridReference.From(x, y, dims, style));
    }

    /// <summary>
    ///     Describes a position as a compass direction regardless of whether it is on the map. Used by
    ///     departure messages, where the direction the marker headed matters more than the cell it was
    ///     last seen in.
    /// </summary>
    /// <param name="localizer">Resolves the direction word.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <returns>A direction, or raw coordinates when dimensions are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is null.</exception>
    public static MapLocationText DescribeDirection(
        ILocalizer localizer,
        string culture,
        float x,
        float y,
        MapDimensions? dims)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return dims is null || dims.WorldSize == 0
            ? new MapLocationText(false, GridReference.From(x, y, dims))
            : new MapLocationText(true, Word(localizer, culture, x, y, dims.WorldSize));
    }

    private static string Word(ILocalizer localizer, string culture, float x, float y, uint worldSize) =>
        localizer.Get(Key(MapGrid.DirectionFrom(x, y, worldSize)), culture);

    private static string Key(MapDirection direction) => direction switch
    {
        MapDirection.North => "direction.n",
        MapDirection.NorthEast => "direction.ne",
        MapDirection.East => "direction.e",
        MapDirection.SouthEast => "direction.se",
        MapDirection.South => "direction.s",
        MapDirection.SouthWest => "direction.sw",
        MapDirection.West => "direction.w",
        MapDirection.NorthWest => "direction.nw",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported map direction."),
    };
}
