using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Clans.State;

/// <summary>
/// Compares two clan snapshots and produces the feed events between them. Pure: no I/O, no clock,
/// no state. Emission order is fixed so the feed reads consistently.
/// </summary>
internal static class ClanSnapshotDiffer
{
    /// <summary>Diffs two snapshots.</summary>
    /// <param name="previous">The last known snapshot, or null when none was stored.</param>
    /// <param name="current">The new snapshot, or null when the clan is gone.</param>
    /// <returns>The detected changes, in a fixed order.</returns>
    public static IReadOnlyList<ClanChange> Diff(ClanSnapshot? previous, ClanSnapshot? current)
    {
        if (previous is null)
        {
            // A first snapshot is a baseline, not news: announcing every existing member as a
            // "joined" would spam a brand-new channel on first detection.
            return [];
        }

        if (current is null)
        {
            return [new ClanChange(ClanChangeKind.Dissolved, Text: previous.Name)];
        }

        if (previous.ClanId != current.ClanId)
        {
            // A different clan entirely; re-baseline rather than diffing unrelated rosters.
            return [];
        }

        var changes = new List<ClanChange>();

        if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.Renamed, Text: current.Name));
        }

        if (!string.Equals(previous.Motd, current.Motd, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.MotdChanged, ActorSteamId: current.MotdAuthor,
                Text: current.Motd));
        }

        var previousMembers = previous.Members.ToDictionary(m => m.SteamId);
        var currentMembers = current.Members.ToDictionary(m => m.SteamId);
        var previousInvites = previous.Invites.Select(i => i.SteamId).ToHashSet();
        var currentInvites = current.Invites.Select(i => i.SteamId).ToHashSet();

        // An id that left Invites and appeared in Members in one step is an acceptance, reported
        // once — never as a join plus a revocation.
        var accepted = previousInvites
            .Where(id => !currentInvites.Contains(id) && currentMembers.ContainsKey(id) && !previousMembers.ContainsKey(id))
            .ToHashSet();

        foreach (var id in currentMembers.Keys.Where(id => !previousMembers.ContainsKey(id) && !accepted.Contains(id))
                     .Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.MemberJoined, id));
        }

        foreach (var id in previousMembers.Keys.Where(id => !currentMembers.ContainsKey(id)).Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.MemberLeft, id));
        }

        var rolesById = current.Roles.ToDictionary(r => r.RoleId);
        foreach (var (id, member) in currentMembers.OrderBy(kv => kv.Key))
        {
            if (!previousMembers.TryGetValue(id, out var before) || before.RoleId == member.RoleId)
            {
                continue;
            }

            if (!rolesById.TryGetValue(member.RoleId, out var newRole) ||
                !rolesById.TryGetValue(before.RoleId, out var oldRole))
            {
                // An unresolvable role change is not worth guessing a direction over.
                continue;
            }

            // Lower Rank is higher standing.
            var kind = newRole.Rank < oldRole.Rank ? ClanChangeKind.MemberPromoted : ClanChangeKind.MemberDemoted;
            changes.Add(new ClanChange(kind, id, RoleName: newRole.Name));
        }

        foreach (var invite in current.Invites.Where(i => !previousInvites.Contains(i.SteamId))
                     .OrderBy(i => i.SteamId))
        {
            changes.Add(new ClanChange(ClanChangeKind.InviteSent, invite.SteamId, invite.Recruiter));
        }

        foreach (var id in accepted.Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.InviteAccepted, id));
        }

        foreach (var id in previousInvites
                     .Where(id => !currentInvites.Contains(id) && !accepted.Contains(id))
                     .Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.InviteRevoked, id));
        }

        if (!string.Equals(previous.LogoHash, current.LogoHash, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.LogoChanged));
        }

        if (previous.Color != current.Color)
        {
            changes.Add(new ClanChange(ClanChangeKind.ColorChanged));
        }

        if (previous.Score != current.Score)
        {
            changes.Add(new ClanChange(ClanChangeKind.ScoreChanged, Score: current.Score));
        }

        return changes;
    }
}
