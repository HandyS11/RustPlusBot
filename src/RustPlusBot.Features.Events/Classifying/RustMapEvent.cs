using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.Classifying;

/// <summary>A classified live map event with its location and time.</summary>
/// <param name="Kind">The event kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
/// <param name="AtUtc">When the event was observed.</param>
public sealed record RustMapEvent(MapEventKind Kind, float X, float Y, MapDimensions? Dimensions, DateTimeOffset AtUtc);
