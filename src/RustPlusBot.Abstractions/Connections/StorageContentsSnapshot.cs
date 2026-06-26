namespace RustPlusBot.Abstractions.Connections;

/// <summary>A point-in-time read of a storage monitor's contents and protection state.</summary>
/// <param name="Capacity">Total slot count (24 = Tool Cupboard, 48 = large box, 12 = small box), or null if unknown.</param>
/// <param name="HasProtection">For a Tool Cupboard: whether decay protection is active. Null for non-TC monitors.</param>
/// <param name="ProtectionExpiry">When decay protection expires (UTC). Only meaningful when <paramref name="HasProtection"/> is true.</param>
/// <param name="Items">The stacks currently inside the monitor.</param>
public sealed record StorageContentsSnapshot(
    int? Capacity,
    bool? HasProtection,
    DateTimeOffset? ProtectionExpiry,
    IReadOnlyList<StorageItemSnapshot> Items);
