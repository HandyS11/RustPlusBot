namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One map marker observed in a <c>GetMapMarkers</c> poll.</summary>
/// <param name="Id">The stable marker id (used to diff polls).</param>
/// <param name="Kind">The classified marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Name">The marker name, if any.</param>
public sealed record MapMarkerSnapshot(ulong Id, MarkerKind Kind, float X, float Y, string? Name);
