namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>A generated RustMaps map ready to reference: its hosted image URL plus its RustMaps page link.</summary>
/// <param name="ImageUrl">The RustMaps-hosted image URL (the monument-icon render).</param>
/// <param name="RustMapsUrl">The RustMaps page URL, when available.</param>
public sealed record RustMapsReadyMap(string ImageUrl, string? RustMapsUrl);
