namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #storagemonitors channel (used to post/edit storage-monitor embeds).</summary>
public interface IStorageMonitorChannelLocator
{
    /// <summary>Gets the Discord channel id of #storagemonitors for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
