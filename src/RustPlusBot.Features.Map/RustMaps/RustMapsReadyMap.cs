namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>A generated RustMaps map ready to post: the downloaded image plus its RustMaps page link.</summary>
/// <param name="ImageBytes">The downloaded RustMaps render bytes.</param>
/// <param name="RustMapsUrl">The RustMaps page URL, when available.</param>
public sealed record RustMapsReadyMap(byte[] ImageBytes, string? RustMapsUrl);
