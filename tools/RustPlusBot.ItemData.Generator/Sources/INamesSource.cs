namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides item names, stack sizes, and despawn times.</summary>
internal interface INamesSource
{
    /// <summary>Loads item names keyed by item id.</summary>
    IReadOnlyDictionary<int, string> LoadNames();
}
