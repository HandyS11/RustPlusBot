namespace RustPlusBot.Abstractions.Map;

/// <summary>A ready RustMaps map for the #info map message: its hosted image URL plus its page link.</summary>
/// <param name="ImageUrl">The RustMaps-hosted image URL (the monument-icon render).</param>
/// <param name="RustMapsPageUrl">The RustMaps page URL for the map, when available.</param>
public sealed record InfoMapView(string ImageUrl, string? RustMapsPageUrl);
