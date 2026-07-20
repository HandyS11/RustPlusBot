using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Players.Messages;

/// <summary>
///     Renders the #info team embed: one line per member with presence, grid reference and how long
///     they have held their current state. Replaces the /team, /online, /offline and /alive commands.
///     Rust caps a team at eight members, so a description list never risks truncation.
/// </summary>
/// <param name="query">Live team query.</param>
/// <param name="afk">Live AFK state.</param>
/// <param name="mapSettings">Supplies the per-server grid convention.</param>
/// <param name="clock">Supplies the current time for durations.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ServerTeamMessageRenderer(
    IRustServerQuery query,
    IAfkState afk,
    IMapSettingsStore mapSettings,
    IClock clock,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "server.team";

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

        var culture = context.Culture;
        var team = await query.GetTeamInfoAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            // Honest over stale: an empty payload would make the reconciler skip the edit and leave
            // the previous roster on screen as though it were current.
            var offline = new EmbedBuilder()
                .WithTitle(localizer.Get("server.team.title", culture, "0", "0"))
                .WithDescription(localizer.Get("server.team.disconnected", culture))
                .WithColor(Color.Red)
                .Build();
            return new MessagePayload(null, offline, null);
        }

        var online = team.Members.Count(m => m.IsOnline);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.team.title", culture,
                online.ToString(CultureInfo.InvariantCulture),
                team.Members.Count.ToString(CultureInfo.InvariantCulture)))
            .WithColor(Color.Blue);

        if (team.Members.Count == 0)
        {
            return new MessagePayload(null,
                embed.WithDescription(localizer.Get("server.team.empty", culture)).Build(), null);
        }

        var afkIds = await ResolveAfkAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var settings = await mapSettings.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var dims = await query.GetMapDimensionsAsync(context.GuildId, serverId, cancellationToken)
            .ConfigureAwait(false);

        var body = new StringBuilder();
        foreach (var member in team.Members.OrderBy(m => RankOf(m, afkIds)).ThenByDescending(SurvivedFor))
        {
            body.Append(LineFor(member, team.LeaderSteamId, afkIds, dims, settings.GridStyle, culture)).Append('\n');
        }

        return new MessagePayload(null, embed.WithDescription(body.ToString().TrimEnd('\n')).Build(), null);
    }

    private async ValueTask<Dictionary<ulong, TimeSpan>> ResolveAfkAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var members = await afk.GetAfkMembersAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return members is null
            ? []
            : members.ToDictionary(m => m.SteamId, m => m.StillFor);
    }

    /// <summary>Sort bucket: online-alive, then AFK, then dead, then offline.</summary>
    /// <param name="member">The member to rank.</param>
    /// <param name="afkIds">Currently-AFK members, keyed by Steam id.</param>
    /// <returns>0 for online-alive, 1 for AFK, 2 for dead, 3 for offline.</returns>
    private static int RankOf(TeamMemberSnapshot member, Dictionary<ulong, TimeSpan> afkIds)
    {
        if (!member.IsOnline)
        {
            return 3;
        }

        if (!member.IsAlive)
        {
            return 2;
        }

        return afkIds.ContainsKey(member.SteamId) ? 1 : 0;
    }

    private TimeSpan SurvivedFor(TeamMemberSnapshot member) => clock.UtcNow - member.LastSpawnTimeUtc;

    private string LineFor(
        TeamMemberSnapshot member,
        ulong leaderSteamId,
        Dictionary<ulong, TimeSpan> afkIds,
        MapDimensions? dims,
        MapGridStyle style,
        string culture)
    {
        var crown = member.SteamId == leaderSteamId ? "👑" : string.Empty;

        // The API can report a member with no display name; fall back to the id rather than a blank.
        var name = string.IsNullOrWhiteSpace(member.Name)
            ? member.SteamId.ToString(CultureInfo.InvariantCulture)
            : member.Name;

        // Offline members report their last-known position, which would read as current.
        var grid = member.IsOnline
            ? GridReference.From(member.X, member.Y, dims, style)
            : localizer.Get("server.team.nogrid", culture);

        var (glyph, state) = StateFor(member, afkIds, culture);

        return localizer.Get("server.team.line", culture, crown, glyph, name, grid, state);
    }

    private (string Glyph, string State) StateFor(
        TeamMemberSnapshot member,
        Dictionary<ulong, TimeSpan> afkIds,
        string culture)
    {
        if (!member.IsOnline)
        {
            // No disconnect timestamp exists on the snapshot, so no duration is reported here.
            return ("⚫", localizer.Get("server.team.offline", culture));
        }

        if (!member.IsAlive)
        {
            return ("💀", localizer.Get("server.team.dead", culture,
                DurationFormat.Compact(clock.UtcNow - member.LastDeathTimeUtc)));
        }

        if (afkIds.TryGetValue(member.SteamId, out var stillFor))
        {
            return ("😴", localizer.Get("server.team.afk", culture, DurationFormat.Compact(stillFor)));
        }

        return ("🟢", localizer.Get("server.team.alive", culture, DurationFormat.Compact(SurvivedFor(member))));
    }
}
