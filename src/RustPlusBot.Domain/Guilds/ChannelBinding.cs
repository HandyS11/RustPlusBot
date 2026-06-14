namespace RustPlusBot.Domain.Guilds;

/// <summary>Maps a Discord channel to a bot feature within a guild.</summary>
public sealed class ChannelBinding
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The bound Discord channel snowflake.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>The feature this channel serves.</summary>
    public BoundFeature Feature { get; set; }
}
