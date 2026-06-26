using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to an item.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record ItemMatch
{
    private ItemMatch() { }

    /// <summary>Exactly one item resolved.</summary>
    /// <param name="Item">The resolved item.</param>
    public sealed record Found(ItemRecord Item) : ItemMatch;

    /// <summary>Several items matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate items, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<ItemRecord> Candidates) : ItemMatch;

    /// <summary>No item matched.</summary>
    public sealed record NotFound : ItemMatch;
}
