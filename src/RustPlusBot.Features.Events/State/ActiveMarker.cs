using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Events.State;

/// <summary>A marker currently present on a server's map.</summary>
/// <param name="Id">The marker id.</param>
/// <param name="Kind">The marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
/// <param name="SeenAtUtc">When the marker was first seen.</param>
/// <param name="History">Recent positions, oldest first, newest (current) last; capped.</param>
/// <param name="Rotation">Heading in degrees as sent by the server, or null.</param>
public sealed record ActiveMarker(
    ulong Id,
    MarkerKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions,
    DateTimeOffset SeenAtUtc,
    IReadOnlyList<TrailPoint> History,
    float? Rotation);
