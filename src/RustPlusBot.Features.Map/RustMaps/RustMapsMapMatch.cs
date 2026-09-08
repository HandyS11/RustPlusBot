namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Whether a RustMaps render actually depicts the world a given server is running.</summary>
public enum RustMapsMapMatch
{
    /// <summary>Not decided yet — the server's own monuments were unavailable or too few to judge.</summary>
    Unknown = 0,

    /// <summary>The render's monuments line up with the server's; the render depicts this world.</summary>
    Match = 1,

    /// <summary>The render depicts a different world (custom/pre-generated level, or a different map-gen version).</summary>
    Mismatch = 2,
}
