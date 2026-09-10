using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Workspace;

/// <summary>A Discord text channel the bot has provisioned, identified by its stable spec key.</summary>
public sealed class ProvisionedChannel : IGuildScoped, ICreatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The server this channel belongs to, or <c>null</c> for a global channel.</summary>
    public Guid? RustServerId { get; set; }

    /// <summary>The stable spec key (e.g. "information", "setup", "settings", "info").</summary>
    public string ChannelKey { get; set; } = string.Empty;

    /// <summary>The provisioned Discord channel snowflake.</summary>
    public ulong DiscordChannelId { get; set; }

    /// <summary>When the record was first created (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }
}
