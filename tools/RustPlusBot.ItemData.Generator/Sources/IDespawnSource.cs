namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides item despawn times.</summary>
internal interface IDespawnSource
{
    /// <summary>Loads despawn seconds keyed by item id.</summary>
    IReadOnlyDictionary<int, int> LoadDespawnSeconds();
}
