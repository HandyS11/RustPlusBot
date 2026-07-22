namespace RustPlusBot.Domain.Clans;

/// <summary>
/// The latest known clan snapshot for one (guild, server). Row presence is the single source of
/// truth for whether the paired player is in a clan.
/// </summary>
public sealed class ClanState
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer; primary key, one row per server).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game clan identifier.</summary>
    public long ClanId { get; set; }

    /// <summary>The clan's display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the clan was created (UTC).</summary>
    public DateTimeOffset Created { get; set; }

    /// <summary>Steam64 id of the clan creator.</summary>
    public ulong Creator { get; set; }

    /// <summary>The message of the day, or null when unset.</summary>
    public string? Motd { get; set; }

    /// <summary>When the MOTD was last changed (UTC), or null.</summary>
    public DateTimeOffset? MotdTimestamp { get; set; }

    /// <summary>Steam64 id of the player who last changed the MOTD, or null.</summary>
    public ulong? MotdAuthor { get; set; }

    /// <summary>Stable hash of the clan logo, or null when no logo is set.</summary>
    public string? LogoHash { get; set; }

    /// <summary>The clan colour as a packed ARGB integer, or null.</summary>
    public int? Color { get; set; }

    /// <summary>The maximum member count, or null when uncapped.</summary>
    public int? MaxMemberCount { get; set; }

    /// <summary>The clan score, or null when the server does not report one.</summary>
    public long? Score { get; set; }

    /// <summary>The clan's roles, serialized as JSON.</summary>
    public string RolesJson { get; set; } = "[]";

    /// <summary>The clan's members, serialized as JSON.</summary>
    public string MembersJson { get; set; } = "[]";

    /// <summary>The clan's pending invites, serialized as JSON.</summary>
    public string InvitesJson { get; set; } = "[]";

    /// <summary>When this snapshot was last confirmed (UTC).</summary>
    public DateTimeOffset LastSeenUtc { get; set; }
}
