namespace RustPlusBot.Features.Clans.Names;

/// <summary>
/// Resolves clan member Steam ids to display names for rendering. The interface is public because
/// the clan renderers take it on their (public) constructors, which the DI container must be able
/// to invoke.
/// </summary>
public interface IClanNameResolver
{
    /// <summary>Resolves display names for every requested Steam id.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="steamIds">The Steam64 ids to resolve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A map covering <em>every</em> requested id: known ids map to their cached display name, and
    /// ids we have never seen a name for map to a Steam profile markdown link, so callers never
    /// have to null-check.
    /// </returns>
    Task<IReadOnlyDictionary<ulong, string>> ResolveAsync(
        ulong guildId,
        Guid serverId,
        IReadOnlyCollection<ulong> steamIds,
        CancellationToken cancellationToken);
}
