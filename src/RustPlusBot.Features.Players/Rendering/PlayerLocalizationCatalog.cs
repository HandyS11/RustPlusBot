namespace RustPlusBot.Features.Players.Rendering;

/// <summary>The in-memory string catalog for player events: culture -> (key -> value). English is the fallback.</summary>
internal sealed class PlayerLocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static PlayerLocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player.title"] = "Team event",
                ["player.connect"] = "🟢 {0} connected",
                ["player.connect.line"] = "{0} connected",
                ["player.disconnect"] = "🔴 {0} disconnected",
                ["player.disconnect.line"] = "{0} disconnected",
                ["player.death"] = "💀 {0} died at {1}",
                ["player.death.line"] = "{0} died at {1}",
                ["player.death.unknown"] = "💀 {0} died",
                ["player.death.unknown.line"] = "{0} died",
                ["player.respawn"] = "✨ {0} respawned at {1}",
                ["player.respawn.line"] = "{0} respawned at {1}",
                ["player.afk"] = "💤 {0} is AFK ({1})",
                ["player.afk.line"] = "{0} is AFK ({1})",
                ["player.afk.back"] = "👋 {0} is back",
                ["player.afk.back.line"] = "{0} is back",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player.title"] = "Événement d'équipe",
                ["player.connect"] = "🟢 {0} s'est connecté",
                ["player.connect.line"] = "{0} s'est connecté",
                ["player.disconnect"] = "🔴 {0} s'est déconnecté",
                ["player.disconnect.line"] = "{0} s'est déconnecté",
                ["player.death"] = "💀 {0} est mort en {1}",
                ["player.death.line"] = "{0} est mort en {1}",
                ["player.death.unknown"] = "💀 {0} est mort",
                ["player.death.unknown.line"] = "{0} est mort",
                ["player.respawn"] = "✨ {0} a réapparu en {1}",
                ["player.respawn.line"] = "{0} a réapparu en {1}",
                ["player.afk"] = "💤 {0} est AFK ({1})",
                ["player.afk.line"] = "{0} est AFK ({1})",
                ["player.afk.back"] = "👋 {0} est de retour",
                ["player.afk.back.line"] = "{0} est de retour",
            },
        },
    };
}
