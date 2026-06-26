namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides item stack sizes.</summary>
internal interface IStackSource
{
    /// <summary>Loads stack sizes keyed by item id.</summary>
    IReadOnlyDictionary<int, int> LoadStackSizes();
}
