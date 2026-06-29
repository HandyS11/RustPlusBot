using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides monument CCTV camera codes from the rustplusplus static data.</summary>
internal interface ICctvSource
{
    /// <summary>Loads every monument with at least one camera code.</summary>
    /// <returns>The monuments, in source order, with un-escaped codes.</returns>
    IReadOnlyList<CctvMonument> LoadMonuments();
}
