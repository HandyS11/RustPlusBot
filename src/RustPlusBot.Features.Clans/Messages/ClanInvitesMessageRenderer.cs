using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Messages;

/// <summary>
///     Renders the anchored #claninfo pending-invites embed. Renders empty when there are no pending
///     invites so the reconciler never posts the message at all.
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
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var clan = await store.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (clan is null || clan.Invites.Count == 0)
        {
            // See ClanOverviewMessageRenderer: an empty payload keeps this key inert, both on clanless
            // servers and when there is simply nothing pending.
            return new MessagePayload(null, null, null);
        }

        var culture = context.Culture;

        // One batched call covering both sides of every invite.
        var ids = new HashSet<ulong>();
        foreach (var invite in clan.Invites)
        {
            ids.Add(invite.SteamId);
            ids.Add(invite.Recruiter);
        }

        var resolved = await names.ResolveAsync(context.GuildId, serverId, ids, cancellationToken)
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

    private static string Name(IReadOnlyDictionary<ulong, string> resolved, ulong steamId) =>
        resolved.TryGetValue(steamId, out var name)
            ? name
            : steamId.ToString(CultureInfo.InvariantCulture);
}
