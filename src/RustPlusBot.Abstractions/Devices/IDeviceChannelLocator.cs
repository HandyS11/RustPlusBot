namespace RustPlusBot.Abstractions.Devices;

/// <summary>
/// Resolves the per-server Discord channel a smart-device type's embeds live in. One implementation
/// per device feature (#switches, #storagemonitors…); lets shared device scaffolding find the
/// channel without knowing which device type it is serving.
/// </summary>
public interface IDeviceChannelLocator
{
    /// <summary>Gets the device channel id for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
