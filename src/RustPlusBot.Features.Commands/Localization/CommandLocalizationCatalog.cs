namespace RustPlusBot.Features.Commands.Localization;

/// <summary>The in-memory string catalog for command replies: culture -> (key -> value). English is the fallback.</summary>
internal sealed class CommandLocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static CommandLocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["command.mute.done"] = "Bot muted.",
                ["command.unmute.done"] = "Bot unmuted.",
                ["command.uptime.ok"] = "Uptime: {0}",
                ["command.pop.ok"] = "Pop: {0}/{1} ({2} queued)",
                ["command.time.ok"] = "Time: {0} — {1}",
                ["command.time.day"] = "day",
                ["command.time.night"] = "night",
                ["command.wipe.ok"] = "Wiped {0} ago",
                ["command.notconnected"] = "Not connected to the server.",
                ["command.wipe.unknown"] = "Wipe time is unknown.",
                ["command.online.ok"] = "Online ({0}): {1}",
                ["command.online.none"] = "No one is online.",
                ["command.offline.ok"] = "Offline ({0}): {1}",
                ["command.offline.none"] = "Everyone is online.",
                ["command.team.ok"] = "Team ({0}): {1}",
                ["command.team.none"] = "No team members.",
                ["command.team.nomatch"] = "No teammate matches '{0}'.",
                ["command.steamid.ok"] = "{0}",
                ["command.alive.ok"] = "Alive: {0}",
                ["command.alive.dead"] = "{0} dead",
                ["command.alive.member"] = "{0} {1}",
                ["command.prox.ok"] = "Prox: {0}",
                ["command.prox.member"] = "{0} {1}m",
                ["command.prox.selfunknown"] = "Can't locate you.",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["command.mute.done"] = "Bot mis en sourdine.",
                ["command.unmute.done"] = "Bot réactivé.",
                ["command.uptime.ok"] = "Disponibilité : {0}",
                ["command.pop.ok"] = "Population : {0}/{1} ({2} en file)",
                ["command.time.ok"] = "Heure : {0} — {1}",
                ["command.time.day"] = "jour",
                ["command.time.night"] = "nuit",
                ["command.wipe.ok"] = "Wipe il y a {0}",
                ["command.notconnected"] = "Non connecté au serveur.",
                ["command.wipe.unknown"] = "Heure de wipe inconnue.",
                ["command.online.ok"] = "En ligne ({0}) : {1}",
                ["command.online.none"] = "Personne n'est en ligne.",
                ["command.offline.ok"] = "Hors ligne ({0}) : {1}",
                ["command.offline.none"] = "Tout le monde est en ligne.",
                ["command.team.ok"] = "Équipe ({0}) : {1}",
                ["command.team.none"] = "Aucun membre d'équipe.",
                ["command.team.nomatch"] = "Aucun coéquipier ne correspond à « {0} ».",
                ["command.steamid.ok"] = "{0}",
                ["command.alive.ok"] = "En vie : {0}",
                ["command.alive.dead"] = "{0} mort",
                ["command.alive.member"] = "{0} {1}",
                ["command.prox.ok"] = "Prox : {0}",
                ["command.prox.member"] = "{0} {1}m",
                ["command.prox.selfunknown"] = "Impossible de vous localiser.",
            },
        },
    };
}
