namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the guild-global #setup channel (used to post server-pairing prompts).</summary>
public interface ISetupChannelLocator
{
    /// <summary>Gets the Discord channel id of #setup for <paramref name="guildId"/>, or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, CancellationToken cancellationToken);
}
