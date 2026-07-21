using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Messages;

/// <summary>
///     Renders the anchored #claninfo roster embed: one field per clan role (in rank order) listing
///     that role's members with presence, tenure and officer notes.
/// </summary>
/// <param name="store">Supplies the stored clan snapshot.</param>
/// <param name="names">Resolves member Steam ids to display names.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ClanRosterMessageRenderer(
    IClanStore store,
    IClanNameResolver names,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "clan.roster";

    /// <summary>Discord's hard cap on the length of an embed field value.</summary>
    private const int FieldValueLimit = 1024;

    /// <inheritdoc />
    public string MessageKey => Key;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var clan = await store.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (clan is null)
        {
            // See ClanOverviewMessageRenderer: an empty payload keeps this key inert on clanless servers.
            return new MessagePayload(null, null, null);
        }

        var culture = context.Culture;
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("clan.roster.title", culture,
                clan.Members.Count.ToString(CultureInfo.InvariantCulture)))
            .WithColor(ColorFor(clan.Color));

        if (clan.Members.Count == 0)
        {
            return new MessagePayload(null,
                embed.WithDescription(localizer.Get("clan.roster.empty", culture)).Build(), null);
        }

        // One batched call for the whole roster rather than one per member.
        var resolved = await names
            .ResolveAsync(context.GuildId, serverId, clan.Members.Select(m => m.SteamId).ToHashSet(),
                cancellationToken)
            .ConfigureAwait(false);

        var roles = clan.Roles.ToDictionary(r => r.RoleId);
        foreach (var role in clan.Roles.OrderBy(r => r.Rank).ThenBy(r => r.RoleId))
        {
            var members = Ordered(clan.Members.Where(m => m.RoleId == role.RoleId)).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            embed.AddField(Header(role, culture), Body(members, resolved, culture));
        }

        var orphans = Ordered(clan.Members.Where(m => !roles.ContainsKey(m.RoleId))).ToList();
        if (orphans.Count > 0)
        {
            embed.AddField(localizer.Get("clan.roster.unknownrole", culture), Body(orphans, resolved, culture));
        }

        return new MessagePayload(null, embed.Build(), null);
    }

    private static Color ColorFor(int? argb) =>
        argb is { } packed ? new Color((uint)packed & 0x00FFFFFFu) : Color.DarkGrey;

    private static string Relative(DateTimeOffset moment) =>
        TimestampTag.FromDateTimeOffset(moment, TimestampTagStyles.Relative).ToString();

    /// <summary>Orders a role's members: online first, then by how long they have been in the clan.</summary>
    /// <param name="members">The members to order.</param>
    /// <returns>The ordered members.</returns>
    private static IEnumerable<ClanMemberSnapshot> Ordered(IEnumerable<ClanMemberSnapshot> members) =>
        members.OrderByDescending(m => m.Online).ThenBy(m => m.Joined);

    /// <summary>Builds the field name: the role name followed by the permissions it actually grants.</summary>
    /// <param name="role">The role.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The field name.</returns>
    private string Header(ClanRoleSnapshot role, string culture)
    {
        var flags = new List<string>();
        Add(role.CanSetMotd, "setmotd");
        Add(role.CanSetLogo, "setlogo");
        Add(role.CanInvite, "invite");
        Add(role.CanKick, "kick");
        Add(role.CanPromote, "promote");
        Add(role.CanDemote, "demote");
        Add(role.CanSetPlayerNotes, "setplayernotes");
        Add(role.CanAccessLogs, "accesslogs");
        Add(role.CanAccessScoreEvents, "accessscoreevents");

        return flags.Count == 0
            ? role.Name
            : string.Create(CultureInfo.InvariantCulture, $"{role.Name} — {string.Join(" · ", flags)}");

        void Add(bool granted, string flag)
        {
            if (granted)
            {
                flags.Add(localizer.Get($"clan.roster.perm.{flag}", culture));
            }
        }
    }

    private string Body(
        IReadOnlyList<ClanMemberSnapshot> members,
        IReadOnlyDictionary<ulong, string> resolved,
        string culture)
    {
        var lines = members.Select(m => Line(m, resolved, culture)).ToList();
        var full = string.Join('\n', lines);
        if (full.Length <= FieldValueLimit)
        {
            return full;
        }

        // Discord rejects a field value over 1024 characters, and silently dropping members would
        // misrepresent the roster; truncate at a line boundary and say how many were omitted.
        for (var kept = lines.Count - 1; kept > 0; kept--)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture,
                $"{string.Join('\n', lines.Take(kept))}\n{Truncated(lines.Count - kept, culture)}");
            if (candidate.Length <= FieldValueLimit)
            {
                return candidate;
            }
        }

        return Truncated(lines.Count, culture);
    }

    private string Truncated(int omitted, string culture) =>
        localizer.Get("clan.roster.truncated", culture, omitted.ToString(CultureInfo.InvariantCulture));

    private string Line(
        ClanMemberSnapshot member,
        IReadOnlyDictionary<ulong, string> resolved,
        string culture)
    {
        var name = resolved.TryGetValue(member.SteamId, out var known)
            ? known
            : member.SteamId.ToString(CultureInfo.InvariantCulture);

        var state = member.Online
            ? localizer.Get("clan.roster.joined", culture, Relative(member.Joined))
            : localizer.Get("clan.roster.lastseen", culture, Relative(member.LastSeen));

        var glyph = member.Online ? "🟢" : "⚫";
        var line = string.Create(CultureInfo.InvariantCulture, $"{glyph} {name} {state}");

        return string.IsNullOrWhiteSpace(member.Notes)
            ? line
            : string.Create(CultureInfo.InvariantCulture,
                $"{line} {localizer.Get("clan.roster.notes", culture, member.Notes)}");
    }
}
