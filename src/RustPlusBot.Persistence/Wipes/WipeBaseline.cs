namespace RustPlusBot.Persistence.Wipes;

/// <summary>The persisted wipe baseline for a server: the last observed wipe time and world identity. All-null before the first observation.</summary>
/// <param name="WipeTimeUtc">The last observed wipe time (UTC), or null when the server never reported one.</param>
/// <param name="MapSeed">The last observed procedural map seed, or null before first observation.</param>
/// <param name="MapSize">The last observed world size (game units), or null before first observation.</param>
public sealed record WipeBaseline(DateTimeOffset? WipeTimeUtc, uint? MapSeed, uint? MapSize);
