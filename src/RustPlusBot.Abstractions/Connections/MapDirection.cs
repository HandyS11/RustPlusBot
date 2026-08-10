namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// An 8-point compass direction. Values are ordered clockwise from north so that a bearing can be
/// binned straight into this enum by integer division.
/// </summary>
public enum MapDirection
{
    /// <summary>Due north.</summary>
    North = 0,

    /// <summary>North-east.</summary>
    NorthEast = 1,

    /// <summary>Due east.</summary>
    East = 2,

    /// <summary>South-east.</summary>
    SouthEast = 3,

    /// <summary>Due south.</summary>
    South = 4,

    /// <summary>South-west.</summary>
    SouthWest = 5,

    /// <summary>Due west.</summary>
    West = 6,

    /// <summary>North-west.</summary>
    NorthWest = 7,
}
