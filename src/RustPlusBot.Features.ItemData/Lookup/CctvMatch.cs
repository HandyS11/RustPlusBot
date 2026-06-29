using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to a CCTV monument.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record CctvMatch
{
    private CctvMatch() { }

    /// <summary>Exactly one monument resolved.</summary>
    /// <param name="Monument">The resolved monument.</param>
    public sealed record Found(CctvMonument Monument) : CctvMatch;

    /// <summary>Several monuments matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate monuments, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<CctvMonument> Candidates) : CctvMatch;

    /// <summary>No monument matched.</summary>
    public sealed record NotFound : CctvMatch;
}
