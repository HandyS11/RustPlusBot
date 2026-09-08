using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Messages;

/// <summary>
///     Renders the anchored #claninfo pending-invites embed. Renders empty only where there is no
///     clan to describe, so the reconciler never posts the message at all on a clanless server.
/// </summary>
/// <param name="store">Supplies the stored clan snapshot.</param>
/// <param name="names">Resolves invitee and recruiter Steam ids to display names.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ClanInvitesMessageRenderer(
    IClanStore store,
    IClanNameResolver names,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "clan.invites";

    /// <inheritdoc />
    public string MessageKey => Key;

    /// <inheritdoc />
    public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken) =>
        ClanMessageShell.RenderAsync(store, context,
            (clan, serverId, culture) =>
                RenderInvitesAsync(context.GuildId, serverId, clan, culture, cancellationToken),
            cancellationToken);

    private async ValueTask<MessagePayload> RenderInvitesAsync(
        ulong guildId,
        Guid serverId,
        ClanSnapshot clan,
        string culture,
        CancellationToken cancellationToken)
    {
        if (clan.Invites.Count == 0)
        {
            // Honest over stale: an empty payload means "leave the previous message on screen", so
            // returning one here would keep a since-resolved invite list pinned forever.
            return new MessagePayload(null, EmptyState(culture), null);
        }

        // One batched call covering both sides of every invite.
        var ids = new HashSet<ulong>();
        foreach (var invite in clan.Invites)
        {
            ids.Add(invite.SteamId);
            ids.Add(invite.Recruiter);
        }

        var resolved = await names.ResolveAsync(guildId, serverId, ids, cancellationToken)
            .ConfigureAwait(false);

        var body = new StringBuilder();
        foreach (var invite in clan.Invites.OrderBy(i => i.Timestamp))
        {
            body.Append(localizer.Get("clan.invites.line", culture,
                    Name(resolved, invite.SteamId),
                    Name(resolved, invite.Recruiter),
                    TimestampTag.FromDateTimeOffset(invite.Timestamp, TimestampTagStyles.Relative).ToString()))
                .Append('\n');
        }

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("clan.invites.title", culture,
                clan.Invites.Count.ToString(CultureInfo.InvariantCulture)))
            .WithColor(Color.Gold)
            .WithDescription(body.ToString().TrimEnd('\n'))
            .Build();

        return new MessagePayload(null, embed, null);
    }

    /// <summary>Builds the "nothing pending" embed shown while a clan is stored but has no invites.</summary>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The empty-state embed.</returns>
    private Embed EmptyState(string culture) =>
        new EmbedBuilder()
            .WithTitle(localizer.Get("clan.invites.title", culture, "0"))
            .WithColor(Color.Gold)
            .WithDescription(localizer.Get("clan.invites.none", culture))
            .Build();

    private static string Name(IReadOnlyDictionary<ulong, string> resolved, ulong steamId) =>
        resolved.TryGetValue(steamId, out var name)
            ? name
            : steamId.ToString(CultureInfo.InvariantCulture);
}
