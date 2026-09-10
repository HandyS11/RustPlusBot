using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Commands;

/// <summary>Per-(guild, server) command configuration: trigger prefix and mute state.</summary>
public sealed class ServerCommandSettings : IGuildScoped
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer; primary key, one row per server).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The command trigger prefix.</summary>
    public string Prefix { get; set; } = "!";

    /// <summary>Whether all bot-to-game output is currently muted.</summary>
    public bool Muted { get; set; }
}
