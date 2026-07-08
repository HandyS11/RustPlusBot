using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a marker poll detects markers that appeared, disappeared, or moved since the previous poll.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Dimensions">Map dimensions for grid-reference rendering, or null if unavailable.</param>
/// <param name="Added">Markers present now that were absent in the previous poll.</param>
/// <param name="Removed">Markers absent now that were present in the previous poll.</param>
/// <param name="Moved">Markers present in both polls whose position or rotation changed.</param>
public sealed record MapMarkersChangedEvent(
    ulong GuildId,
    Guid ServerId,
    MapDimensions? Dimensions,
    IReadOnlyList<MapMarkerSnapshot> Added,
    IReadOnlyList<MapMarkerSnapshot> Removed,
    IReadOnlyList<MapMarkerSnapshot> Moved);
