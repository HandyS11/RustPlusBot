namespace RustPlusBot.Features.Map.Composing;

/// <summary>One provider of the static-per-wipe base map image. Sources are tried in registration order.</summary>
public interface IBaseMapSource
{
    /// <summary>Fetches the base map, or null when this source cannot provide one (falls through).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base map image, or null.</returns>
    Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
