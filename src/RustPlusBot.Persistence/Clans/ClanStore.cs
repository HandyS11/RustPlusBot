using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Clans;

namespace RustPlusBot.Persistence.Clans;

/// <summary>EF-backed <see cref="IClanStore"/>.</summary>
/// <param name="db">The bot database context.</param>
/// <param name="timeProvider">Supplies the last-seen timestamp; the same clock the TimestampInterceptor uses.</param>
internal sealed class ClanStore(BotDbContext db, TimeProvider timeProvider) : IClanStore
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

        await db.ClanStates.UpsertAsync(
                s => s.ServerId == serverId,
                () => new ClanState
                {
                    ServerId = serverId
                },
                row => Apply(row, guildId, snapshot, timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Apply(ClanState row, ulong guildId, ClanSnapshot snapshot, DateTimeOffset seenAt)
    {
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
        row.LastSeenUtc = seenAt;
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

        // Writing the same name again is the common case (every chat line repeats it). The upsert
        // saves only when something actually changed, so an unchanged row keeps its UpdatedAt.
        await db.ClanPlayerNames.UpsertAsync(
                n => n.ServerId == serverId && n.SteamId == steamId,
                () => new ClanPlayerName
                {
                    ServerId = serverId, SteamId = steamId
                },
                row =>
                {
                    row.GuildId = guildId;
                    row.Name = name;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
