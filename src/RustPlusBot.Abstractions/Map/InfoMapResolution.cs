namespace RustPlusBot.Abstractions.Map;

/// <summary>What the #info map renderer should show for one server.</summary>
public enum InfoMapStatus
{
    /// <summary>Nothing decided yet (no render, or its match against the live server is not verified).</summary>
    Pending = 0,

    /// <summary>A render exists and its monuments line up with the server's — safe to show.</summary>
    Verified = 1,

    /// <summary>
    /// A render exists but depicts a different world (the server runs a pre-generated/custom level, or its
    /// map-gen version has drifted from RustMaps'). It must not be shown; the server's own map is the truth.
    /// </summary>
    Mismatched = 2,
}

/// <summary>The #info map state for a server: the verdict plus the render it applies to.</summary>
/// <param name="Status">Whether a render is available and trustworthy for this server.</param>
/// <param name="View">The render, present only when <see cref="InfoMapStatus.Verified"/>.</param>
public sealed record InfoMapResolution(InfoMapStatus Status, InfoMapView? View)
{
    /// <summary>Nothing to show (yet).</summary>
    public static InfoMapResolution Pending { get; } = new(InfoMapStatus.Pending, null);
}
