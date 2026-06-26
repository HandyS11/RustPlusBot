using System.Globalization;

namespace RustPlusBot.Features.ItemData.Naming;

/// <summary>Resolves item names from the <see cref="IItemDatabase"/>. Singleton.</summary>
/// <param name="database">The item database.</param>
public sealed class ItemDatabaseNameResolver(IItemDatabase database) : IItemNameResolver
{
    /// <inheritdoc />
    public string Resolve(int itemId) =>
        database.GetById(itemId)?.Name ?? "Item " + itemId.ToString(CultureInfo.InvariantCulture);
}
