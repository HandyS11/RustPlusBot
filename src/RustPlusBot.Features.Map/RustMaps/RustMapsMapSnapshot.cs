namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>An immutable view of a map key's generation progress.</summary>
/// <param name="State">The current lifecycle state.</param>
/// <param name="MapId">The RustMaps map id once assigned, else null.</param>
/// <param name="Ready">The ready image when <see cref="State"/> is <see cref="RustMapsGenerationState.Ready"/>.</param>
public sealed record RustMapsMapSnapshot(RustMapsGenerationState State, string? MapId, RustMapsReadyMap? Ready);
