namespace RustPlusBot.Features.Map.Composing;

/// <summary>One legend row: a colored square, the player's name, and their status.</summary>
/// <param name="Emoji">The colored-square emoji matching the player's on-map cross color.</param>
/// <param name="Name">The player's in-game display name.</param>
/// <param name="Status">Human-readable status, e.g. "online" or "offline, dead".</param>
public sealed record MapLegendEntry(string Emoji, string Name, string Status);

/// <summary>The player legend for a rendered map, one entry per teammate.</summary>
/// <param name="Entries">The legend rows, ordered as the crosses were assigned (by SteamId).</param>
public sealed record MapLegend(IReadOnlyList<MapLegendEntry> Entries);

/// <summary>A rendered map: the PNG plus its optional player legend.</summary>
/// <param name="Png">The rendered PNG bytes.</param>
/// <param name="Legend">The player legend, or null when the Players layer is off or the team is empty.</param>
public sealed record MapComposition(byte[] Png, MapLegend? Legend);
