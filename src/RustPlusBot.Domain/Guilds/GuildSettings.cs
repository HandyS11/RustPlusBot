using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Guilds;

/// <summary>Per-guild configuration. Primary key is the guild snowflake.</summary>
public sealed class GuildSettings : IGuildScoped
{
    /// <summary>BCP-47 culture for localized output (e.g. "en", "fr").</summary>
    public string Culture { get; set; } = "en";

    /// <summary>When true, the server-wiped announcement pings @everyone in #events. Off by default.</summary>
    public bool PingEveryoneOnWipe { get; set; }

    /// <summary>The Discord guild snowflake (primary key).</summary>
    public ulong GuildId { get; set; }
}
