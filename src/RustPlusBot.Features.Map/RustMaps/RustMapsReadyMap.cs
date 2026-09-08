using RustMapsApi.V4.Models;

namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>A generated RustMaps map ready to reference: its hosted image URL plus its RustMaps page link.</summary>
/// <param name="ImageUrl">The RustMaps-hosted image URL (the monument-icon render).</param>
/// <param name="RustMapsUrl">The RustMaps page URL, when available.</param>
/// <param name="Monuments">
/// The monuments RustMaps placed on this render, kept so each requesting server can be checked against it:
/// the render is only shown to a server whose own monuments line up (see <see cref="RustMapsMapMatcher"/>).
/// </param>
public sealed record RustMapsReadyMap(
    string ImageUrl,
    string? RustMapsUrl,
    IReadOnlyList<Monument> Monuments);
