namespace RustPlusBot.Abstractions.Connections;

/// <summary>One named monument observed in a <c>GetMap</c> response.</summary>
/// <param name="Token">The monument token (e.g. <c>oilrig_1</c>, <c>large_oil_rig</c>).</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
public sealed record MonumentSnapshot(string Token, float X, float Y);
