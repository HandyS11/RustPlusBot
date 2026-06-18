namespace RustPlusBot.Abstractions.Events;

/// <summary>One team-member presence transition, with a pre-resolved optional map location.</summary>
/// <param name="Kind">The transition kind.</param>
/// <param name="SteamId">Steam64 id of the member.</param>
/// <param name="Name">In-game display name (empty when the game reports none).</param>
/// <param name="Location">Resolved map coordinate to show, or null when no location applies.</param>
public sealed record PlayerTransition(
    PlayerTransitionKind Kind,
    ulong SteamId,
    string Name,
    (float X, float Y)? Location);
