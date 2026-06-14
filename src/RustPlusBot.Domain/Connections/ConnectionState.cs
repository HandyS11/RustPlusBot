namespace RustPlusBot.Domain.Connections;

/// <summary>Persisted last-known connection state per server, so the active identity survives restarts.</summary>
public sealed class ConnectionState
{
    /// <summary>The server this state belongs to (primary key, one row per server).</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The credential currently selected as active, if any.</summary>
    public Guid? ActiveCredentialId { get; set; }

    /// <summary>Whether the connection was healthy at last check.</summary>
    public bool IsHealthy { get; set; }

    /// <summary>When the state was last updated (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
