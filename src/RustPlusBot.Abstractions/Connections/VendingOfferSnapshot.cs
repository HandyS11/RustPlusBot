namespace RustPlusBot.Abstractions.Connections;

/// <summary>One sell order listed by a player vending machine.</summary>
/// <param name="ItemId">The Rust item id being sold.</param>
/// <param name="ItemIsBlueprint">True when the item sold is a blueprint.</param>
/// <param name="Quantity">How many items one order yields (RustPlusApi <c>StackSize</c>); always at least 1.</param>
/// <param name="CurrencyId">The Rust item id accepted as payment.</param>
/// <param name="CurrencyIsBlueprint">True when the currency is a blueprint.</param>
/// <param name="CostPerOrder">The currency amount charged for one order (RustPlusApi <c>CostPerStack</c>).</param>
/// <param name="AmountInStock">Orders remaining (RustPlusApi <c>StackSizeAmount</c>); 0 means sold out.</param>
public sealed record VendingOfferSnapshot(
    int ItemId,
    bool ItemIsBlueprint,
    int Quantity,
    int CurrencyId,
    bool CurrencyIsBlueprint,
    int CostPerOrder,
    int AmountInStock);
