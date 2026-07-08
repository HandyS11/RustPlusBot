namespace RustPlusBot.Features.Events.State;

/// <summary>One historical marker position, in world coordinates.</summary>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
public sealed record TrailPoint(float X, float Y);
