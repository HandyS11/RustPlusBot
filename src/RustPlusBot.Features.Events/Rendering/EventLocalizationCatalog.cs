namespace RustPlusBot.Features.Events.Rendering;

/// <summary>The in-memory string catalog for live events: culture -> (key -> value). English is the fallback.</summary>
internal sealed class EventLocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static EventLocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["event.cargo.entered"] = "🚢 Cargo Ship entered at {0}",
                ["event.cargo.left"] = "🚢 Cargo Ship left ({0})",
                ["event.heli.entered"] = "🚁 Patrol Helicopter entered at {0}",
                ["event.heli.left"] = "🚁 Patrol Helicopter left ({0})",
                ["event.chinook.spawned"] = "🚁 Chinook spawned at {0}",
                ["event.title"] = "Live event",
                ["event.cargo.entered.line"] = "Cargo Ship entered at {0}",
                ["event.cargo.left.line"] = "Cargo Ship left ({0})",
                ["event.heli.entered.line"] = "Patrol Helicopter entered at {0}",
                ["event.heli.left.line"] = "Patrol Helicopter left ({0})",
                ["event.chinook.spawned.line"] = "Chinook spawned at {0}",
                ["event.rig.small.activated"] = "🛢️ Small Oil Rig activated — combat phase, crate lootable soon ({0})",
                ["event.rig.small.lootable"] = "🛢️ Small Oil Rig — crate is now LOOTABLE ({0})",
                ["event.rig.small.respawned"] = "🛢️ Small Oil Rig — crate respawned, armed again ({0})",
                ["event.rig.large.activated"] = "🛢️ Large Oil Rig activated — combat phase, crate lootable soon ({0})",
                ["event.rig.large.lootable"] = "🛢️ Large Oil Rig — crate is now LOOTABLE ({0})",
                ["event.rig.large.respawned"] = "🛢️ Large Oil Rig — crate respawned, armed again ({0})",
                ["event.rig.small.activated.line"] = "Small Oil Rig activated — combat phase ({0})",
                ["event.rig.small.lootable.line"] = "Small Oil Rig — crate is now lootable ({0})",
                ["event.rig.small.respawned.line"] = "Small Oil Rig — crate respawned ({0})",
                ["event.rig.large.activated.line"] = "Large Oil Rig activated — combat phase ({0})",
                ["event.rig.large.lootable.line"] = "Large Oil Rig — crate is now lootable ({0})",
                ["event.rig.large.respawned.line"] = "Large Oil Rig — crate respawned ({0})",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["event.cargo.entered"] = "🚢 Cargo Ship arrivé en {0}",
                ["event.cargo.left"] = "🚢 Cargo Ship parti ({0})",
                ["event.heli.entered"] = "🚁 Hélicoptère de patrouille arrivé en {0}",
                ["event.heli.left"] = "🚁 Hélicoptère de patrouille parti ({0})",
                ["event.chinook.spawned"] = "🚁 Chinook apparu en {0}",
                ["event.title"] = "Événement",
                ["event.cargo.entered.line"] = "Cargo Ship arrivé en {0}",
                ["event.cargo.left.line"] = "Cargo Ship parti ({0})",
                ["event.heli.entered.line"] = "Hélicoptère de patrouille arrivé en {0}",
                ["event.heli.left.line"] = "Hélicoptère de patrouille parti ({0})",
                ["event.chinook.spawned.line"] = "Chinook apparu en {0}",
                ["event.rig.small.activated"] =
                    "🛢️ Petite plateforme pétrolière activée — phase de combat, caisse bientôt lootable ({0})",
                ["event.rig.small.lootable"] = "🛢️ Petite plateforme pétrolière — caisse LOOTABLE ({0})",
                ["event.rig.small.respawned"] = "🛢️ Petite plateforme pétrolière — caisse réapparue, réarmée ({0})",
                ["event.rig.large.activated"] =
                    "🛢️ Grande plateforme pétrolière activée — phase de combat, caisse bientôt lootable ({0})",
                ["event.rig.large.lootable"] = "🛢️ Grande plateforme pétrolière — caisse LOOTABLE ({0})",
                ["event.rig.large.respawned"] = "🛢️ Grande plateforme pétrolière — caisse réapparue, réarmée ({0})",
                ["event.rig.small.activated.line"] = "Petite plateforme activée — phase de combat ({0})",
                ["event.rig.small.lootable.line"] = "Petite plateforme — caisse lootable ({0})",
                ["event.rig.small.respawned.line"] = "Petite plateforme — caisse réapparue ({0})",
                ["event.rig.large.activated.line"] = "Grande plateforme activée — phase de combat ({0})",
                ["event.rig.large.lootable.line"] = "Grande plateforme — caisse lootable ({0})",
                ["event.rig.large.respawned.line"] = "Grande plateforme — caisse réapparue ({0})",
            },
        },
    };
}
