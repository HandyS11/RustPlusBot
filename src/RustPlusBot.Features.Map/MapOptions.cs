namespace RustPlusBot.Features.Map;

/// <summary>Map feature configuration, bound from the "Map" config section.</summary>
public sealed class MapOptions
{
    /// <summary>Minimum time between #map image re-renders per server (coalesces rapid marker changes). Default 30s.</summary>
    public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>RustMaps integration settings.</summary>
    public RustMapsOptions RustMaps { get; set; } = new();
}

/// <summary>RustMaps settings. The integration is inactive when no API key is configured.</summary>
public sealed class RustMapsOptions
{
    /// <summary>How often the background service checks generation progress. Default 20s.</summary>
    public TimeSpan GenerationPollInterval { get; set; } = TimeSpan.FromSeconds(20);
}
