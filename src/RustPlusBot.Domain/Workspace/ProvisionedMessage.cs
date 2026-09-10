using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Workspace;

/// <summary>An anchored bot message, edited in place rather than re-posted.</summary>
public sealed class ProvisionedMessage : IGuildScoped, ICreatedAt, IUpdatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this message belongs to, or <c>null</c> for a global message.</summary>
    public Guid? RustServerId { get; set; }

    /// <summary>The stable spec key (e.g. "information.main", "settings.main", "server.info").</summary>
    public string MessageKey { get; set; } = string.Empty;

    /// <summary>The channel the message lives in.</summary>
    public ulong DiscordChannelId { get; set; }

    /// <summary>The anchored message snowflake.</summary>
    public ulong DiscordMessageId { get; set; }

    /// <summary>When the record was first created (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the record was last written (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
