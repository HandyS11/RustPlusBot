namespace RustPlusBot.Features.Switches.Rendering;

/// <summary>The in-memory string catalog for Smart Switches: culture -> (key -> value). English is the fallback.</summary>
internal sealed class SwitchLocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static SwitchLocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["switch.status.on"] = "⚡ ON",
                ["switch.status.off"] = "⭘ OFF",
                ["switch.status.unreachable"] = "⚠️ Unreachable",
                ["switch.button.on"] = "Turn on",
                ["switch.button.off"] = "Turn off",
                ["switch.button.strobe"] = "Strobe",
                ["switch.button.rename"] = "Rename",
                ["switch.embed.footer"] = "Entity {0}",
                ["switch.prompt.title"] = "New switch detected",
                ["switch.prompt.body"] = "Detected a new Smart Switch ({0}). Add it?",
                ["switch.prompt.accept"] = "Accept",
                ["switch.prompt.dismiss"] = "Dismiss",
                ["switch.prompt.dismissed"] = "Dismissed.",
                ["switch.rename.modal.title"] = "Rename switch",
                ["switch.rename.input.label"] = "Switch name",
                ["switch.unreachable.ephemeral"] = "Switch is unreachable right now.",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["switch.status.on"] = "⚡ ALLUMÉ",
                ["switch.status.off"] = "⭘ ÉTEINT",
                ["switch.status.unreachable"] = "⚠️ Injoignable",
                ["switch.button.on"] = "Allumer",
                ["switch.button.off"] = "Éteindre",
                ["switch.button.strobe"] = "Stroboscope",
                ["switch.button.rename"] = "Renommer",
                ["switch.embed.footer"] = "Entité {0}",
                ["switch.prompt.title"] = "Nouvel interrupteur détecté",
                ["switch.prompt.body"] = "Nouvel interrupteur connecté détecté ({0}). L'ajouter ?",
                ["switch.prompt.accept"] = "Accepter",
                ["switch.prompt.dismiss"] = "Ignorer",
                ["switch.prompt.dismissed"] = "Ignoré.",
                ["switch.rename.modal.title"] = "Renommer l'interrupteur",
                ["switch.rename.input.label"] = "Nom de l'interrupteur",
                ["switch.unreachable.ephemeral"] = "L'interrupteur est injoignable pour le moment.",
            },
        },
    };
}
