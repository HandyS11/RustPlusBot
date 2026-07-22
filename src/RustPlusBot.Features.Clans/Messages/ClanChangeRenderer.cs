using System.Globalization;
using RustPlusBot.Features.Clans.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Clans.Messages;

/// <summary>
/// Turns one detected <see cref="ClanChange"/> into a localized #claninfo feed line. Pure: no I/O
/// and no state, so ordering and throttling stay the caller's concern.
/// </summary>
/// <param name="localizer">String resolution.</param>
internal sealed class ClanChangeRenderer(ILocalizer localizer)
{
    /// <summary>Renders one change into a feed line, or null when it should not be posted.</summary>
    /// <param name="change">The change to render.</param>
    /// <param name="names">Resolved display names covering every id referenced by the change.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The feed line, or null.</returns>
    public string? Render(ClanChange change, IReadOnlyDictionary<ulong, string> names, string culture)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(names);

        return change.Kind switch
        {
            ClanChangeKind.Dissolved => localizer.Get("clan.event.dissolved", culture, change.Text ?? string.Empty),
            ClanChangeKind.Renamed => localizer.Get("clan.event.renamed", culture, change.Text ?? string.Empty),
            ClanChangeKind.MotdChanged => RenderMotd(change, names, culture),
            ClanChangeKind.MemberJoined =>
                localizer.Get("clan.event.joined", culture, Name(names, change.SteamId)),
            ClanChangeKind.MemberLeft =>
                localizer.Get("clan.event.left", culture, Name(names, change.SteamId)),
            ClanChangeKind.MemberPromoted =>
                localizer.Get("clan.event.promoted", culture, Name(names, change.SteamId),
                    change.RoleName ?? string.Empty),
            ClanChangeKind.MemberDemoted =>
                localizer.Get("clan.event.demoted", culture, Name(names, change.SteamId),
                    change.RoleName ?? string.Empty),
            ClanChangeKind.InviteSent =>
                localizer.Get("clan.event.invited", culture, Name(names, change.SteamId),
                    Name(names, change.ActorSteamId)),
            ClanChangeKind.InviteAccepted =>
                localizer.Get("clan.event.inviteaccepted", culture, Name(names, change.SteamId)),
            ClanChangeKind.InviteRevoked =>
                localizer.Get("clan.event.inviterevoked", culture, Name(names, change.SteamId)),
            ClanChangeKind.LogoChanged => localizer.Get("clan.event.logo", culture),
            ClanChangeKind.ColorChanged => localizer.Get("clan.event.color", culture),
            ClanChangeKind.ScoreChanged => localizer.Get("clan.event.score", culture,
                (change.Score ?? 0L).ToString(CultureInfo.InvariantCulture)),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves a display name, falling back to the bare Steam id so a rendering never throws on an
    /// id the resolver did not cover.
    /// </summary>
    /// <param name="names">The resolved names.</param>
    /// <param name="steamId">The id to name, or null.</param>
    /// <returns>The display name, the bare id, or an empty string when there is no id.</returns>
    private static string Name(IReadOnlyDictionary<ulong, string> names, ulong? steamId)
    {
        if (steamId is not { } id)
        {
            return string.Empty;
        }

        return names.TryGetValue(id, out var name) ? name : id.ToString(CultureInfo.InvariantCulture);
    }

    private string RenderMotd(ClanChange change, IReadOnlyDictionary<ulong, string> names, string culture)
    {
        // A cleared MOTD reads as its own event; "set the MOTD to (nothing)" would be nonsense.
        if (string.IsNullOrWhiteSpace(change.Text))
        {
            return localizer.Get("clan.event.motdcleared", culture);
        }

        return localizer.Get("clan.event.motd", culture, Name(names, change.ActorSteamId), change.Text);
    }
}
