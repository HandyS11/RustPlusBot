using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to a raid target.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record RaidMatch
{
    private RaidMatch() { }

    /// <summary>Exactly one raid target resolved.</summary>
    /// <param name="Target">The resolved target.</param>
    public sealed record Found(RaidTarget Target) : RaidMatch;

    /// <summary>Several targets matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate targets, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<RaidTarget> Candidates) : RaidMatch;

    /// <summary>No target matched.</summary>
    public sealed record NotFound : RaidMatch;
}
