using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.State;

/// <summary>A marker currently present on a server's map.</summary>
/// <param name="Id">The marker id.</param>
/// <param name="Kind">The marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
/// <param name="SeenAtUtc">When the marker was first seen.</param>
public sealed record ActiveMarker(
    ulong Id,
    MarkerKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions,
    DateTimeOffset SeenAtUtc);
