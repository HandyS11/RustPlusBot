namespace RustPlusBot.Features.Vending;

/// <summary>Tuning for the vending feature.</summary>
public sealed class VendingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Vending";

    /// <summary>
    /// Maximum live notifications per server, applied separately to undercut and sell-out messages so a
    /// busy undercut situation cannot starve alerts about the team's own shop. Excess is logged, never
    /// silently dropped.
    /// </summary>
    public int MaxNotificationsPerServer { get; set; } = 50;
}
