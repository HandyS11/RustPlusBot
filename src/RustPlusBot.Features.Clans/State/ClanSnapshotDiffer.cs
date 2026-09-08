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
        AddIdentityChanges(changes, previous, current);
        AddMembershipChanges(changes, previous, current);
        AddRoleChanges(changes, previous, current);
        return changes;
    }

    /// <summary>Adds clan-level identity changes: renames and MOTD updates.</summary>
    /// <param name="changes">The change list being built, appended to in emission order.</param>
    /// <param name="previous">The last known snapshot.</param>
    /// <param name="current">The new snapshot.</param>
    private static void AddIdentityChanges(List<ClanChange> changes, ClanSnapshot previous, ClanSnapshot current)
    {
        if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.Renamed, Text: current.Name));
        }

        if (!string.Equals(previous.Motd, current.Motd, StringComparison.Ordinal))
        {
            changes.Add(new ClanChange(ClanChangeKind.MotdChanged, ActorSteamId: current.MotdAuthor,
                Text: current.Motd));
        }
    }

    /// <summary>Adds member joins and departures, each ordered ascending by Steam id.</summary>
    /// <param name="changes">The change list being built, appended to in emission order.</param>
    /// <param name="previous">The last known snapshot.</param>
    /// <param name="current">The new snapshot.</param>
    private static void AddMembershipChanges(List<ClanChange> changes, ClanSnapshot previous, ClanSnapshot current)
    {
        var previousMembers = ToMemberLookup(previous);
        var currentMembers = ToMemberLookup(current);
        var accepted = ComputeAcceptedInvites(
            ToInviteIdSet(previous), ToInviteIdSet(current), previousMembers, currentMembers);

        foreach (var id in currentMembers.Keys.Where(id => !previousMembers.ContainsKey(id) && !accepted.Contains(id))
                     .Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.MemberJoined, id));
        }

        foreach (var id in previousMembers.Keys.Where(id => !currentMembers.ContainsKey(id)).Order())
        {
            changes.Add(new ClanChange(ClanChangeKind.MemberLeft, id));
        }
    }

    /// <summary>
    /// Adds role promotions and demotions, then invite lifecycle changes and clan attribute
    /// changes (logo, colour, score). The latter two groups are appended here — rather than in
    /// <see cref="AddMembershipChanges"/>, which runs earlier — purely to keep the fixed emission
    /// order: role changes, then invites, then attribute changes.
    /// </summary>
    /// <param name="changes">The change list being built, appended to in emission order.</param>
    /// <param name="previous">The last known snapshot.</param>
    /// <param name="current">The new snapshot.</param>
    private static void AddRoleChanges(List<ClanChange> changes, ClanSnapshot previous, ClanSnapshot current)
    {
        var previousMembers = ToMemberLookup(previous);
        var currentMembers = ToMemberLookup(current);
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

        AddInviteChanges(changes, previous, current);
        AddAttributeChanges(changes, previous, current);
    }

    /// <summary>Adds sent, accepted and revoked invites, each ordered ascending by Steam id.</summary>
    /// <param name="changes">The change list being built, appended to in emission order.</param>
    /// <param name="previous">The last known snapshot.</param>
    /// <param name="current">The new snapshot.</param>
    private static void AddInviteChanges(List<ClanChange> changes, ClanSnapshot previous, ClanSnapshot current)
    {
        var previousInvites = ToInviteIdSet(previous);
        var currentInvites = ToInviteIdSet(current);
        var accepted = ComputeAcceptedInvites(
            previousInvites, currentInvites, ToMemberLookup(previous), ToMemberLookup(current));

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
    }

    /// <summary>Adds clan-wide attribute changes: logo, colour and score.</summary>
    /// <param name="changes">The change list being built, appended to in emission order.</param>
    /// <param name="previous">The last known snapshot.</param>
    /// <param name="current">The new snapshot.</param>
    private static void AddAttributeChanges(List<ClanChange> changes, ClanSnapshot previous, ClanSnapshot current)
    {
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
    }

    /// <summary>Indexes a snapshot's members by Steam id.</summary>
    /// <param name="snapshot">The snapshot to index.</param>
    /// <returns>The members keyed by Steam id.</returns>
    private static Dictionary<ulong, ClanMemberSnapshot> ToMemberLookup(ClanSnapshot snapshot) =>
        snapshot.Members.ToDictionary(m => m.SteamId);

    /// <summary>Collects the Steam ids of a snapshot's pending invites.</summary>
    /// <param name="snapshot">The snapshot to read invites from.</param>
    /// <returns>The invited Steam ids.</returns>
    private static HashSet<ulong> ToInviteIdSet(ClanSnapshot snapshot) =>
        [.. snapshot.Invites.Select(i => i.SteamId)];

    /// <summary>
    /// Computes ids that left <see cref="ClanSnapshot.Invites"/> and appeared in
    /// <see cref="ClanSnapshot.Members"/> in the same step: an acceptance, reported once — never
    /// as a join plus a revocation.
    /// </summary>
    /// <param name="previousInvites">The previous snapshot's invited Steam ids.</param>
    /// <param name="currentInvites">The new snapshot's invited Steam ids.</param>
    /// <param name="previousMembers">The previous snapshot's members, keyed by Steam id.</param>
    /// <param name="currentMembers">The new snapshot's members, keyed by Steam id.</param>
    /// <returns>The Steam ids of members whose invite was just accepted.</returns>
    private static HashSet<ulong> ComputeAcceptedInvites(
        HashSet<ulong> previousInvites,
        HashSet<ulong> currentInvites,
        Dictionary<ulong, ClanMemberSnapshot> previousMembers,
        Dictionary<ulong, ClanMemberSnapshot> currentMembers) =>
        [.. previousInvites.Where(id =>
            !currentInvites.Contains(id) && currentMembers.ContainsKey(id) && !previousMembers.ContainsKey(id))];
}
