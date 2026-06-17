namespace RustPlusBot.Features.Map;

/// <summary>Map feature configuration, bound from the "Map" config section.</summary>
public sealed class MapOptions
{
    /// <summary>Minimum time between #map image re-renders per server (coalesces rapid marker changes). Default 45s.</summary>
    public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(45);
}
