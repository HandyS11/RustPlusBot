namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>
/// The in-memory string catalog for Smart Alarms: culture → (key → value).
/// English is the fallback. Intended to be passed directly to the shared
/// <c>RustPlusBot.Discord.Localization.ILocalizer</c> constructor.
/// </summary>
internal static class AlarmLocalizationCatalog
{
    /// <summary>
    /// The built-in EN/FR catalog keyed by BCP-47 primary language tag.
    /// Shape: <c>culture → key → localized string</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Default { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alarm.status.armed"] = "🔔 Armed",
                ["alarm.status.active"] = "🚨 Active",
                ["alarm.status.unreachable"] = "⚠️ Unreachable",
                ["alarm.button.ping.on"] = "Ping @everyone: on",
                ["alarm.button.ping.off"] = "Ping @everyone: off",
                ["alarm.button.relay.on"] = "Relay to team chat: on",
                ["alarm.button.relay.off"] = "Relay to team chat: off",
                ["alarm.button.rename"] = "Rename",
                ["alarm.embed.footer"] = "Entity {0}",
                ["alarm.embed.nevertriggered"] = "Never triggered",
                ["alarm.embed.lasttriggered"] = "Last triggered {0} ago",
                ["alarm.prompt.title"] = "New alarm detected",
                ["alarm.prompt.body"] = "Detected a new Smart Alarm ({0}). Add it?",
                ["alarm.prompt.accept"] = "Accept",
                ["alarm.prompt.dismiss"] = "Dismiss",
                ["alarm.rename.modal.title"] = "Rename alarm",
                ["alarm.rename.input.label"] = "Alarm name",
                ["alarm.triggered.teamchat"] = "🚨 {0} triggered",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alarm.status.armed"] = "🔔 Armée",
                ["alarm.status.active"] = "🚨 Active",
                ["alarm.status.unreachable"] = "⚠️ Injoignable",
                ["alarm.button.ping.on"] = "Ping @everyone : activé",
                ["alarm.button.ping.off"] = "Ping @everyone : désactivé",
                ["alarm.button.relay.on"] = "Relais tchat équipe : activé",
                ["alarm.button.relay.off"] = "Relais tchat équipe : désactivé",
                ["alarm.button.rename"] = "Renommer",
                ["alarm.embed.footer"] = "Entité {0}",
                ["alarm.embed.nevertriggered"] = "Jamais déclenchée",
                ["alarm.embed.lasttriggered"] = "Déclenchée il y a {0}",
                ["alarm.prompt.title"] = "Nouvelle alarme détectée",
                ["alarm.prompt.body"] = "Nouvelle alarme connectée détectée ({0}). L'ajouter ?",
                ["alarm.prompt.accept"] = "Accepter",
                ["alarm.prompt.dismiss"] = "Ignorer",
                ["alarm.rename.modal.title"] = "Renommer l'alarme",
                ["alarm.rename.input.label"] = "Nom de l'alarme",
                ["alarm.triggered.teamchat"] = "🚨 {0} déclenchée",
            },
        };
}
