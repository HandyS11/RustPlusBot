using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Bindings;

/// <summary>Guild-scoped management of channel-to-feature bindings (one channel per feature per guild).</summary>
public interface IBindingService
{
    /// <summary>Binds (or re-binds) a feature to a channel within a guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="feature">The feature to bind.</param>
    /// <param name="channelId">The Discord channel snowflake to bind it to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the binding is saved.</returns>
    Task BindAsync(ulong guildId,
        BoundFeature feature,
        ulong channelId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the channel bound to a feature, or null if unbound.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="feature">The feature to look up.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The bound channel snowflake, or null if the feature is unbound in this guild.</returns>
    Task<ulong?> GetBoundChannelAsync(ulong guildId,
        BoundFeature feature,
        CancellationToken cancellationToken = default);
}
