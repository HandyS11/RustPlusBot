namespace RustPlusBot.Features.ItemData.Naming;

/// <summary>Resolves a Rust item id to a human-readable display name.</summary>
public interface IItemNameResolver
{
    /// <summary>Gets the display name for an item id, or a stable "Item {id}" fallback when unknown.</summary>
    /// <param name="itemId">The Rust item id.</param>
    /// <returns>The display name, or "Item {id}" if not in the bundled lookup.</returns>
    string Resolve(int itemId);
}
