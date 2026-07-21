namespace RustPlusBot.Abstractions.Connections;

/// <summary>A permission role within a clan.</summary>
/// <param name="RoleId">Unique id of the role within the clan.</param>
/// <param name="Rank">Rank order; LOWER values are HIGHER ranks (0 is the leader).</param>
/// <param name="Name">Display name of the role.</param>
/// <param name="CanSetMotd">True when holders may set the clan MOTD.</param>
/// <param name="CanSetLogo">True when holders may set the clan logo.</param>
/// <param name="CanInvite">True when holders may invite players.</param>
/// <param name="CanKick">True when holders may kick members.</param>
/// <param name="CanPromote">True when holders may promote members.</param>
/// <param name="CanDemote">True when holders may demote members.</param>
/// <param name="CanSetPlayerNotes">True when holders may set notes on members.</param>
/// <param name="CanAccessLogs">True when holders may view clan audit logs.</param>
/// <param name="CanAccessScoreEvents">True when holders may view clan score events.</param>
public sealed record ClanRoleSnapshot(
    int RoleId,
    int Rank,
    string Name,
    bool CanSetMotd,
    bool CanSetLogo,
    bool CanInvite,
    bool CanKick,
    bool CanPromote,
    bool CanDemote,
    bool CanSetPlayerNotes,
    bool CanAccessLogs,
    bool CanAccessScoreEvents);

/// <summary>A member of a clan.</summary>
/// <param name="SteamId">Steam64 id of the member.</param>
/// <param name="RoleId">Id of the role assigned to this member.</param>
/// <param name="Joined">When the member joined the clan (UTC).</param>
/// <param name="LastSeen">When the member was last seen online (UTC).</param>
/// <param name="Notes">Officer notes attached to this member, or null.</param>
/// <param name="Online">True when the member is currently online. The API reports this as nullable; null is mapped to false.</param>
public sealed record ClanMemberSnapshot(
    ulong SteamId,
    int RoleId,
    DateTimeOffset Joined,
    DateTimeOffset LastSeen,
    string? Notes,
    bool Online);

/// <summary>A pending invitation to join a clan.</summary>
/// <param name="SteamId">Steam64 id of the invited player.</param>
/// <param name="Recruiter">Steam64 id of the member who sent the invitation.</param>
/// <param name="Timestamp">When the invitation was created (UTC).</param>
public sealed record ClanInviteSnapshot(ulong SteamId, ulong Recruiter, DateTimeOffset Timestamp);

/// <summary>A full clan snapshot, decoupled from RustPlusApi types.</summary>
/// <param name="ClanId">Unique identifier of the clan.</param>
/// <param name="Name">Display name of the clan.</param>
/// <param name="Created">When the clan was created (UTC).</param>
/// <param name="Creator">Steam64 id of the clan creator.</param>
/// <param name="Motd">Message of the day, or null when unset.</param>
/// <param name="MotdTimestamp">When the MOTD was last changed (UTC), or null.</param>
/// <param name="MotdAuthor">Steam64 id of the player who last changed the MOTD, or null.</param>
/// <param name="LogoHash">Stable hash of the clan logo bytes, or null when no logo is set. The bytes themselves are not carried: nothing renders them.</param>
/// <param name="Color">Clan colour as a packed ARGB integer, or null.</param>
/// <param name="MaxMemberCount">Maximum members allowed, or null when uncapped.</param>
/// <param name="Score">Clan score, or null when the server does not report one.</param>
/// <param name="Roles">Roles defined in this clan.</param>
/// <param name="Members">Current members of the clan.</param>
/// <param name="Invites">Pending invitations to the clan.</param>
public sealed record ClanSnapshot(
    long ClanId,
    string Name,
    DateTimeOffset Created,
    ulong Creator,
    string? Motd,
    DateTimeOffset? MotdTimestamp,
    ulong? MotdAuthor,
    string? LogoHash,
    int? Color,
    int? MaxMemberCount,
    long? Score,
    IReadOnlyList<ClanRoleSnapshot> Roles,
    IReadOnlyList<ClanMemberSnapshot> Members,
    IReadOnlyList<ClanInviteSnapshot> Invites);
