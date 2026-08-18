namespace RustPlusBot.Abstractions.Vending;

/// <summary>
/// The identity of a tradeable listing. Prices are only ever compared within one key, which is what
/// keeps scrap from being compared to cloth and a blueprint from being compared to the item itself.
/// </summary>
/// <param name="ItemId">The Rust item id being sold.</param>
/// <param name="ItemIsBlueprint">True when the item sold is a blueprint.</param>
/// <param name="CurrencyId">The Rust item id accepted as payment.</param>
/// <param name="CurrencyIsBlueprint">True when the currency is a blueprint.</param>
public readonly record struct ListingKey(
    int ItemId,
    bool ItemIsBlueprint,
    int CurrencyId,
    bool CurrencyIsBlueprint);
