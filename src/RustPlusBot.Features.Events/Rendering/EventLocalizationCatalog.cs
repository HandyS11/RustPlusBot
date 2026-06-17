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
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["event.cargo.entered"] = "🚢 Cargo Ship arrivé en {0}",
                ["event.cargo.left"] = "🚢 Cargo Ship parti ({0})",
                ["event.heli.entered"] = "🚁 Hélicoptère de patrouille arrivé en {0}",
                ["event.heli.left"] = "🚁 Hélicoptère de patrouille parti ({0})",
                ["event.chinook.spawned"] = "🚁 Chinook apparu en {0}",
                ["event.title"] = "Événement",
            },
        },
    };
}
