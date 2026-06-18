namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A point-in-time view of a team, decoupled from RustPlusApi types.</summary>
/// <param name="LeaderSteamId">Steam64 id of the current team leader.</param>
/// <param name="Members">Status snapshots for all team members.</param>
/// <param name="DeathNote">The leader's death-note map coordinate, if one is set; null otherwise.</param>
public sealed record TeamInfoSnapshot(
    ulong LeaderSteamId,
    IReadOnlyList<TeamMemberSnapshot> Members,
    (float X, float Y)? DeathNote = null);

/// <summary>A point-in-time view of one team member.</summary>
/// <param name="SteamId">Steam64 id of the member.</param>
/// <param name="Name">In-game display name (empty when the game reports none).</param>
/// <param name="X">Horizontal map coordinate (west to east).</param>
/// <param name="Y">Vertical map coordinate (south to north).</param>
/// <param name="IsOnline">Whether the member is currently connected.</param>
/// <param name="IsAlive">Whether the member is currently alive.</param>
/// <param name="LastSpawnTimeUtc">UTC time of the member's last spawn.</param>
/// <param name="LastDeathTimeUtc">UTC time of the member's last death.</param>
public sealed record TeamMemberSnapshot(
    ulong SteamId,
    string Name,
    float X,
    float Y,
    bool IsOnline,
    bool IsAlive,
    DateTimeOffset LastSpawnTimeUtc,
    DateTimeOffset LastDeathTimeUtc);
