namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// The Rust+ monument tokens that belong to the separately-toggleable Tunnels layer rather than the
/// general Monuments layer. Trainyard and Military Tunnels stay ordinary monuments.
/// </summary>
public static class TunnelTokens
{
    /// <summary>The train-tunnel entrance and link tokens.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "train_tunnel_display_name", "train_tunnel_link_display_name",
    };
}
