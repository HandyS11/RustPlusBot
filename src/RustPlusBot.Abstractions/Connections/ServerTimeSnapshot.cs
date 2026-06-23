namespace RustPlusBot.Abstractions.Connections;

/// <summary>A point-in-time view of in-game time, decoupled from RustPlusApi types.</summary>
/// <param name="TimeOfDay">Current in-game time of day (RustPlusApi <c>TimeInfo.Time</c>).</param>
/// <param name="Sunrise">In-game sunrise time (RustPlusApi <c>TimeInfo.Sunrise</c>).</param>
/// <param name="Sunset">In-game sunset time (RustPlusApi <c>TimeInfo.Sunset</c>).</param>
public sealed record ServerTimeSnapshot(float TimeOfDay, float Sunrise, float Sunset);
