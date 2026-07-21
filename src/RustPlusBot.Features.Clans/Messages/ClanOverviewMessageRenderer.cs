using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Clans.Messages;

/// <summary>
///     Renders the anchored #claninfo overview embed: name, score, member count, age, leadership and
///     the message of the day, plus a Set MOTD button for players whose clan role allows it.
/// </summary>
/// <param name="store">Supplies the stored clan snapshot.</param>
/// <param name="names">Resolves member Steam ids to display names.</param>
/// <param name="connections">Supplies the server's active player credential.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ClanOverviewMessageRenderer(
    IClanStore store,
    IClanNameResolver names,
    IConnectionStore connections,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "clan.overview";

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
            // The clan channel only exists while a clan does, so there is no provisioned message to
            // edit here. An empty payload keeps this key inert on clanless servers.
            return new MessagePayload(null, null, null);
        }

        var culture = context.Culture;
        var leader = LeaderOf(clan);
        var resolved = await ResolveNamesAsync(context.GuildId, serverId, clan, leader, cancellationToken)
            .ConfigureAwait(false);

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("clan.overview.title", culture, clan.Name))
            .WithColor(ColorFor(clan.Color))
            .AddField(localizer.Get("clan.overview.score", culture),
                clan.Score?.ToString(CultureInfo.InvariantCulture)
                ?? localizer.Get("clan.overview.unknown", culture))
            .AddField(localizer.Get("clan.overview.members", culture), MemberCount(clan))
            .AddField(localizer.Get("clan.overview.created", culture), Relative(clan.Created));

        if (leader is not null)
        {
            embed.AddField(localizer.Get("clan.overview.leader", culture), Name(resolved, leader.SteamId));
        }

        if (leader is null || leader.SteamId != clan.Creator)
        {
            embed.AddField(localizer.Get("clan.overview.creator", culture), Name(resolved, clan.Creator));
        }

        embed.AddField(localizer.Get("clan.overview.motd", culture), Motd(clan, resolved, culture));

        var components = await BuildComponentsAsync(context.GuildId, serverId, clan, culture, cancellationToken)
            .ConfigureAwait(false);

        return new MessagePayload(null, embed.Build(), components);
    }

    /// <summary>The member holding the lowest-<c>Rank</c> role, or null when no member has a known role.</summary>
    /// <param name="clan">The clan snapshot.</param>
    /// <returns>The leading member, or null.</returns>
    private static ClanMemberSnapshot? LeaderOf(ClanSnapshot clan)
    {
        var ranks = clan.Roles.ToDictionary(r => r.RoleId, r => r.Rank);
        return clan.Members
            .Where(m => ranks.ContainsKey(m.RoleId))
            .OrderBy(m => ranks[m.RoleId])
            .ThenBy(m => m.Joined)
            .FirstOrDefault();
    }

    private static Color ColorFor(int? argb) =>
        argb is { } packed ? new Color((uint)packed & 0x00FFFFFFu) : Color.DarkGrey;

    private static string Relative(DateTimeOffset moment) =>
        TimestampTag.FromDateTimeOffset(moment, TimestampTagStyles.Relative).ToString();

    private static string Name(IReadOnlyDictionary<ulong, string> resolved, ulong steamId) =>
        resolved.TryGetValue(steamId, out var name)
            ? name
            : steamId.ToString(CultureInfo.InvariantCulture);

    private static string MemberCount(ClanSnapshot clan)
    {
        var count = clan.Members.Count.ToString(CultureInfo.InvariantCulture);
        return clan.MaxMemberCount is { } max
            ? string.Create(CultureInfo.InvariantCulture, $"{count}/{max}")
            : count;
    }

    private Task<IReadOnlyDictionary<ulong, string>> ResolveNamesAsync(
        ulong guildId,
        Guid serverId,
        ClanSnapshot clan,
        ClanMemberSnapshot? leader,
        CancellationToken cancellationToken)
    {
        // One batched call: the embed needs at most the leader, the creator and the MOTD author.
        var ids = new HashSet<ulong> { clan.Creator };
        if (leader is not null)
        {
            ids.Add(leader.SteamId);
        }

        if (clan.MotdAuthor is { } author)
        {
            ids.Add(author);
        }

        return names.ResolveAsync(guildId, serverId, ids, cancellationToken);
    }

    private string Motd(ClanSnapshot clan, IReadOnlyDictionary<ulong, string> resolved, string culture)
    {
        if (string.IsNullOrWhiteSpace(clan.Motd))
        {
            return localizer.Get("clan.overview.nomotd", culture);
        }

        if (clan.MotdAuthor is not { } author || clan.MotdTimestamp is not { } changed)
        {
            return clan.Motd;
        }

        var by = localizer.Get("clan.overview.motdby", culture, Name(resolved, author), Relative(changed));
        return string.Create(CultureInfo.InvariantCulture, $"{clan.Motd}\n{by}");
    }

    /// <summary>
    ///     Builds the Set MOTD button, but only when the server's active player is a clan member whose
    ///     role carries <c>CanSetMotd</c>. Showing an action the API will reject is worse than hiding it.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="clan">The clan snapshot.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The components, or null when the button must be hidden.</returns>
    private async Task<MessageComponent?> BuildComponentsAsync(
        ulong guildId,
        Guid serverId,
        ClanSnapshot clan,
        string culture,
        CancellationToken cancellationToken)
    {
        var credential = await connections.GetActiveCredentialAsync(guildId, serverId, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        var member = clan.Members.FirstOrDefault(m => m.SteamId == credential.SteamId);
        if (member is null)
        {
            return null;
        }

        var role = clan.Roles.FirstOrDefault(r => r.RoleId == member.RoleId);
        if (role is not { CanSetMotd: true })
        {
            return null;
        }

        return new ComponentBuilder()
            .WithButton(
                localizer.Get("clan.overview.setmotd", culture),
                $"{ClanComponentIds.SetMotdButtonPrefix}{serverId}",
                ButtonStyle.Primary,
                row: 0)
            .Build();
    }
}
