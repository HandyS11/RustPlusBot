using System.Globalization;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Names;

/// <summary>
/// Resolves clan member Steam ids to display names. The clan API reports members by id only, so
/// names are harvested from clan chat and team snapshots; an id we have never seen a name for
/// renders as a Steam profile link rather than a bare number.
/// </summary>
/// <param name="store">Supplies the cached names.</param>
internal sealed class ClanNameResolver(IClanStore store) : IClanNameResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ulong, string>> ResolveAsync(
        ulong guildId,
        Guid serverId,
        IReadOnlyCollection<ulong> steamIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steamIds);
        if (steamIds.Count == 0)
        {
            return new Dictionary<ulong, string>();
        }

        var known = await store.GetNamesAsync(guildId, serverId, steamIds, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<ulong, string>(steamIds.Count);
        foreach (var id in steamIds)
        {
            result[id] = known.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : ProfileLink(id);
        }

        return result;
    }

    private static string ProfileLink(ulong steamId)
    {
        var id = steamId.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"[{id}](https://steamcommunity.com/profiles/{id})");
    }
}
