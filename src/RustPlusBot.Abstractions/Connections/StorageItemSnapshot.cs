namespace RustPlusBot.Abstractions.Connections;

/// <summary>One stack inside a storage monitor: the item id, its quantity, and whether it is a blueprint.</summary>
/// <param name="ItemId">The Rust item id (resolve to a display name via the item-name lookup).</param>
/// <param name="Quantity">The stack quantity.</param>
/// <param name="IsBlueprint">True when the stack is a blueprint rather than the item itself.</param>
public sealed record StorageItemSnapshot(int ItemId, int Quantity, bool IsBlueprint);
