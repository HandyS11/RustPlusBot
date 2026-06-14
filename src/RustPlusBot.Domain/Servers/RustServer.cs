namespace RustPlusBot.Domain.Servers;

/// <summary>A Rust+ server target bound to a Discord guild. Guild-scoped.</summary>
public sealed class RustServer
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Display name shown in Discord.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Server host (ip or dns).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Rust+ app port.</summary>
    public int Port { get; set; }

    /// <summary>The Discord user who added this server.</summary>
    public ulong AddedByUserId { get; set; }
}
