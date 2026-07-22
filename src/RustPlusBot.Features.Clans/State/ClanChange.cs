namespace RustPlusBot.Features.Clans.State;

/// <summary>The kind of clan change detected between two snapshots.</summary>
internal enum ClanChangeKind
{
    /// <summary>The clan was dissolved, or the player left it.</summary>
    Dissolved = 0,

    /// <summary>The clan was renamed.</summary>
    Renamed = 1,

    /// <summary>The message of the day changed.</summary>
    MotdChanged = 2,

    /// <summary>A member joined the clan.</summary>
    MemberJoined = 3,

    /// <summary>A member is no longer in the clan. The API cannot distinguish leaving from being kicked.</summary>
    MemberLeft = 4,

    /// <summary>A member moved to a higher-standing role.</summary>
    MemberPromoted = 5,

    /// <summary>A member moved to a lower-standing role.</summary>
    MemberDemoted = 6,

    /// <summary>A player was invited to the clan.</summary>
    InviteSent = 7,

    /// <summary>An invited player joined, observed as a single invite-to-member transition.</summary>
    InviteAccepted = 8,

    /// <summary>An invitation went away without the player joining.</summary>
    InviteRevoked = 9,

    /// <summary>The clan logo changed.</summary>
    LogoChanged = 10,

    /// <summary>The clan colour changed.</summary>
    ColorChanged = 11,

    /// <summary>The clan score changed.</summary>
    ScoreChanged = 12,
}

/// <summary>One detected clan change, ready to be rendered into the #claninfo feed.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="SteamId">The player the change is about, or null for clan-wide changes.</param>
/// <param name="ActorSteamId">The player who caused the change (MOTD author, recruiter), or null.</param>
/// <param name="Text">Free text carried by the change (the new name or the new MOTD), or null.</param>
/// <param name="RoleName">The new role name for a promotion or demotion, or null.</param>
/// <param name="Score">The new score for a score change, or null.</param>
internal sealed record ClanChange(
    ClanChangeKind Kind,
    ulong? SteamId = null,
    ulong? ActorSteamId = null,
    string? Text = null,
    string? RoleName = null,
    long? Score = null);
