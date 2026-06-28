using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData;

/// <summary>The single source of truth for bundled Rust item data. Singleton.</summary>
public interface IItemDatabase
{
    /// <summary>Per-section provenance dates, for "data as of" display.</summary>
    DatasetSources Sources { get; }

    /// <summary>Gets the record for an item id, or null if unknown.</summary>
    /// <param name="id">The Rust item id.</param>
    ItemRecord? GetById(int id);

    /// <summary>Resolves a user query (name or id) to an item.</summary>
    /// <param name="query">The raw user input.</param>
    /// <returns>A match describing the outcome.</returns>
    ItemMatch Resolve(string query);

    /// <summary>Resolves a user query (target name or id) to a raid target.</summary>
    /// <param name="query">The raw user input.</param>
    /// <returns>A match describing the outcome.</returns>
    RaidMatch ResolveRaidTarget(string query);
}
