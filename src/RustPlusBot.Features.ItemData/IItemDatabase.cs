using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData;

/// <summary>The single source of truth for bundled Rust item data. Singleton.</summary>
public interface IItemDatabase
{
    /// <summary>Per-section provenance dates, for "data as of" display.</summary>
    DatasetSources Sources { get; }

    /// <summary>Gets the record for an item id, or null if unknown.</summary>
    /// <param name="id">The Rust item id.</param>
    ItemRecord? GetById(int id);
}
