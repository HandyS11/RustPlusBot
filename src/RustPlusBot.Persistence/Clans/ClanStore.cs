using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Clans;

namespace RustPlusBot.Persistence.Clans;

/// <summary>EF-backed <see cref="IClanStore"/>.</summary>
/// <param name="db">The bot database context.</param>
/// <param name="clock">Supplies write timestamps.</param>
internal sealed class ClanStore(BotDbContext db, IClock clock) : IClanStore
{
    /// <inheritdoc />
    public async Task<ClanSnapshot?> GetAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ClanStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ServerId == serverId && s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new ClanSnapshot(
            row.ClanId,
            row.Name,
            row.Created,
            row.Creator,
            row.Motd,
            row.MotdTimestamp,
            row.MotdAuthor,
            row.LogoHash,
            row.Color,
            row.MaxMemberCount,
            row.Score,
            ClanSnapshotSerializer.Deserialize<ClanRoleSnapshot>(row.RolesJson),
            ClanSnapshotSerializer.Deserialize<ClanMemberSnapshot>(row.MembersJson),
            ClanSnapshotSerializer.Deserialize<ClanInviteSnapshot>(row.InvitesJson));
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ulong guildId,
        Guid serverId,
        ClanSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var row = await db.ClanStates
            .FirstOrDefaultAsync(s => s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new ClanState
            {
                ServerId = serverId
            };
            db.ClanStates.Add(row);
        }

        row.GuildId = guildId;
        row.ClanId = snapshot.ClanId;
        row.Name = snapshot.Name;
        row.Created = snapshot.Created;
        row.Creator = snapshot.Creator;
        row.Motd = snapshot.Motd;
        row.MotdTimestamp = snapshot.MotdTimestamp;
        row.MotdAuthor = snapshot.MotdAuthor;
        row.LogoHash = snapshot.LogoHash;
        row.Color = snapshot.Color;
        row.MaxMemberCount = snapshot.MaxMemberCount;
        row.Score = snapshot.Score;
        row.RolesJson = ClanSnapshotSerializer.Serialize(snapshot.Roles);
        row.MembersJson = ClanSnapshotSerializer.Serialize(snapshot.Members);
        row.InvitesJson = ClanSnapshotSerializer.Serialize(snapshot.Invites);
        row.LastSeenUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ClearAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ClanStates
            .FirstOrDefaultAsync(s => s.ServerId == serverId && s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return false;
        }

        db.ClanStates.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> HasClanAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        db.ClanStates.AnyAsync(s => s.ServerId == serverId && s.GuildId == guildId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ulong, string>> GetNamesAsync(
        ulong guildId,
        Guid serverId,
        IReadOnlyCollection<ulong> steamIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steamIds);

        if (steamIds.Count == 0)
        {
            return new Dictionary<ulong, string>();
        }

        return await db.ClanPlayerNames
            .AsNoTracking()
            .Where(n => n.GuildId == guildId && n.ServerId == serverId && steamIds.Contains(n.SteamId))
            .ToDictionaryAsync(n => n.SteamId, n => n.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordNameAsync(
        ulong guildId,
        Guid serverId,
        ulong steamId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var row = await db.ClanPlayerNames
            .FirstOrDefaultAsync(
                n => n.ServerId == serverId && n.SteamId == steamId,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is not null && row.GuildId == guildId && string.Equals(row.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        if (row is null)
        {
            row = new ClanPlayerName
            {
                ServerId = serverId, SteamId = steamId
            };
            db.ClanPlayerNames.Add(row);
        }

        row.GuildId = guildId;
        row.Name = name;
        row.UpdatedUtc = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
