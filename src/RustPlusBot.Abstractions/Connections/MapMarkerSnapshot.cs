namespace RustPlusBot.Abstractions.Connections;

// Rotation defaults to null so existing call sites stay valid and "no heading" is distinguishable
// from "heading north" once RustPlusApi beta.4 supplies values.
/// <summary>One map marker observed in a <c>GetMapMarkers</c> poll.</summary>
/// <param name="Id">The stable marker id (used to diff polls).</param>
/// <param name="Kind">The classified marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Name">The marker name, if any.</param>
/// <param name="Rotation">Heading in degrees as sent by the server, or null when unavailable.</param>
public sealed record MapMarkerSnapshot(
    ulong Id,
    MarkerKind Kind,
    float X,
    float Y,
    string? Name,
    float? Rotation = null);
