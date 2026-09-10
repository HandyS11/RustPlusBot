using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Workspace;

/// <summary>A Discord category the bot has provisioned. One per scope (global or per-server).</summary>
public sealed class ProvisionedCategory : IGuildScoped, ICreatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this category belongs to, or <c>null</c> for the global category.</summary>
    public Guid? RustServerId { get; set; }

    /// <summary>The provisioned Discord category snowflake.</summary>
    public ulong DiscordCategoryId { get; set; }

    /// <summary>When the record was first created (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
