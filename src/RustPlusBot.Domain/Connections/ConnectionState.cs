using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Connections;

/// <summary>Persisted last-known connection state per server, so the active identity and status survive restarts.</summary>
public sealed class ConnectionState : IUpdatedAt
{
    /// <summary>The server this state belongs to (primary key, one row per server).</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The credential currently selected as active, if any.</summary>
    public Guid? ActiveCredentialId { get; set; }

    /// <summary>The live-connection status.</summary>
    public ConnectionStatus Status { get; set; }

    /// <summary>Last heartbeat player count, or null if unknown.</summary>
    public int? PlayerCount { get; set; }

    /// <summary>When the state was last updated (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
