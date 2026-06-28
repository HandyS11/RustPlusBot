using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to a smelter.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record SmeltMatch
{
    private SmeltMatch() { }

    /// <summary>Exactly one smelter resolved.</summary>
    /// <param name="Smelter">The resolved smelter.</param>
    public sealed record Found(Smelter Smelter) : SmeltMatch;

    /// <summary>Several smelters matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate smelters, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<Smelter> Candidates) : SmeltMatch;

    /// <summary>No smelter matched.</summary>
    public sealed record NotFound : SmeltMatch;
}
