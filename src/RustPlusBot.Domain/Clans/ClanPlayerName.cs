using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Clans;

/// <summary>
/// A cached Steam64 id to display-name mapping. The clan API reports members by id only, so names
/// are harvested from clan chat and team snapshots, which do carry them.
/// </summary>
public sealed class ClanPlayerName : IUpdatedAt
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The Steam64 id of the player.</summary>
    public ulong SteamId { get; set; }

    /// <summary>The most recently observed display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the name was last observed (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
