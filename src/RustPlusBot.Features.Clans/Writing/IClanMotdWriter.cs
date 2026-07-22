namespace RustPlusBot.Features.Clans.Writing;

/// <summary>The outcome of a clan MOTD write.</summary>
internal enum ClanMotdWriteResult
{
    /// <summary>The MOTD was set.</summary>
    Ok = 0,

    /// <summary>The acting player's clan role does not allow setting the MOTD.</summary>
    NotPermitted = 1,

    /// <summary>The write did not succeed: no live socket, a rejected write, or blank input.</summary>
    Failed = 2,
}

/// <summary>Applies a clan MOTD change on behalf of an acting player, enforcing their in-game clan permission.</summary>
internal interface IClanMotdWriter
{
    /// <summary>Sets the clan's message of the day, re-checking the acting player's permission first.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="actorSteamId">Steam64 id of the player performing the change.</param>
    /// <param name="motd">The new message of the day.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outcome of the write.</returns>
    Task<ClanMotdWriteResult> SetAsync(ulong guildId,
        Guid serverId,
        ulong actorSteamId,
        string motd,
        CancellationToken cancellationToken);
}
