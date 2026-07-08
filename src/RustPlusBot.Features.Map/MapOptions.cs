namespace RustPlusBot.Features.Map;

/// <summary>Map feature configuration, bound from the "Map" config section.</summary>
public sealed class MapOptions
{
    /// <summary>Minimum time between #map image re-renders per server (coalesces rapid marker changes). Default 30s.</summary>
    public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>RustMaps integration settings.</summary>
    public RustMapsOptions RustMaps { get; set; } = new();
}

/// <summary>RustMaps API settings; the integration is inactive when no key is configured.</summary>
public sealed class RustMapsOptions
{
    /// <summary>The RustMaps API key, or null/empty to disable the RustMaps base-map source.</summary>
    public string? ApiKey { get; set; }
}
