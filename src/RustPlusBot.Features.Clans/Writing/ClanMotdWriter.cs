using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Writing;

/// <summary>
/// Applies a clan MOTD change, enforcing the acting player's clan permission before touching the
/// socket. The permission check is duplicated here rather than trusted from the button's presence:
/// a component id can be forged, and a button rendered before a demotion can still be clicked from
/// scrollback afterwards. It is not a fresh authority check — it reads the same cached clan
/// snapshot the button was rendered from, so a demotion the poller has not yet observed still gets
/// through; the game's own API is the last word there.
/// </summary>
/// <param name="store">Supplies the stored clan snapshot for the permission check.</param>
/// <param name="query">Performs the write on the live socket.</param>
internal sealed class ClanMotdWriter(IClanStore store, IRustServerQuery query) : IClanMotdWriter
{
    /// <inheritdoc />
    public async Task<ClanMotdWriteResult> SetAsync(ulong guildId,
        Guid serverId,
        ulong actorSteamId,
        string motd,
        CancellationToken cancellationToken)
    {
        var trimmed = motd?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ClanMotdWriteResult.Failed;
        }

        var clan = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (clan is null)
        {
            return ClanMotdWriteResult.NotPermitted;
        }

        var member = clan.Members.FirstOrDefault(m => m.SteamId == actorSteamId);
        if (member is null)
        {
            return ClanMotdWriteResult.NotPermitted;
        }

        var role = clan.Roles.FirstOrDefault(r => r.RoleId == member.RoleId);
        if (role?.CanSetMotd != true)
        {
            return ClanMotdWriteResult.NotPermitted;
        }

        var ok = await query.SetClanMotdAsync(guildId, serverId, trimmed, cancellationToken).ConfigureAwait(false);
        return ok ? ClanMotdWriteResult.Ok : ClanMotdWriteResult.Failed;
    }
}
